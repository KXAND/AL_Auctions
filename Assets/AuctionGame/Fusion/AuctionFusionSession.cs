using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        [SerializeField, Min(1)] private int dedicatedServerStartAttempts = 3;
        [SerializeField, Min(0.1f)] private float dedicatedServerRetryDelaySeconds = 1f;

        private readonly Dictionary<PlayerRef, AuctionConnection> _connectionByPlayer = new Dictionary<PlayerRef, AuctionConnection>();
        private NetworkRunner _runner;
        private AuctionSession _session;
        private AuctionMatch _match;
        private AuctionPhase _lastBroadcastPhase = AuctionPhase.WaitingForPlayers;
        private float _settlementElapsed;
        private float _viewSyncElapsed;
        private bool _starting;
        private bool _dedicatedServerRestartPending;
        private bool _isQuitting;

        public AuctionWireView CurrentView { get; private set; }
        public string Status { get; private set; } = "尚未连接";
        public bool IsStarting => _starting;
        public bool CanStartClient => !_starting && _runner == null;

        private void Start()
        {
            if (IsDedicatedServer())
            {
                StartDedicatedServer();
            }
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void Update()
        {
            if (_runner == null || !_runner.IsServer || _match == null)
            {
                return;
            }

            if (_match.Phase == AuctionPhase.Settlement)
            {
                _settlementElapsed += Time.deltaTime;
                if (_settlementElapsed >= 3f)
                {
                    _match = _session.StartNextMatch();
                    _settlementElapsed = 0f;
                    BroadcastViews();
                }
                return;
            }

            _match.AdvanceTime(TimeSpan.FromSeconds(Time.deltaTime));
            _viewSyncElapsed += Time.deltaTime;
            if (_match.Phase != _lastBroadcastPhase || _viewSyncElapsed >= 0.1f)
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
            if (!runner.IsServer)
            {
                return;
            }

            if (_session == null)
            {
                _session = new AuctionSession(AuctionRules.CreateDemo(playerCount, initialAssets), new SystemRandomSource());
            }

            _connectionByPlayer[player] = _session.OpenConnection(Guid.NewGuid().ToString("N"));
            if (_match == null || _match.Phase == AuctionPhase.Settlement)
            {
                _match = _session.StartNextMatch();
                _settlementElapsed = 0f;
            }
            BroadcastViews();
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
            if (key.Equals(ViewKey))
            {
                CurrentView = JsonUtility.FromJson<AuctionWireView>(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count));
                return;
            }

            if (!runner.IsServer || !key.Equals(ActionKey) || _match == null || !_connectionByPlayer.TryGetValue(player, out var connection) || !_session.TryGetSeat(connection, out var seatIndex))
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
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer || _session == null || !_connectionByPlayer.TryGetValue(player, out var connection))
            {
                return;
            }

            _session.Disconnect(connection);
            _connectionByPlayer.Remove(player);
            BroadcastViews();
        }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (!ReferenceEquals(runner, _runner))
            {
                return;
            }

            _runner = null;
            CurrentView = null;
            if (!_starting)
            {
                Status = IsDedicatedServer()
                    ? $"专用服务端连接已关闭：{shutdownReason}。正在重新启动。"
                    : $"连接已关闭：{shutdownReason}。请重新连接。";
                Debug.LogWarning(Status);
            }
            Destroy(runner.gameObject);

            if (IsDedicatedServer() && !_isQuitting && isActiveAndEnabled)
            {
                ResetDedicatedServerMatch();
                RestartDedicatedServerAfterDisconnect();
            }
        }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

        private async void StartRunner(GameMode gameMode)
        {
            if (_starting || _runner != null)
            {
                return;
            }

            _starting = true;
            var attemptCount = gameMode == GameMode.Server ? dedicatedServerStartAttempts : 1;
            var lastFailure = string.Empty;

            try
            {
                for (var attempt = 1; attempt <= attemptCount; attempt++)
                {
                    Status = GetStartingStatus(gameMode, attempt, attemptCount);
                    var runner = CreateRunner();
                    runner.AddCallbacks(this);

                    try
                    {
                        var result = await runner.StartGame(new StartGameArgs
                        {
                            GameMode = gameMode,
                            SessionName = sessionName,
                            PlayerCount = playerCount,
                            IsVisible = false,
                            IsOpen = true
                        });

                        if (result.Ok)
                        {
                            _runner = runner;
                            Status = gameMode == GameMode.Server ? "专用服务端已启动" : "已连接到专用服务端";
                            Debug.Log(Status);
                            return;
                        }

                        lastFailure = DescribeStartFailure(result.ShutdownReason, result.ErrorMessage);
                    }
                    catch (Exception exception)
                    {
                        lastFailure = exception.Message;
                        Debug.LogException(exception);
                    }

                    DisposeFailedRunner(runner);
                    if (attempt < attemptCount)
                    {
                        Status = $"专用服务端启动失败：{lastFailure}。{dedicatedServerRetryDelaySeconds:0.#} 秒后重试（{attempt + 1}/{attemptCount}）";
                        Debug.LogWarning(Status);
                        await Task.Delay(Mathf.CeilToInt(dedicatedServerRetryDelaySeconds * 1000f));
                    }
                }

                Status = gameMode == GameMode.Server
                    ? $"专用服务端启动失败（已尝试 {attemptCount} 次）：{lastFailure}"
                    : $"连接失败：{lastFailure}。请重新连接。";
                Debug.LogError(Status);
            }
            finally
            {
                _starting = false;
            }
        }

        private static string GetStartingStatus(GameMode gameMode, int attempt, int attemptCount)
        {
            if (gameMode != GameMode.Server)
            {
                return "正在连接专用服务端";
            }

            return $"正在启动专用服务端（{attempt}/{attemptCount}）";
        }

        private static string DescribeStartFailure(ShutdownReason shutdownReason, string errorMessage)
        {
            return string.IsNullOrWhiteSpace(errorMessage)
                ? shutdownReason.ToString()
                : $"{shutdownReason}：{errorMessage}";
        }

        private async void RestartDedicatedServerAfterDisconnect()
        {
            if (_dedicatedServerRestartPending)
            {
                return;
            }

            _dedicatedServerRestartPending = true;
            Status = $"专用服务端将在 {dedicatedServerRetryDelaySeconds:0.#} 秒后重新启动。";
            Debug.LogWarning(Status);

            try
            {
                await Task.Delay(Mathf.CeilToInt(dedicatedServerRetryDelaySeconds * 1000f));
                if (!_isQuitting && isActiveAndEnabled && _runner == null)
                {
                    StartDedicatedServer();
                }
            }
            finally
            {
                _dedicatedServerRestartPending = false;
            }
        }

        private void ResetDedicatedServerMatch()
        {
            _connectionByPlayer.Clear();
            _session = null;
            _match = null;
            _settlementElapsed = 0f;
            _viewSyncElapsed = 0f;
            _lastBroadcastPhase = AuctionPhase.WaitingForPlayers;
        }

        private void DisposeFailedRunner(NetworkRunner runner)
        {
            if (runner == null)
            {
                return;
            }

            runner.RemoveCallbacks(this);
            if (runner.IsRunning)
            {
                runner.Shutdown();
            }
            else
            {
                Destroy(runner.gameObject);
            }
        }

        private NetworkRunner CreateRunner()
        {
            var runnerObject = new GameObject("Auction Network Runner");
            DontDestroyOnLoad(runnerObject);
            return runnerObject.AddComponent<NetworkRunner>();
        }

        private static bool IsDedicatedServer()
        {
            return Application.isBatchMode || HasCommandLineArgument("-auctionServer");
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

            foreach (var entry in _connectionByPlayer)
            {
                var view = _session.TryGetSeat(entry.Value, out var seatIndex)
                    ? AuctionWireView.From(seatIndex, _match.GetSeatView(seatIndex))
                    : AuctionWireView.Waiting();
                _runner.SendReliableDataToPlayer(
                    entry.Key,
                    ViewKey,
                    Encoding.UTF8.GetBytes(JsonUtility.ToJson(view)));
            }
            _lastBroadcastPhase = _match.Phase;
            _viewSyncElapsed = 0f;
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
        public float RemainingSeconds;
        public bool IsWaitingForNextMatch;
        public string PublicClue;
        public AuctionWireClueChoice[] PrivateClueChoices;
        public string PrivateClueResult;
        public AuctionWireKnowledge[] Knowledge;
        public int WinnerSlot;
        public int WinningBid;
        public AuctionWireRoundReveal RoundReveal;
        public AuctionWireKnowledge[] SettlementPackage;

        public static AuctionWireView From(int seatIndex, AuctionSeatView view)
        {
            return new AuctionWireView
            {
                SeatIndex = seatIndex,
                Phase = view.Phase.ToString(),
                AvailableAssets = view.AvailableAssets,
                GridWidth = view.Grid.Width,
                GridHeight = view.Grid.Height,
                RemainingSeconds = Mathf.Max(0f, (float)view.Remaining.TotalSeconds),
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
                WinningBid = view.Settlement == null ? 0 : view.Settlement.WinningBid,
                RoundReveal = view.RoundReveal == null ? null : new AuctionWireRoundReveal
                {
                    Round = view.RoundReveal.Round,
                    WinnerSlot = view.RoundReveal.Result.WinnerSlot,
                    WinningBid = view.RoundReveal.Result.WinningBid,
                    Seats = view.RoundReveal.Seats.Select(item => new AuctionWireRoundSeatReveal
                    {
                        SeatIndex = item.SeatIndex,
                        Bid = item.Bid,
                        ClueKind = item.ClueKind == null ? null : item.ClueKind.ToString()
                    }).ToArray()
                },
                SettlementPackage = view.SettlementPackage.Select(item => new AuctionWireKnowledge
                {
                    X = item.X,
                    Y = item.Y,
                    Width = item.Width,
                    Height = item.Height,
                    Rarity = item.Rarity.ToString(),
                    Name = item.Name,
                    Value = item.Value
                }).ToArray()
            };
        }

        public static AuctionWireView Waiting()
        {
            return new AuctionWireView { IsWaitingForNextMatch = true, Phase = "WaitingForPlayers" };
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

    [Serializable]
    public sealed class AuctionWireRoundReveal
    {
        public int Round;
        public int WinnerSlot;
        public int WinningBid;
        public AuctionWireRoundSeatReveal[] Seats;
    }

    [Serializable]
    public sealed class AuctionWireRoundSeatReveal
    {
        public int SeatIndex;
        public int Bid;
        public string ClueKind;
    }
}
