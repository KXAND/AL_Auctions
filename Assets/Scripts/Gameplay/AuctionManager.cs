using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AuctionGame
{
    public sealed partial class AuctionManager : MonoBehaviour
    {
        private readonly Dictionary<IAuctionController, string> _controllers = new();
        private readonly Dictionary<string, IAuctionController> _controllersByPlayer = new();
        private readonly Dictionary<string, PendingRequest> _pendingRequests = new Dictionary<string, PendingRequest>();
        private readonly Dictionary<IAuctionController, PendingRequest> _latestRequestByController = new Dictionary<IAuctionController, PendingRequest>();
        private readonly Dictionary<string, VisibleRecord> _visibleRecords = new Dictionary<string, VisibleRecord>();
        private readonly Dictionary<string, long> _lastStateVersions = new Dictionary<string, long>();

        private Authority _localAuthority;
        private bool _acceptingRequests;

        public AuctionManagerMode Mode { get; private set; }
        public string ConnectedPlayerIdentity { get; private set; }

        public VisibleRecord HumanVisibleRecord
        {
            get
            {
                foreach (KeyValuePair<IAuctionController, string> pair in _controllers)
                {
                    if (pair.Key is HumanController &&
                        _visibleRecords.TryGetValue(pair.Value, out VisibleRecord record))
                    {
                        return record;
                    }
                }
                return null;
            }
        }

        public event Action<VisibleRecord> VisibleRecordChanged;
        public event Action<RequestStateChange> RequestStateChanged;
        public event Action<string> ConnectionChanged;


        public void StartLocal(Authority authority)
        {
            StartWithLocalAuthority(authority, AuctionManagerMode.Local);
            _acceptingRequests = true;
        }

        public void StartServerAI(Authority authority)
        {
            StartWithLocalAuthority(authority, AuctionManagerMode.ServerAI);
            _acceptingRequests = true;
        }

        public void RegisterController(IAuctionController controller, string playerIdentity)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }
            if (string.IsNullOrWhiteSpace(playerIdentity))
            {
                throw new ArgumentException("玩家身份不能为空。", nameof(playerIdentity));
            }
            if (_controllers.ContainsKey(controller) || _controllersByPlayer.ContainsKey(playerIdentity))
            {
                throw new InvalidOperationException("Controller 或玩家身份已经注册。");
            }

            _controllers[controller] = playerIdentity;
            _controllersByPlayer[playerIdentity] = controller;
            if (_visibleRecords.TryGetValue(playerIdentity, out VisibleRecord record))
            {
                if (controller is AIController ai)
                {
                    ai.ReceiveVisibleRecord(record);
                }
            }
        }

        public void UnregisterController(IAuctionController controller)
        {
            if (controller == null || !_controllers.TryGetValue(controller, out string identity))
            {
                return;
            }
            _controllers.Remove(controller);
            _controllersByPlayer.Remove(identity);
            _latestRequestByController.Remove(controller);
        }

        public void RequestAction(IAuctionController controller, AuctionActionType actionType, int actionValue)
        {
            if (!_acceptingRequests)
            {
                throw new InvalidOperationException("当前连接不接受新请求。");
            }

            if (!_controllers.TryGetValue(controller, out string playerIdentity))
            {
                throw new InvalidOperationException("Controller 尚未注册可信玩家身份。");
            }

            if (!_visibleRecords.TryGetValue(playerIdentity, out VisibleRecord record))
            {
                throw new InvalidOperationException("尚未取得 Authority 可见状态。");
            }

            if (_latestRequestByController.TryGetValue(controller, out PendingRequest existing) &&
                (existing.State == PendingRequestState.Pending || existing.State == PendingRequestState.Unknown) &&
                existing.Request.RoundId == record.Round &&
                (existing.Request.ActionType == AuctionActionType.Clue && record.Phase == MatchPhase.Analysis ||
               existing.Request.ActionType == AuctionActionType.Bid && record.Phase == MatchPhase.Bidding))
            {
                throw new InvalidOperationException("当前阶段已有未确认请求。");
            }

            if (actionType == AuctionActionType.Bid && actionValue < 0)
            {
                throw new FormatException("出价必须是非负整数最小货币单位。");
            }

            string requestId = Guid.NewGuid().ToString("N");
            ActionRequest request = actionType == AuctionActionType.Bid
                ? ActionRequest.CreateBid(requestId, record.MatchId, record.Round, actionValue)
                : ActionRequest.CreateClue(requestId, record.MatchId, record.Round, actionValue);
            PendingRequest pending = new PendingRequest(request, controller);
            _pendingRequests[requestId] = pending;
            _latestRequestByController[controller] = pending;
            PublishRequestState(playerIdentity, pending);

            if (Mode == AuctionManagerMode.Local || Mode == AuctionManagerMode.ServerAI)
            {
                _localAuthority.HandleRequest(playerIdentity, request);
            }
            else
            {
                SendToServer(AuctionFusionProtocol.Action, request);
            }
        }

        public void Update()
        {
            TimeSpan deltaTime = TimeSpan.FromSeconds(Time.deltaTime);

            if (deltaTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }
            if (Mode == AuctionManagerMode.Local)
            {
                _localAuthority?.AdvanceTime(deltaTime);
            }
            if (Mode == AuctionManagerMode.OnlineClient)
            {
                UpdateReconnect(deltaTime);
                UpdateOnlineVisibleTimes(deltaTime);
            }

            foreach (PendingRequest pending in _pendingRequests.Values.Where(item => item.State == PendingRequestState.Pending).ToArray())
            {
                pending.PendingTime += deltaTime;
                if (pending.PendingTime <= GlobalSettings.RequestTimeout)
                {
                    continue;
                }
                pending.State = PendingRequestState.Unknown;
                string playerIdentity = _controllers[pending.Controller];
                PublishRequestState(playerIdentity, pending);
                if (Mode == AuctionManagerMode.OnlineClient)
                {
                    SendToServer(AuctionFusionProtocol.QueryState, null);
                }
            }

            foreach (IAuctionController controller in _controllers.Keys.ToArray())
            {
                if (controller is AIController ai)
                {
                    ai.Update(deltaTime);
                }
            }
        }

        public VisibleRecord GetVisibleRecord(string playerIdentity)
        {
            return _visibleRecords.TryGetValue(playerIdentity, out VisibleRecord record) ? record : null;
        }

        public string GetPlayerIdentity(IAuctionController controller)
        {
            return controller != null && _controllers.TryGetValue(controller, out string identity) ? identity : null;
        }

        public void Disconnect()
        {
            _acceptingRequests = false;
            DisconnectRunner(true);

            if (_localAuthority != null)
            {
                _localAuthority.ResultCreated -= OnAuthorityResult;
                _localAuthority.StateCreated -= OnAuthorityState;
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        private void StartWithLocalAuthority(Authority authority, AuctionManagerMode mode)
        {
            if (Mode != AuctionManagerMode.None)
            {
                throw new InvalidOperationException("AuctionManager 已经启动。");
            }
            _localAuthority = authority ?? throw new ArgumentNullException(nameof(authority));
            Mode = mode;
            authority.ResultCreated += OnAuthorityResult;
            authority.StateCreated += OnAuthorityState;
        }

        private bool IsOlderThanCurrent(string playerIdentity, VisibleRecord candidate)
        {
            if (!_visibleRecords.TryGetValue(playerIdentity, out VisibleRecord current) || current.MatchId != candidate.MatchId)
            {
                return false;
            }
            if (candidate.Round != current.Round)
            {
                return candidate.Round < current.Round;
            }
            return PhaseOrder(candidate.Phase) < PhaseOrder(current.Phase);
        }
        private static int PhaseOrder(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Analysis:
                    return 1;
                case MatchPhase.Bidding:
                    return 2;
                case MatchPhase.Revealing:
                    return 3;
                case MatchPhase.Settlement:
                    return 4;
                default:
                    return 0;
            }
        }
        private void OnAuthorityResult(AuthorityResult result)
        {
            if (result == null)
            {
                return;
            }
            if (_pendingRequests.TryGetValue(result.RequestId, out PendingRequest pending))
            {
                pending.State = result.Accepted ? PendingRequestState.Accepted : PendingRequestState.Rejected;
                pending.Reason = result.RejectedReason;
                pending.PendingTime = TimeSpan.Zero;
                PublishRequestState(result.TargetPlayer, pending);
                if (pending.Controller is AIController ai)
                {
                    ai.ReceiveAuthorityResult(result);
                }
            }
            if (result.VisibleRecord != null && !IsOlderThanCurrent(result.TargetPlayer, result.VisibleRecord))
            {
                ApplyVisibleRecord(result.TargetPlayer, result.VisibleRecord);
            }

        }
        private void OnAuthorityState(AuthorityState state)
        {
            if (state == null || state.VisibleRecord == null)
            {
                return;
            }
            bool sameMatch = _visibleRecords.TryGetValue(state.TargetPlayer, out VisibleRecord current) &&
                current.MatchId == state.VisibleRecord.MatchId;
            if (sameMatch && _lastStateVersions.TryGetValue(state.TargetPlayer, out long version) && state.StateVersion <= version)
            {
                return;
            }
            _lastStateVersions[state.TargetPlayer] = state.StateVersion;
            ApplyVisibleRecord(state.TargetPlayer, state.VisibleRecord);
        }

        private void ApplyVisibleRecord(string playerIdentity, VisibleRecord newRecord)
        {
            _visibleRecords[playerIdentity] = newRecord;
            if (_controllersByPlayer.TryGetValue(playerIdentity, out IAuctionController controller))
            {
                if (controller is AIController ai)
                {
                    ai.ReceiveVisibleRecord(newRecord);
                }
            }

            if (_controllersByPlayer.TryGetValue(playerIdentity, out IAuctionController uiController) && uiController is HumanController)
            {
                VisibleRecordChanged?.Invoke(newRecord);
            }
        }

        private void PublishRequestState(string playerIdentity, PendingRequest pending)
        {
            if (!_controllersByPlayer.TryGetValue(playerIdentity, out IAuctionController controller) || !(controller is HumanController))
            {
                return;
            }


            RequestStateChanged?.Invoke(new RequestStateChange(
                playerIdentity,
                pending.RequestId,
                pending.State,
                pending.Reason));
        }
    }
}
