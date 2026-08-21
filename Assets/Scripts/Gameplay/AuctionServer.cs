using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace AuctionGame
{
    public sealed class AuctionServer : SimulationBehaviour, INetworkRunnerCallbacks
    {
        private static readonly TimeSpan MatchmakingDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MatchmakingLogInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SettlementDisplayDuration = TimeSpan.FromSeconds(3);

        [SerializeField] private string sessionName = "auction-local";
        [SerializeField] private ClueCatalog clueCatalog;
        [SerializeField] private ItemCatalog itemCatalog;

        private readonly Dictionary<PlayerRef, string> _connections = new Dictionary<PlayerRef, string>();
        private readonly Dictionary<string, PlayerRef> _playerConnections = new Dictionary<string, PlayerRef>();
        private readonly Dictionary<PlayerRef, string> _connectionCredentials = new Dictionary<PlayerRef, string>();
        private readonly HashSet<string> _activeCredentials = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _waitingPlayers = new List<string>();
        private readonly Dictionary<string, string> _playerMatches = new Dictionary<string, string>();
        private readonly Dictionary<string, MatchContext> _matches = new Dictionary<string, MatchContext>();
        private readonly Dictionary<string, int> _runtimeAssets = new Dictionary<string, int>();
        private readonly Dictionary<string, SettlementRecord> _appliedSettlements = new Dictionary<string, SettlementRecord>();

        private ItemCatalog _itemCatalog;
        private NetworkRunner _runner;
        private bool _startingServer;
        private bool _serverRestartScheduled;
        private bool _stoppingServer;
        private DateTime _serverRestartDeadlineUtc;
        private TimeSpan _matchmakingElapsed;
        private TimeSpan _matchmakingLogElapsed;
        private long _identitySequence;
        private int _reliableMessageSequence;

        private void OnEnable()
        {
            Debug.Log($"[{DateTime.UtcNow:O}] AuctionServer enabled. Scene={gameObject.scene.name}, " +
                $"ActiveInHierarchy={gameObject.activeInHierarchy}, BatchMode={Application.isBatchMode}, " +
                $"TimeScale={Time.timeScale}.");
        }

        private void Start()
        {
            bool dedicatedLaunch = Application.isBatchMode ||
                System.Environment.GetCommandLineArgs().Any(argument =>
                    string.Equals(argument, "-auctionServer", System.StringComparison.OrdinalIgnoreCase));
            if (!dedicatedLaunch)
            {
                enabled = false;
                return;
            }

            if (!ValidateConfiguration())
            {
                enabled = false;
                return;
            }

            _itemCatalog = itemCatalog;
            StartServer();
        }

        private async void StartServer()
        {
            if (_startingServer || _runner != null || _stoppingServer)
            {
                return;
            }

            _startingServer = true;
            _serverRestartScheduled = false;
            GameObject runnerObject = new GameObject("Auction Server Runner");
            DontDestroyOnLoad(runnerObject);
            NetworkRunner runner = runnerObject.AddComponent<NetworkRunner>();
            runner.AddCallbacks(this);
            _runner = runner;
            try
            {
                StartGameResult result = await runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Server,
                    SessionName = sessionName,
                    PlayerCount = Math.Max(GlobalSettings.PlayerCount, 64),
                    IsVisible = false,
                    IsOpen = true
                });
                if (!ReferenceEquals(_runner, runner))
                {
                    return;
                }
                if (result.Ok)
                {
                    runner.AddGlobal(this);
                    Debug.Log($"[{DateTime.UtcNow:O}] AuctionServer 已启动。");
                }
                else
                {
                    HandleServerFailure(
                        runner,
                        result.ErrorMessage ?? result.ShutdownReason.ToString());
                }
            }
            catch (Exception exception)
            {
                HandleServerFailure(runner, exception.Message);
            }
            finally
            {
                _startingServer = false;
            }
        }

        private void Update()
        {
            if (!_serverRestartScheduled || _stoppingServer || _startingServer || _runner != null ||
                DateTime.UtcNow < _serverRestartDeadlineUtc)
            {
                return;
            }

            _serverRestartScheduled = false;
            StartServer();
        }

        public override void FixedUpdateNetwork()
        {
            if (_runner == null || !_runner.IsServer)
            {
                return;
            }

            TimeSpan delta = TimeSpan.FromSeconds(_runner.DeltaTime);
            MatchContext[] activeMatches = _matches.Values.ToArray();
            UpdateMatchmakingDiagnostics(delta);
            UpdateMatchmaking(delta);

            foreach (MatchContext context in activeMatches)
            {
                if (!context.Completed)
                {
                    context.AiManager.AdvanceServerTime(delta);
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

        private void UpdateMatchmakingDiagnostics(TimeSpan delta)
        {
            if (_waitingPlayers.Count == 0)
            {
                _matchmakingLogElapsed = TimeSpan.Zero;
                return;
            }

            _matchmakingLogElapsed += delta;
            if (_matchmakingLogElapsed < MatchmakingLogInterval)
            {
                return;
            }

            _matchmakingLogElapsed = TimeSpan.Zero;
            Debug.Log($"[{DateTime.UtcNow:O}] AuctionServer matchmaking tick. " +
                $"FusionTick={_runner.Tick}, WaitingPlayers={_waitingPlayers.Count}, " +
                $"Elapsed={_matchmakingElapsed.TotalSeconds:0.###}s, " +
                $"FusionDelta={delta.TotalSeconds:0.######}s, " +
                $"TimeScale={Time.timeScale:0.###}, Enabled={enabled}, " +
                $"ActiveInHierarchy={gameObject.activeInHierarchy}.");
        }

        private void UpdateMatchmaking(TimeSpan delta)
        {
            if (_waitingPlayers.Count == 0)
            {
                _matchmakingElapsed = TimeSpan.Zero;
                return;
            }

            _matchmakingElapsed += delta;
            bool createdFullMatch = false;
            while (_waitingPlayers.Count >= GlobalSettings.PlayerCount)
            {
                if (!TryCreateNextMatch(GlobalSettings.PlayerCount))
                {
                    return;
                }
                createdFullMatch = true;
            }

            if (createdFullMatch)
            {
                _matchmakingElapsed = TimeSpan.Zero;
            }

            if (_waitingPlayers.Count == 0 || _matchmakingElapsed < MatchmakingDelay)
            {
                return;
            }

            int humanCount = Math.Min(GlobalSettings.PlayerCount, _waitingPlayers.Count);
            Debug.Log($"[{DateTime.UtcNow:O}] AuctionServer matchmaking delay reached. " +
                $"FusionTick={_runner.Tick}, WaitingPlayers={_waitingPlayers.Count}, " +
                $"Elapsed={_matchmakingElapsed.TotalSeconds:0.###}s, HumanPlayers={humanCount}.");
            if (TryCreateNextMatch(humanCount))
            {
                _matchmakingElapsed = TimeSpan.Zero;
            }
        }

        private bool ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(sessionName))
            {
                Debug.LogError("AuctionServer 配置无效：SessionName 不能为空。");
                return false;
            }
            if (itemCatalog == null)
            {
                Debug.LogError("AuctionServer 配置无效：ItemCatalog 未赋值。");
                return false;
            }
            if (clueCatalog == null)
            {
                Debug.LogError("AuctionServer 配置无效：ClueCatalog 未赋值。");
                return false;
            }
            return true;
        }

        private void HandleServerFailure(NetworkRunner runner, string reason)
        {
            if (!ReferenceEquals(_runner, runner))
            {
                return;
            }

            _runner = null;
            ResetConnectionsForServerRestart();
            DestroyRunner(runner);
            if (_stoppingServer)
            {
                return;
            }

            Debug.LogError($"[{DateTime.UtcNow:O}] AuctionServer 运行失败：{reason}");
            _serverRestartDeadlineUtc = DateTime.UtcNow + GlobalSettings.ReconnectInterval;
            _serverRestartScheduled = true;
        }

        private void ResetConnectionsForServerRestart()
        {
            foreach (KeyValuePair<PlayerRef, string> connection in _connections.ToArray())
            {
                string identity = connection.Value;
                if (_playerMatches.TryGetValue(identity, out string matchId) &&
                    _matches.ContainsKey(matchId))
                {
                    try
                    {
                        CreateTakeoverController(matchId, identity);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }

            _connections.Clear();
            _playerConnections.Clear();
            _connectionCredentials.Clear();
            _activeCredentials.Clear();
            _waitingPlayers.Clear();
            _matchmakingElapsed = TimeSpan.Zero;
        }

        private static void DestroyRunner(NetworkRunner runner)
        {
            if (runner == null || runner.gameObject == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(runner.gameObject);
            }
            else
            {
                DestroyImmediate(runner.gameObject);
            }
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
            if (!ReferenceEquals(runner, _runner) || !runner.IsServer ||
                !AuctionFusionProtocol.IsMessageKey(key) || data.Count > 262144)
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
                Authenticate(runner, player, AuctionFusionProtocol.Payload<string>(envelope));
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
                Debug.LogWarning($"AuctionServer Fusion player left before business authentication. Player={player}.");
                return;
            }
            Debug.Log($"AuctionServer player disconnected. Player={player}, PlayerIdentity={identity}.");
            _connections.Remove(player);
            _playerConnections.Remove(identity);
            _waitingPlayers.Remove(identity);
            ReleaseCredential(player);
            if (_playerMatches.TryGetValue(identity, out string matchId) && _matches.TryGetValue(matchId, out MatchContext context))
            {
                CreateTakeoverController(matchId, identity);
            }
        }

        private void Authenticate(NetworkRunner runner, PlayerRef player, string credentials)
        {
            if (_connections.TryGetValue(player, out string existingIdentity))
            {
                Debug.Log($"AuctionServer repeated authentication accepted. Player={player}, " +
                    $"PlayerIdentity={existingIdentity}.");
                Send(runner, player, AuctionFusionProtocol.Authenticated, existingIdentity);
                SendCurrentState(existingIdentity);
                return;
            }
            if (!TryAcceptCredential(player, credentials, out string rejectionReason))
            {
                Debug.LogWarning($"AuctionServer authentication rejected. Player={player}, " +
                    $"Reason={rejectionReason}.");
                Send(runner, player, AuctionFusionProtocol.AuthenticationRejected, rejectionReason);
                return;
            }

            string identity = $"runtime-{++_identitySequence}-{Guid.NewGuid():N}";
            _connections[player] = identity;
            _playerConnections[identity] = player;
            _runtimeAssets[identity] = GlobalSettings.InitialAssets;
            _waitingPlayers.Add(identity);
            Debug.Log($"[{DateTime.UtcNow:O}] AuctionServer authentication accepted. " +
                $"FusionTick={runner.Tick}, Player={player}, PlayerIdentity={identity}, " +
                $"WaitingPlayers={_waitingPlayers.Count}.");
            Send(runner, player, AuctionFusionProtocol.Authenticated, identity);
            Send(runner, player, AuctionFusionProtocol.State,
                new AuthorityState(identity, 1, VisibleRecord.Waiting(null, _runtimeAssets[identity])));
        }

        private bool TryAcceptCredential(PlayerRef player, string credentials, out string rejectionReason)
        {
            if (!Guid.TryParseExact(credentials, "N", out Guid credentialId))
            {
                rejectionReason = "凭证格式无效。";
                return false;
            }

            string normalizedCredential = credentialId.ToString("N");
            if (_activeCredentials.Contains(normalizedCredential))
            {
                rejectionReason = "凭证已被其他连接使用。";
                return false;
            }

            _connectionCredentials[player] = normalizedCredential;
            _activeCredentials.Add(normalizedCredential);
            rejectionReason = null;
            return true;
        }

        private void ReleaseCredential(PlayerRef player)
        {
            if (!_connectionCredentials.TryGetValue(player, out string credential))
            {
                return;
            }

            _connectionCredentials.Remove(player);
            _activeCredentials.Remove(credential);
        }

        private bool TryCreateNextMatch(int humanCount)
        {
            Debug.Log($"AuctionServer creating next match. RequestedHumanPlayers={humanCount}, " +
                $"WaitingPlayers={_waitingPlayers.Count}.");
            string[] humans = _waitingPlayers.Take(humanCount).ToArray();
            foreach (string human in humans)
            {
                _waitingPlayers.Remove(human);
            }
            try
            {
                CreateMatch(humans);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                for (int index = humans.Length - 1; index >= 0; index--)
                {
                    string human = humans[index];
                    if (_playerConnections.ContainsKey(human) && !_waitingPlayers.Contains(human))
                    {
                        _waitingPlayers.Insert(0, human);
                    }
                }
                return false;
            }
        }

        private void CreateMatch(IReadOnlyList<string> humanPlayers)
        {
            string matchId = Guid.NewGuid().ToString("N");
            Debug.Log($"AuctionServer CreateMatch entered. MatchId={matchId}, " +
                $"HumanPlayers={humanPlayers.Count}.");
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
            Debug.Log($"AuctionServer Authority created. MatchId={matchId}.");
            GameObject aiManagerObject = new GameObject($"Server AI AuctionManager {matchId}");
            aiManagerObject.transform.SetParent(transform, false);
            AuctionManager aiManager = aiManagerObject.AddComponent<AuctionManager>();
            aiManager.StartServerAI(authority);
            Debug.Log($"AuctionServer AI AuctionManager started. MatchId={matchId}.");
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
                Debug.Log($"AuctionServer preparing Authority match. MatchId={matchId}.");
                authority.PrepareMatch(matchId, participants);
                Debug.Log($"AuctionServer Authority match prepared. MatchId={matchId}.");
                authority.StartMatch();
                Debug.Log($"[{DateTime.UtcNow:O}] AuctionServer match started. " +
                    $"FusionTick={_runner.Tick}, MatchId={matchId}, " +
                    $"HumanPlayers={humanPlayers.Count}, AIPlayers={participants.Count - humanPlayers.Count}.");
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
            // TODO: AI 接管后同步 Authority 中的控制者类型，不在本次修改范围。
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
            Send(_runner, player, type, payload);
        }

        private void Send(NetworkRunner runner, PlayerRef player, string type, object payload)
        {
            if (runner == null || !runner.IsRunning)
            {
                return;
            }
            runner.SendReliableDataToPlayer(
                player,
                NextReliableMessageKey(),
                AuctionFusionProtocol.Encode(type, payload));
        }

        private ReliableKey NextReliableMessageKey()
        {
            _reliableMessageSequence = unchecked(_reliableMessageSequence + 1);
            return AuctionFusionProtocol.CreateMessageKey(_reliableMessageSequence);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[{DateTime.UtcNow:O}] AuctionServer Fusion player joined. " +
                $"FusionTick={runner.Tick}, Player={player}.");
        }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
        }
        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
        }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            HandleServerFailure(runner, reason.ToString());
        }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
        {
        }
        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            HandleServerFailure(runner, reason.ToString());
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
            HandleServerFailure(runner, shutdownReason.ToString());
        }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        private void OnApplicationQuit()
        {
            _stoppingServer = true;
            _serverRestartScheduled = false;
        }

        private void OnDisable()
        {
            Debug.Log($"AuctionServer disabled. Scene={gameObject.scene.name}, " +
                $"ActiveInHierarchy={gameObject.activeInHierarchy}, TimeScale={Time.timeScale}.");
        }

        private void OnDestroy()
        {
            Debug.Log("AuctionServer destroyed.");
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
