using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace AuctionGame
{
    public sealed partial class AuctionManager : INetworkRunnerCallbacks
    {
        private const string SessionName = "auction-local";

        private enum ClientConnectionState
        {
            Stopped,
            Starting,
            AwaitingPlayer,
            Authenticating,
            Connected,
            ReconnectScheduled
        }

        private string _credentials;
        private NetworkRunner _fusionRunner;
        private ClientConnectionState _connectionState;
        private int _networkAttemptId;
        private int _failedAttemptId = -1;
        private int _remainingReconnectAttempts;
        private TimeSpan _reconnectDelay;
        private TimeSpan _authenticationElapsed;
        private TimeSpan _authenticationRetryDelay;
        private int _authenticationSendCount;
        private int _reliableMessageSequence;

        public void StartOnlineClient(string credentials)
        {
            if (Mode != AuctionManagerMode.None)
            {
                throw new InvalidOperationException("AuctionManager 已经启动。");
            }
            _credentials = credentials ?? string.Empty;
            _remainingReconnectAttempts = GlobalSettings.ReconnectAttempts;
            Mode = AuctionManagerMode.OnlineClient;
            _connectionState = ClientConnectionState.Stopped;
            ConnectNetwork();
        }

        public void Reconnect()
        {
            if (Mode != AuctionManagerMode.OnlineClient)
            {
                return;
            }
            DisconnectRunner(true);
            _remainingReconnectAttempts = GlobalSettings.ReconnectAttempts;
            ConnectNetwork();
        }

        private async void ConnectNetwork()
        {
            if (Mode != AuctionManagerMode.OnlineClient ||
                _fusionRunner != null ||
                _connectionState == ClientConnectionState.Starting ||
                _connectionState == ClientConnectionState.AwaitingPlayer ||
                _connectionState == ClientConnectionState.Authenticating ||
                _connectionState == ClientConnectionState.Connected)
            {
                return;
            }

            int attemptId = ++_networkAttemptId;
            _failedAttemptId = -1;
            _connectionState = ClientConnectionState.Starting;
            GameObject runnerObject = new GameObject("Auction Client Runner");
            NetworkRunner runner = runnerObject.AddComponent<NetworkRunner>();
            runner.AddCallbacks(this);
            _fusionRunner = runner;
            Debug.Log($"Auction client connection attempt {attemptId} starting Session '{SessionName}'.");
            try
            {
                StartGameResult result = await runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Client,
                    SessionName = SessionName
                });
                if (attemptId != _networkAttemptId || _failedAttemptId == attemptId)
                {
                    return;
                }
                if (result.Ok)
                {
                    if (_connectionState == ClientConnectionState.Starting)
                    {
                        _connectionState = ClientConnectionState.AwaitingPlayer;
                        _authenticationElapsed = TimeSpan.Zero;
                    }
                    Debug.Log($"Auction client connection attempt {attemptId} joined Session '{SessionName}'. " +
                        $"State={_connectionState}.");
                    return;
                }

                CompleteConnectionFailure(
                    attemptId,
                    runner,
                    "StartGame",
                    result.ErrorMessage ?? result.ShutdownReason.ToString());
                DestroyRunnerObject(runner);
            }
            catch (Exception exception)
            {
                if (attemptId != _networkAttemptId || _failedAttemptId == attemptId)
                {
                    return;
                }

                CompleteConnectionFailure(attemptId, runner, "StartGameException", exception.Message);
                DestroyRunnerObject(runner);
            }
        }

        private void DisconnectRunner(bool intentional)
        {
            int canceledAttemptId = _networkAttemptId;
            _networkAttemptId++;
            _connectionState = ClientConnectionState.Stopped;
            NetworkRunner runner = _fusionRunner;
            _fusionRunner = null;
            if (runner != null)
            {
                Debug.Log($"Auction client connection attempt {canceledAttemptId} stopping. " +
                    $"Intentional={intentional}.");
                runner.Shutdown();
            }
        }

        private void SendToServer(string type, object payload)
        {
            if (_fusionRunner == null || !_fusionRunner.IsClient)
            {
                return;
            }
            _fusionRunner.SendReliableDataToServer(
                NextReliableMessageKey(),
                AuctionFusionProtocol.Encode(type, payload));
        }

        private ReliableKey NextReliableMessageKey()
        {
            _reliableMessageSequence = unchecked(_reliableMessageSequence + 1);
            return AuctionFusionProtocol.CreateMessageKey(_reliableMessageSequence);
        }

        private void OnConnectionSucceeded(string playerIdentity)
        {
            if (_connectionState != ClientConnectionState.Authenticating)
            {
                Debug.LogWarning($"Auction client ignored authentication success in state {_connectionState}.");
                return;
            }

            ConnectedPlayerIdentity = playerIdentity;
            _acceptingRequests = true;
            _connectionState = ClientConnectionState.Connected;
            _remainingReconnectAttempts = GlobalSettings.ReconnectAttempts;
            Debug.Log($"Auction client connection attempt {_networkAttemptId} authenticated. " +
                $"PlayerIdentity={playerIdentity}.");
            ConnectionChanged?.Invoke("Connected");

        }

        private void HandleConnectionFailure(string reason)
        {
            _acceptingRequests = false;
            Debug.LogError($"Auction client connection failed: {reason}");
            ConnectionChanged?.Invoke($"Failed:{reason}");
            ScheduleReconnect();
        }

        private void OnConnectionLost(string reason)
        {
            _acceptingRequests = false;
            Debug.LogError($"Auction client connection lost: {reason}");
            foreach (PendingRequest pending in _pendingRequests.Values.Where(item => item.State == PendingRequestState.Pending))
            {
                pending.State = PendingRequestState.Unknown;
                PublishRequestState(_controllers[pending.Controller], pending);
            }
            ConnectionChanged?.Invoke("Disconnected");
            ScheduleReconnect();
        }

        private void ScheduleReconnect()
        {
            if (_connectionState == ClientConnectionState.ReconnectScheduled)
            {
                return;
            }
            if (Mode != AuctionManagerMode.OnlineClient || _remainingReconnectAttempts <= 0)
            {
                _connectionState = ClientConnectionState.Stopped;
                Debug.LogError("Auction client reconnect attempts exhausted.");
                ConnectionChanged?.Invoke("ReconnectExhausted");
                return;
            }
            _remainingReconnectAttempts--;
            _reconnectDelay = GlobalSettings.ReconnectInterval;
            _connectionState = ClientConnectionState.ReconnectScheduled;
            Debug.LogWarning($"Auction client reconnect scheduled after {_reconnectDelay.TotalSeconds:0.###}s. " +
                $"RemainingAttempts={_remainingReconnectAttempts}.");
        }

        private void UpdateReconnect(TimeSpan deltaTime)
        {
            if (_connectionState != ClientConnectionState.ReconnectScheduled)
            {
                return;
            }
            _reconnectDelay -= deltaTime;
            if (_reconnectDelay > TimeSpan.Zero)
            {
                return;
            }
            _connectionState = ClientConnectionState.Stopped;
            ConnectionChanged?.Invoke("Reconnecting");
            ConnectNetwork();
        }

        private void UpdateNetworkHandshake(TimeSpan deltaTime)
        {
            if (_connectionState != ClientConnectionState.AwaitingPlayer &&
                _connectionState != ClientConnectionState.Authenticating)
            {
                return;
            }

            _authenticationElapsed += deltaTime;
            if (_authenticationElapsed >= GlobalSettings.AuthenticationTimeout)
            {
                NetworkRunner timedOutRunner = _fusionRunner;
                string stage = _connectionState == ClientConnectionState.AwaitingPlayer
                    ? "PlayerJoinTimeout"
                    : "AuthenticationTimeout";
                CompleteConnectionFailure(
                    _networkAttemptId,
                    timedOutRunner,
                    stage,
                    $"No business handshake response within {GlobalSettings.AuthenticationTimeout.TotalSeconds:0.###}s.");
                if (timedOutRunner != null)
                {
                    timedOutRunner.Shutdown();
                }
                return;
            }

            if (_connectionState != ClientConnectionState.Authenticating ||
                _fusionRunner == null ||
                !_fusionRunner.IsRunning ||
                !_fusionRunner.IsClient ||
                !_fusionRunner.IsConnectedToServer)
            {
                return;
            }

            _authenticationRetryDelay -= deltaTime;
            if (_authenticationRetryDelay > TimeSpan.Zero)
            {
                return;
            }

            _fusionRunner.SendReliableDataToServer(
                NextReliableMessageKey(),
                AuctionFusionProtocol.Encode(AuctionFusionProtocol.Authenticate, _credentials));
            _authenticationSendCount++;
            Debug.Log($"Auction client connection attempt {_networkAttemptId} sent authentication " +
                $"request {_authenticationSendCount}.");
            _authenticationRetryDelay = GlobalSettings.ReconnectInterval;
        }

        private void CompleteConnectionFailure(
            int attemptId,
            NetworkRunner runner,
            string stage,
            string reason)
        {
            if (attemptId != _networkAttemptId)
            {
                Debug.Log($"Auction client ignored stale connection failure. Attempt={attemptId}, " +
                    $"CurrentAttempt={_networkAttemptId}, Stage={stage}, Reason={reason}.");
                return;
            }
            if (_failedAttemptId == attemptId)
            {
                return;
            }

            ClientConnectionState failedState = _connectionState;
            bool wasConnected = failedState == ClientConnectionState.Connected;
            _failedAttemptId = attemptId;
            if (ReferenceEquals(_fusionRunner, runner))
            {
                _fusionRunner = null;
            }
            _connectionState = ClientConnectionState.Stopped;

            string details = $"Attempt={attemptId}, Stage={stage}, State={failedState}, Reason={reason}";
            if (wasConnected)
            {
                OnConnectionLost(details);
            }
            else
            {
                HandleConnectionFailure(details);
            }
        }

        private static void DestroyRunnerObject(NetworkRunner runner)
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

        private void UpdateOnlineVisibleTimes(TimeSpan deltaTime)
        {
            foreach (KeyValuePair<string, VisibleRecord> entry in _visibleRecords.ToArray())
            {
                VisibleRecord record = entry.Value;
                if (record.RemainingTime <= TimeSpan.Zero ||
                    record.Phase == MatchPhase.Waiting ||
                    record.Phase == MatchPhase.Settlement)
                {
                    continue;
                }
                TimeSpan remaining = record.RemainingTime - deltaTime;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }
                ApplyVisibleRecord(entry.Key, new VisibleRecord(
                    record.MatchId,
                    record.OwnAssets,
                    record.Phase,
                    record.Round,
                    remaining,
                    record.CanRequestAction,
                    record.VisibleBoard,
                    record.ClueChoices,
                    record.BidHistory,
                    record.PublicClueHistory,
                    record.PrivateClueHistory,
                    record.Participants,
                    record.RoundReveal,
                    record.Settlement));
            }
        }

        public void OnReliableDataReceived(
            NetworkRunner runner,
            PlayerRef player,
            ReliableKey key,
            ArraySegment<byte> data)
        {
            if (!ReferenceEquals(runner, _fusionRunner) ||
                !AuctionFusionProtocol.IsMessageKey(key))
            {
                return;
            }
            AuctionFusionProtocol.FusionEnvelope envelope = AuctionFusionProtocol.Decode(data);
            switch (envelope.Type)
            {
                case AuctionFusionProtocol.Authenticated:
                    OnConnectionSucceeded(AuctionFusionProtocol.Payload<string>(envelope));
                    break;
                case AuctionFusionProtocol.AuthenticationRejected:
                    CompleteConnectionFailure(
                        _networkAttemptId,
                        runner,
                        "AuthenticationRejected",
                        AuctionFusionProtocol.Payload<string>(envelope));
                    runner.Shutdown();
                    break;
                case AuctionFusionProtocol.Result:
                    AuthorityResult result = AuctionFusionProtocol.Payload<AuthorityResult>(envelope);
                    Debug.Log($"Auction client received AuthorityResult. RequestId={result.RequestId}, " +
                        $"Accepted={result.Accepted}.");
                    OnAuthorityResult(result);
                    break;
                case AuctionFusionProtocol.State:
                    AuthorityState state = AuctionFusionProtocol.Payload<AuthorityState>(envelope);
                    Debug.Log($"Auction client received AuthorityState. PlayerIdentity={state.TargetPlayer}, " +
                        $"MatchId={state.VisibleRecord.MatchId}, Version={state.StateVersion}, " +
                        $"Phase={state.VisibleRecord.Phase}, Round={state.VisibleRecord.Round}.");
                    OnAuthorityState(state);
                    break;
            }
        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
            if (!ReferenceEquals(runner, _fusionRunner))
            {
                return;
            }

            Debug.Log($"Auction client connection attempt {_networkAttemptId} connected to Fusion server. " +
                $"State={_connectionState}.");
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            if (!ReferenceEquals(runner, _fusionRunner))
            {
                return;
            }

            int attemptId = _networkAttemptId;
            CompleteConnectionFailure(attemptId, runner, "DisconnectedFromServer", reason.ToString());
            runner.Shutdown();
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            if (!ReferenceEquals(runner, _fusionRunner))
            {
                return;
            }

            int attemptId = _networkAttemptId;
            CompleteConnectionFailure(attemptId, runner, "ConnectFailed", reason.ToString());
            runner.Shutdown();
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            bool currentRunner = ReferenceEquals(runner, _fusionRunner);
            ClientConnectionState stateAtShutdown = _connectionState;
            Debug.Log($"Auction client Runner shutdown. CurrentAttempt={_networkAttemptId}, " +
                $"CurrentRunner={currentRunner}, State={stateAtShutdown}, Reason={shutdownReason}.");

            if (ReferenceEquals(runner, _fusionRunner))
            {
                if (stateAtShutdown == ClientConnectionState.Authenticating ||
                    stateAtShutdown == ClientConnectionState.AwaitingPlayer ||
                    stateAtShutdown == ClientConnectionState.Connected)
                {
                    CompleteConnectionFailure(
                        _networkAttemptId,
                        runner,
                        "Shutdown",
                        shutdownReason.ToString());
                }
                else
                {
                    _fusionRunner = null;
                }
            }
            DestroyRunnerObject(runner);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!ReferenceEquals(runner, _fusionRunner) ||
                !runner.IsClient ||
                _connectionState == ClientConnectionState.Connected)
            {
                return;
            }

            if (_connectionState == ClientConnectionState.Authenticating)
            {
                Debug.Log($"Auction client connection attempt {_networkAttemptId} observed additional " +
                    $"PlayerJoined {player} while authenticating.");
                return;
            }
            if (_connectionState != ClientConnectionState.Starting &&
                _connectionState != ClientConnectionState.AwaitingPlayer)
            {
                Debug.LogWarning($"Auction client ignored PlayerJoined {player} in state {_connectionState}.");
                return;
            }

            _connectionState = ClientConnectionState.Authenticating;
            _authenticationElapsed = TimeSpan.Zero;
            _authenticationRetryDelay = TimeSpan.Zero;
            _authenticationSendCount = 0;
            Debug.Log($"Auction client connection attempt {_networkAttemptId} observed PlayerJoined {player}; " +
                "business authentication started.");
        }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
        }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
        }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
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
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }
    }
}
