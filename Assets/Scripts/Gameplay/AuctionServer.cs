using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace AuctionGame
{
    public sealed class AuctionServer : MonoBehaviour, INetworkRunnerCallbacks
    {
        private static readonly TimeSpan MatchmakingDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan SettlementDisplayDuration = TimeSpan.FromSeconds(3);

        [SerializeField] private string sessionName = "auction-local";
        [SerializeField] private ClueCatalog clueCatalog;
        [SerializeField] private ItemCatalog itemCatalog;

        private readonly Dictionary<PlayerRef, string> _connections = new Dictionary<PlayerRef, string>();
        private readonly Dictionary<string, PlayerRef> _playerConnections = new Dictionary<string, PlayerRef>();
        private readonly List<string> _waitingPlayers = new List<string>();
        private readonly Dictionary<string, string> _playerMatches = new Dictionary<string, string>();
        private readonly Dictionary<string, MatchContext> _matches = new Dictionary<string, MatchContext>();
        private readonly Dictionary<string, int> _runtimeAssets = new Dictionary<string, int>();
        private readonly Dictionary<string, SettlementRecord> _appliedSettlements = new Dictionary<string, SettlementRecord>();

        private ItemCatalog _itemCatalog;
        private NetworkRunner _runner;
        private TimeSpan _matchmakingElapsed;
        private long _identitySequence;

        private async void Start()
        {
            bool dedicatedLaunch = Application.isBatchMode ||
                System.Environment.GetCommandLineArgs().Any(argument =>
                    string.Equals(argument, "-auctionServer", System.StringComparison.OrdinalIgnoreCase));
            if (!dedicatedLaunch)
            {
                enabled = false;
                return;
            }

            _itemCatalog = itemCatalog;
            GameObject runnerObject = new GameObject("Auction Server Runner");
            DontDestroyOnLoad(runnerObject);
            NetworkRunner runner = runnerObject.AddComponent<NetworkRunner>();
            runner.AddCallbacks(this);
            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Server,
                SessionName = sessionName,
                PlayerCount = Math.Max(GlobalSettings.PlayerCount, 64),
                IsVisible = false,
                IsOpen = true
            });
            if (result.Ok)
            {
                _runner = runner;
                Debug.Log("AuctionServer 已启动。");
            }
            else
            {
                Debug.LogError($"AuctionServer 启动失败：{result.ErrorMessage ?? result.ShutdownReason.ToString()}");
                Destroy(runnerObject);
            }
        }

        private void Update()
        {
            if (_itemCatalog == null)
            {
                return;
            }
            TimeSpan delta = TimeSpan.FromSeconds(Time.deltaTime);
            _matchmakingElapsed += delta;
            if (_waitingPlayers.Count >= GlobalSettings.PlayerCount ||
                _waitingPlayers.Count > 0 && _matchmakingElapsed >= MatchmakingDelay)
            {
                TryCreateMatches();
                _matchmakingElapsed = TimeSpan.Zero;
            }

            foreach (MatchContext context in _matches.Values.ToArray())
            {
                if (!context.Completed)
                {
                    context.Authority.AdvanceTime(delta);
                    continue;
                }

                context.CompletionElapsed += delta;
                if (context.CompletionElapsed >= SettlementDisplayDuration)
                {
                    DestroyCompletedMatch(context.MatchId);
                }
            }
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
            if (!runner.IsServer || !key.Equals(AuctionFusionProtocol.MessageKey) || data.Count > 262144)
            {
                return;
            }
            AuctionFusionProtocol.FusionEnvelope envelope;
            try
            {
                envelope = AuctionFusionProtocol.Decode(data);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"忽略格式无效的竞拍消息：{exception.Message}");
                return;
            }

            if (envelope.Type == AuctionFusionProtocol.Authenticate)
            {
                Authenticate(player, AuctionFusionProtocol.Payload<string>(envelope));
                return;
            }
            if (!_connections.TryGetValue(player, out string identity))
            {
                return;
            }

            if (envelope.Type == AuctionFusionProtocol.Action)
            {
                RouteAction(identity, AuctionFusionProtocol.Payload<ActionRequest>(envelope));
            }
            else if (envelope.Type == AuctionFusionProtocol.QueryState)
            {
                SendCurrentState(identity);
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!_connections.TryGetValue(player, out string identity))
            {
                return;
            }
            _connections.Remove(player);
            _playerConnections.Remove(identity);
            _waitingPlayers.Remove(identity);
            if (_playerMatches.TryGetValue(identity, out string matchId) && _matches.TryGetValue(matchId, out MatchContext context))
            {
                CreateTakeoverController(matchId, identity);
            }
        }

        private void Authenticate(PlayerRef player, string credentials)
        {
            if (string.IsNullOrWhiteSpace(credentials) || _connections.ContainsKey(player))
            {
                return;
            }
            string identity = $"runtime-{++_identitySequence}-{Guid.NewGuid():N}";
            _connections[player] = identity;
            _playerConnections[identity] = player;
            _runtimeAssets[identity] = GlobalSettings.InitialAssets;
            _waitingPlayers.Add(identity);
            Send(player, AuctionFusionProtocol.Authenticated, identity);
            Send(player, AuctionFusionProtocol.State,
                new AuthorityState(identity, 1, VisibleRecord.Waiting(null, _runtimeAssets[identity])));
        }

        private void TryCreateMatches()
        {
            while (_waitingPlayers.Count > 0)
            {
                string[] humans = _waitingPlayers.Take(GlobalSettings.PlayerCount).ToArray();
                foreach (string human in humans)
                {
                    _waitingPlayers.Remove(human);
                }
                try
                {
                    CreateMatch(humans);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    foreach (string human in humans.Where(_playerConnections.ContainsKey))
                    {
                        if (!_waitingPlayers.Contains(human))
                        {
                            _waitingPlayers.Add(human);
                        }
                    }
                    break;
                }
            }
        }

        private void CreateMatch(IReadOnlyList<string> humanPlayers)
        {
            string matchId = Guid.NewGuid().ToString("N");
            List<MatchParticipant> participants = new List<MatchParticipant>();
            for (int index = 0; index < humanPlayers.Count; index++)
            {
                string identity = humanPlayers[index];
                participants.Add(new MatchParticipant(
                    identity,
                    _runtimeAssets[identity],
                    false,
                    identity));
            }
            while (participants.Count < GlobalSettings.PlayerCount)
            {
                int participantIndex = participants.Count;
                string identity = $"ai-{matchId}-{participantIndex}";
                participants.Add(new MatchParticipant(
                    identity,
                    GlobalSettings.InitialAssets,
                    true,
                    identity));
            }

            Authority authority = new Authority(
                _itemCatalog,
                clueCatalog,
                new System.Random());
            GameObject aiManagerObject = new GameObject($"Server AI AuctionManager {matchId}");
            aiManagerObject.transform.SetParent(transform, false);
            AuctionManager aiManager = aiManagerObject.AddComponent<AuctionManager>();
            aiManager.StartServerAI(authority);
            MatchContext context = new MatchContext(matchId, authority, aiManager, humanPlayers.ToArray());
            _matches[matchId] = context;

            authority.ResultCreated += result => RouteResult(matchId, result);
            authority.StateCreated += state => RouteState(matchId, state);
            authority.SettlementCreated += settlement => ApplySettlement(matchId, settlement);
            authority.MatchCompleted += completedMatchId => MarkMatchCompleted(completedMatchId);

            foreach (MatchParticipant participant in participants.Where(item => item.IsAI))
            {
                AIController ai = new AIController(aiManager, new System.Random());
                context.AiControllers.Add(ai);
                aiManager.RegisterController(ai, participant.PlayerIdentity);
            }
            foreach (string human in humanPlayers)
            {
                _playerMatches[human] = matchId;
            }

            try
            {
                authority.PrepareMatch(matchId, participants);
                authority.StartMatch();
            }
            catch
            {
                foreach (string human in humanPlayers)
                {
                    _playerMatches.Remove(human);
                }
                _matches.Remove(matchId);
                aiManager.Disconnect();
                DestroyAuctionManager(aiManager);
                throw;
            }
        }

        private void RouteAction(string identity, ActionRequest request)
        {
            if (request == null || !_playerMatches.TryGetValue(identity, out string matchId) ||
                !_matches.TryGetValue(matchId, out MatchContext context))
            {
                return;
            }
            if (request.MatchId != matchId)
            {
                SendResult(identity, new AuthorityResult(
                    identity,
                    request.RequestId,
                    false,
                    "请求伪造了对局 ID。",
                    context.Authority.GetVisibleRecord(identity)));
                return;
            }
            context.Authority.HandleRequest(identity, request);
        }

        private void SendCurrentState(string identity)
        {
            if (!_playerMatches.TryGetValue(identity, out string matchId) ||
                !_matches.TryGetValue(matchId, out MatchContext context))
            {
                if (_playerConnections.TryGetValue(identity, out PlayerRef waitingPlayer))
                {
                    Send(waitingPlayer, AuctionFusionProtocol.State,
                    new AuthorityState(identity, 1, VisibleRecord.Waiting(null, _runtimeAssets[identity])));
                }
                return;
            }
            if (context.LastStates.TryGetValue(identity, out AuthorityState state) &&
                _playerConnections.TryGetValue(identity, out PlayerRef player))
            {
                Send(player, AuctionFusionProtocol.State, state);
            }
        }

        private void RouteResult(string matchId, AuthorityResult result)
        {
            if (!_matches.ContainsKey(matchId))
            {
                return;
            }
            SendResult(result.TargetPlayer, result);
        }

        private void RouteState(string matchId, AuthorityState state)
        {
            if (!_matches.TryGetValue(matchId, out MatchContext context))
            {
                return;
            }
            context.LastStates[state.TargetPlayer] = state;
            if (_playerConnections.TryGetValue(state.TargetPlayer, out PlayerRef player))
            {
                Send(player, AuctionFusionProtocol.State, state);
            }
        }

        private void SendResult(string identity, AuthorityResult result)
        {
            if (_playerConnections.TryGetValue(identity, out PlayerRef player))
            {
                Send(player, AuctionFusionProtocol.Result, result);
            }
        }

        private void CreateTakeoverController(string matchId, string playerIdentity)
        {
            if (!_matches.TryGetValue(matchId, out MatchContext context))
            {
                return;
            }
            AIController ai = new AIController(context.AiManager, new System.Random());
            context.AiControllers.Add(ai);
            context.AiManager.RegisterController(ai, playerIdentity);
            ai.OnTakeOver();
        }

        private void ApplySettlement(string matchId, SettlementRecord settlement)
        {
            if (!_matches.ContainsKey(matchId) || _appliedSettlements.ContainsKey(settlement.SettlementId))
            {
                return;
            }
            foreach (KeyValuePair<string, int> finalAsset in settlement.FinalVisibleAssets)
            {
                if (_connections.Values.Contains(finalAsset.Key))
                {
                    _runtimeAssets[finalAsset.Key] = finalAsset.Value;
                }
            }
            _appliedSettlements[settlement.SettlementId] = settlement;
        }

        private void MarkMatchCompleted(string matchId)
        {
            if (_matches.TryGetValue(matchId, out MatchContext context))
            {
                context.Completed = true;
            }
        }

        private void DestroyCompletedMatch(string matchId)
        {
            if (!_matches.TryGetValue(matchId, out MatchContext context))
            {
                return;
            }
            context.AiManager.Disconnect();
            DestroyAuctionManager(context.AiManager);
            foreach (string human in context.HumanPlayers)
            {
                _playerMatches.Remove(human);
                if (_playerConnections.ContainsKey(human) && !_waitingPlayers.Contains(human))
                {
                    _waitingPlayers.Add(human);
                }
            }
            _matches.Remove(matchId);
        }

        private static void DestroyAuctionManager(AuctionManager manager)
        {
            if (manager == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(manager.gameObject);
            }
            else
            {
                DestroyImmediate(manager.gameObject);
            }
        }

        private void Send(PlayerRef player, string type, object payload)
        {
            if (_runner == null)
            {
                return;
            }
            _runner.SendReliableDataToPlayer(
                player,
                AuctionFusionProtocol.MessageKey,
                AuctionFusionProtocol.Encode(type, payload));
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
        }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
        }
        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
        }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
        }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
        {
        }
        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
        }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
        }
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
        }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
        {
        }
        public void OnSceneLoadDone(NetworkRunner runner)
        {
        }
        public void OnSceneLoadStart(NetworkRunner runner)
        {
        }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
        }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
        }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        private sealed class MatchContext
        {
            public MatchContext(
                string matchId,
                Authority authority,
                AuctionManager aiManager,
                IReadOnlyList<string> humanPlayers)
            {
                MatchId = matchId;
                Authority = authority;
                AiManager = aiManager;
                HumanPlayers = humanPlayers;
            }

            public string MatchId { get; }
            public Authority Authority { get; }
            public AuctionManager AiManager { get; }
            public IReadOnlyList<string> HumanPlayers { get; }
            public List<AIController> AiControllers { get; } = new List<AIController>();
            public Dictionary<string, AuthorityState> LastStates { get; } = new Dictionary<string, AuthorityState>();
            public bool Completed { get; set; }
            public TimeSpan CompletionElapsed { get; set; }
        }
    }
}
