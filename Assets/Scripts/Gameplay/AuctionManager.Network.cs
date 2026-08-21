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

        private string _credentials;
        private NetworkRunner _fusionRunner;
        private bool _startingNetwork;
        private bool _intentionalDisconnect;
        private int _remainingReconnectAttempts;
        private TimeSpan _reconnectDelay;
        private bool _reconnectScheduled;

        public void StartOnlineClient(string credentials)
        {
            if (Mode != AuctionManagerMode.None)
            {
                throw new InvalidOperationException("AuctionManager 已经启动。");
            }
            _credentials = credentials ?? string.Empty;
            _remainingReconnectAttempts = GlobalSettings.ReconnectAttempts;
            Mode = AuctionManagerMode.OnlineClient;
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
            _reconnectScheduled = false;
            ConnectNetwork();
        }

        private async void ConnectNetwork()
        {
            if (_startingNetwork || _fusionRunner != null || Mode != AuctionManagerMode.OnlineClient)
            {
                return;
            }
            _startingNetwork = true;
            _intentionalDisconnect = false;
            GameObject runnerObject = new GameObject("Auction Client Runner");
            runnerObject.transform.SetParent(transform, false);
            NetworkRunner runner = runnerObject.AddComponent<NetworkRunner>();
            runner.AddCallbacks(this);
            try
            {
                StartGameResult result = await runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Client,
                    SessionName = SessionName
                });
                if (result.Ok)
                {
                    _fusionRunner = runner;
                }
                else
                {
                    HandleConnectionFailure(result.ErrorMessage ?? result.ShutdownReason.ToString());
                    Destroy(runnerObject);
                }
            }
            catch (Exception exception)
            {
                HandleConnectionFailure(exception.Message);
                Destroy(runnerObject);
            }
            finally
            {
                _startingNetwork = false;
            }
        }

        private void DisconnectRunner(bool intentional)
        {
            _intentionalDisconnect = intentional;
            _reconnectScheduled = false;
            NetworkRunner runner = _fusionRunner;
            _fusionRunner = null;
            if (runner != null)
            {
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
                AuctionFusionProtocol.MessageKey,
                AuctionFusionProtocol.Encode(type, payload));
        }

        private void OnConnectionSucceeded(string playerIdentity)
        {
            ConnectedPlayerIdentity = playerIdentity;
            _acceptingRequests = true;
            _remainingReconnectAttempts = GlobalSettings.ReconnectAttempts;
            _reconnectScheduled = false;
            ConnectionChanged?.Invoke("Connected");
            SendToServer(AuctionFusionProtocol.QueryState, null);

        }

        private void HandleConnectionFailure(string reason)
        {
            _acceptingRequests = false;
            ConnectionChanged?.Invoke($"Failed:{reason}");
            ScheduleReconnect();
        }

        private void OnConnectionLost()
        {
            _acceptingRequests = false;
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
            if (_reconnectScheduled)
            {
                return;
            }
            if (Mode != AuctionManagerMode.OnlineClient || _remainingReconnectAttempts <= 0)
            {
                ConnectionChanged?.Invoke("ReconnectExhausted");
                return;
            }
            _remainingReconnectAttempts--;
            _reconnectDelay = GlobalSettings.ReconnectInterval;
            _reconnectScheduled = true;
        }

        private void UpdateReconnect(TimeSpan deltaTime)
        {
            if (!_reconnectScheduled)
            {
                return;
            }
            _reconnectDelay -= deltaTime;
            if (_reconnectDelay > TimeSpan.Zero)
            {
                return;
            }
            _reconnectScheduled = false;
            ConnectionChanged?.Invoke("Reconnecting");
            ConnectNetwork();
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
            if (!key.Equals(AuctionFusionProtocol.MessageKey))
            {
                return;
            }
            AuctionFusionProtocol.FusionEnvelope envelope = AuctionFusionProtocol.Decode(data);
            switch (envelope.Type)
            {
                case AuctionFusionProtocol.Authenticated:
                    OnConnectionSucceeded(AuctionFusionProtocol.Payload<string>(envelope));
                    break;
                case AuctionFusionProtocol.Result:
                    OnAuthorityResult(AuctionFusionProtocol.Payload<AuthorityResult>(envelope));
                    break;
                case AuctionFusionProtocol.State:
                    OnAuthorityState(AuctionFusionProtocol.Payload<AuthorityState>(envelope));
                    break;
            }
        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
            runner.SendReliableDataToServer(
                AuctionFusionProtocol.MessageKey,
                AuctionFusionProtocol.Encode(AuctionFusionProtocol.Authenticate, _credentials));
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            if (ReferenceEquals(runner, _fusionRunner))
            {
                _fusionRunner = null;
            }
            runner.Shutdown();
            if (!_intentionalDisconnect)
            {
                OnConnectionLost();
            }
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            HandleConnectionFailure(reason.ToString());
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (ReferenceEquals(runner, _fusionRunner))
            {
                _fusionRunner = null;
            }
            if (runner != null)
            {
                Destroy(runner.gameObject);
            }
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
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
