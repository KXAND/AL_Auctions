using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace AuctionGame.Fusion
{
    public sealed class AuctionFusionSession : MonoBehaviour, INetworkRunnerCallbacks
    {
        private static readonly ReliableKey ActionKey = ReliableKey.FromInts(0x41554354, 1);
        private static readonly ReliableKey ViewKey = ReliableKey.FromInts(0x41554354, 2);

        [SerializeField] private string sessionName = "auction-local";
        [SerializeField] private int initialAssets = 100;
        [SerializeField] private int playerCount = 4;

        private readonly Dictionary<PlayerRef, int> _seatByPlayer = new Dictionary<PlayerRef, int>();
        private NetworkRunner _runner;
        private AuctionMatch _match;
        private AuctionPhase _lastBroadcastPhase = AuctionPhase.WaitingForPlayers;
        private bool _starting;

        public AuctionWireView CurrentView { get; private set; }
        public string Status { get; private set; } = "尚未连接";

        private void Start()
        {
            if (Application.isBatchMode || HasCommandLineArgument("-auctionServer"))
            {
                StartDedicatedServer();
            }
        }

        private void Update()
        {
            if (_runner == null || !_runner.IsServer || _match == null)
            {
                return;
            }

            _match.AdvanceTime(TimeSpan.FromSeconds(Time.deltaTime));
            var view = _seatByPlayer.Count == 0 ? null : _match.GetSeatView(_seatByPlayer.Values.First());
            if (view != null && view.Phase != _lastBroadcastPhase)
            {
                BroadcastViews();
            }
        }

        public void StartDedicatedServer()
        {
            StartRunner(GameMode.Server);
        }

        public void StartClient()
        {
            StartRunner(GameMode.Client);
        }

        public void SelectPrivateClue(string clueId)
        {
            SendAction(new AuctionActionMessage { Action = "select-clue", ClueId = clueId });
        }

        public void SubmitBid(int amount)
        {
            SendAction(new AuctionActionMessage { Action = "submit-bid", Amount = amount });
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer || _match != null)
            {
                return;
            }

            var rules = AuctionRules.CreateDemo(playerCount, initialAssets);
            _match = AuctionMatch.CreateDemo(rules);
            _seatByPlayer[player] = _match.ConnectHuman(player.ToString());
            _match.Start();
            BroadcastViews();
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
            if (key.Equals(ViewKey))
            {
                CurrentView = JsonUtility.FromJson<AuctionWireView>(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count));
                return;
            }

            if (!runner.IsServer || !key.Equals(ActionKey) || _match == null || !_seatByPlayer.TryGetValue(player, out var seatIndex))
            {
                return;
            }

            try
            {
                var action = JsonUtility.FromJson<AuctionActionMessage>(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count));
                if (action.Action == "select-clue")
                {
                    _match.SelectPrivateClue(seatIndex, action.ClueId);
                }
                else if (action.Action == "submit-bid")
                {
                    _match.SubmitBid(seatIndex, action.Amount);
                }

                BroadcastViews();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"竞拍动作被服务端拒绝：{exception.Message}");
            }
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
            Status = "已连接到专用服务端";
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Status = $"连接已断开：{reason}";
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            Status = $"连接失败：{reason}";
        }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

        private async void StartRunner(GameMode gameMode)
        {
            if (_starting || _runner != null)
            {
                return;
            }

            _starting = true;
            Status = gameMode == GameMode.Server ? "正在启动专用服务端" : "正在连接专用服务端";
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(this);

            try
            {
                await _runner.StartGame(new StartGameArgs
                {
                    GameMode = gameMode,
                    SessionName = sessionName,
                    PlayerCount = playerCount,
                    IsVisible = false,
                    IsOpen = true
                });
                Status = gameMode == GameMode.Server ? "专用服务端已启动" : Status;
            }
            catch (Exception exception)
            {
                Status = $"启动失败：{exception.Message}";
                Debug.LogException(exception);
            }
            finally
            {
                _starting = false;
            }
        }

        private void SendAction(AuctionActionMessage action)
        {
            if (_runner == null || !_runner.IsClient)
            {
                return;
            }

            _runner.SendReliableDataToServer(ActionKey, Encoding.UTF8.GetBytes(JsonUtility.ToJson(action)));
        }

        private void BroadcastViews()
        {
            if (_match == null || _runner == null)
            {
                return;
            }

            foreach (var entry in _seatByPlayer)
            {
                var view = _match.GetSeatView(entry.Value);
                _runner.SendReliableDataToPlayer(
                    entry.Key,
                    ViewKey,
                    Encoding.UTF8.GetBytes(JsonUtility.ToJson(AuctionWireView.From(entry.Value, view))));
                _lastBroadcastPhase = view.Phase;
            }
        }

        private static bool HasCommandLineArgument(string value)
        {
            return Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, value, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Serializable]
    public sealed class AuctionActionMessage
    {
        public string Action;
        public string ClueId;
        public int Amount;
    }

    [Serializable]
    public sealed class AuctionWireView
    {
        public int SeatIndex;
        public string Phase;
        public int AvailableAssets;
        public int GridWidth;
        public int GridHeight;
        public string PublicClue;
        public AuctionWireClueChoice[] PrivateClueChoices;
        public string PrivateClueResult;
        public AuctionWireKnowledge[] Knowledge;
        public int WinnerSlot;
        public int WinningBid;

        public static AuctionWireView From(int seatIndex, AuctionSeatView view)
        {
            return new AuctionWireView
            {
                SeatIndex = seatIndex,
                Phase = view.Phase.ToString(),
                AvailableAssets = view.AvailableAssets,
                GridWidth = view.Grid.Width,
                GridHeight = view.Grid.Height,
                PublicClue = view.PublicClue.Text,
                PrivateClueChoices = view.PrivateClueChoices
                    .Select(choice => new AuctionWireClueChoice { Id = choice.Id, Label = choice.Label })
                    .ToArray(),
                PrivateClueResult = view.PrivateClueResult == null ? null : view.PrivateClueResult.Text,
                Knowledge = view.Knowledge.Select(item => new AuctionWireKnowledge
                {
                    X = item.X,
                    Y = item.Y,
                    Width = item.Width,
                    Height = item.Height,
                    Rarity = item.Rarity.ToString(),
                    Name = item.Name,
                    Value = item.Value
                }).ToArray(),
                WinnerSlot = view.Settlement == null ? -1 : view.Settlement.WinnerSlot,
                WinningBid = view.Settlement == null ? 0 : view.Settlement.WinningBid
            };
        }
    }

    [Serializable]
    public sealed class AuctionWireClueChoice
    {
        public string Id;
        public string Label;
    }

    [Serializable]
    public sealed class AuctionWireKnowledge
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public string Rarity;
        public string Name;
        public int? Value;
    }
}
