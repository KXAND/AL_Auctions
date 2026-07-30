using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame
{
    public enum AuctionPhase
    {
        WaitingForPlayers,
        Analysis,
        Bidding,
        Settlement
    }

    public sealed class AuctionMatch
    {
        private const int DemoPackageTotalValue = 100;
        private readonly AuctionRules _rules;
        private readonly AuctionPackage _package;
        private readonly List<AuctionSeat> _seats;
        private readonly List<PrivateClueChoice> _privateClueChoices;
        private AuctionPhase _phase;
        private TimeSpan _remainingPhaseTime;
        private AuctionSettlement _settlement;

        private AuctionMatch(AuctionRules rules, AuctionPackage package)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _seats = Enumerable.Range(0, rules.PlayerCount).Select(index => new AuctionSeat(index)).ToList();
            _privateClueChoices = new List<PrivateClueChoice>
            {
                new PrivateClueChoice("rarity-mark", "查看珍稀度标记")
            };
            _phase = AuctionPhase.WaitingForPlayers;
        }

        public static AuctionMatch CreateDemo(AuctionRules rules)
        {
            return new AuctionMatch(rules, new AuctionPackage(DemoPackageTotalValue, rules.GridWidth, height: 2));
        }

        public int ConnectHuman(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                throw new ArgumentException("连接身份不能为空。", nameof(connectionId));
            }

            if (_phase != AuctionPhase.WaitingForPlayers)
            {
                throw new InvalidOperationException("对局开始后不能加入席位。");
            }

            var seat = _seats.FirstOrDefault(candidate => candidate.Controller == SeatController.None);
            if (seat == null)
            {
                throw new InvalidOperationException("没有可用的玩家席位。");
            }

            seat.AssignHuman(connectionId, _rules.InitialAssets);
            return seat.Index;
        }

        public void Start()
        {
            if (_phase != AuctionPhase.WaitingForPlayers)
            {
                throw new InvalidOperationException("对局已经开始。");
            }

            foreach (var seat in _seats.Where(candidate => candidate.Controller == SeatController.None))
            {
                seat.AssignAi();
            }

            _phase = AuctionPhase.Analysis;
            _remainingPhaseTime = _rules.AnalysisDuration;
        }

        public void SelectPrivateClue(int seatIndex, string clueId)
        {
            EnsurePhase(AuctionPhase.Analysis);
            var seat = GetSeat(seatIndex);
            var choice = _privateClueChoices.FirstOrDefault(candidate => candidate.Id == clueId);

            if (choice == null)
            {
                throw new ArgumentOutOfRangeException(nameof(clueId));
            }

            if (seat.PrivateClueResult != null)
            {
                throw new InvalidOperationException("该席位本轮已经选择过私有线索。");
            }

            seat.SetPrivateClue(new PrivateClueResult(choice.Id, "该包裹中存在一件稀有藏品。"));
        }

        public void SubmitBid(int seatIndex, int amount)
        {
            EnsurePhase(AuctionPhase.Bidding);
            var seat = GetSeat(seatIndex);

            if (amount < 0 || amount > seat.AvailableAssets)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (seat.HasSubmittedBid)
            {
                throw new InvalidOperationException("该席位本轮已经提交过出价。");
            }

            seat.SetBid(amount);
        }

        public void AdvanceTime(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }

            if (_phase != AuctionPhase.Analysis && _phase != AuctionPhase.Bidding)
            {
                return;
            }

            _remainingPhaseTime -= elapsed;
            if (_remainingPhaseTime > TimeSpan.Zero)
            {
                return;
            }

            if (_phase == AuctionPhase.Analysis)
            {
                _phase = AuctionPhase.Bidding;
                _remainingPhaseTime = _rules.BiddingDuration;
                return;
            }

            CompleteDemoAuction();
        }

        public AuctionSeatView GetSeatView(int seatIndex)
        {
            var seat = GetSeat(seatIndex);
            return new AuctionSeatView(
                _phase,
                seat.AvailableAssets,
                new AuctionGridView(_package.GridWidth, _package.GridHeight),
                new PublicClueView("公共线索：包裹中至少有一件藏品。"),
                _phase == AuctionPhase.Analysis ? _privateClueChoices.ToArray() : Array.Empty<PrivateClueChoice>(),
                seat.PrivateClueResult,
                _settlement == null ? null : new AuctionSettlementView(_settlement.WinnerSlot, _settlement.WinningBid));
        }

        private void CompleteDemoAuction()
        {
            foreach (var seat in _seats.Where(candidate => !candidate.HasSubmittedBid))
            {
                seat.SetBid(0);
            }

            var winner = _seats
                .Where(candidate => candidate.Bid > 0)
                .OrderByDescending(candidate => candidate.Bid)
                .ThenBy(candidate => candidate.Index)
                .FirstOrDefault();

            if (winner == null)
            {
                _settlement = new AuctionSettlement(-1, 0);
            }
            else
            {
                winner.ChangeAssets(_package.TotalValue - winner.Bid);
                _settlement = new AuctionSettlement(winner.Index, winner.Bid);
            }

            _phase = AuctionPhase.Settlement;
            _remainingPhaseTime = TimeSpan.Zero;
        }

        private AuctionSeat GetSeat(int seatIndex)
        {
            if (seatIndex < 0 || seatIndex >= _seats.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(seatIndex));
            }

            return _seats[seatIndex];
        }

        private void EnsurePhase(AuctionPhase expectedPhase)
        {
            if (_phase != expectedPhase)
            {
                throw new InvalidOperationException("当前不接受该席位动作。");
            }
        }

        private sealed class AuctionPackage
        {
            public AuctionPackage(int totalValue, int gridWidth, int height)
            {
                TotalValue = totalValue;
                GridWidth = gridWidth;
                GridHeight = height;
            }

            public int TotalValue { get; }
            public int GridWidth { get; }
            public int GridHeight { get; }
        }

        private sealed class AuctionSeat
        {
            public AuctionSeat(int index)
            {
                Index = index;
                Controller = SeatController.None;
            }

            public int Index { get; }
            public SeatController Controller { get; private set; }
            public int AvailableAssets { get; private set; }
            public int Bid { get; private set; }
            public bool HasSubmittedBid { get; private set; }
            public PrivateClueResult PrivateClueResult { get; private set; }

            public void AssignHuman(string connectionId, int initialAssets)
            {
                Controller = SeatController.Human;
                AvailableAssets = initialAssets;
            }

            public void AssignAi()
            {
                Controller = SeatController.Ai;
            }

            public void SetPrivateClue(PrivateClueResult result)
            {
                PrivateClueResult = result;
            }

            public void SetBid(int amount)
            {
                Bid = amount;
                HasSubmittedBid = true;
            }

            public void ChangeAssets(int amount)
            {
                AvailableAssets += amount;
            }
        }

        private sealed class AuctionSettlement
        {
            public AuctionSettlement(int winnerSlot, int winningBid)
            {
                WinnerSlot = winnerSlot;
                WinningBid = winningBid;
            }

            public int WinnerSlot { get; }
            public int WinningBid { get; }
        }

        private enum SeatController
        {
            None,
            Human,
            Ai
        }
    }

    public sealed class AuctionSeatView
    {
        public AuctionSeatView(
            AuctionPhase phase,
            int availableAssets,
            AuctionGridView grid,
            PublicClueView publicClue,
            IReadOnlyList<PrivateClueChoice> privateClueChoices,
            PrivateClueResult privateClueResult,
            AuctionSettlementView settlement)
        {
            Phase = phase;
            AvailableAssets = availableAssets;
            Grid = grid;
            PublicClue = publicClue;
            PrivateClueChoices = privateClueChoices;
            PrivateClueResult = privateClueResult;
            Settlement = settlement;
        }

        public AuctionPhase Phase { get; }
        public int AvailableAssets { get; }
        public AuctionGridView Grid { get; }
        public PublicClueView PublicClue { get; }
        public IReadOnlyList<PrivateClueChoice> PrivateClueChoices { get; }
        public PrivateClueResult PrivateClueResult { get; }
        public AuctionSettlementView Settlement { get; }
    }

    public sealed class AuctionGridView
    {
        public AuctionGridView(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }
    }

    public sealed class PublicClueView
    {
        public PublicClueView(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    public sealed class PrivateClueChoice
    {
        public PrivateClueChoice(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }
    }

    public sealed class PrivateClueResult
    {
        public PrivateClueResult(string clueId, string text)
        {
            ClueId = clueId;
            Text = text;
        }

        public string ClueId { get; }
        public string Text { get; }
    }

    public sealed class AuctionSettlementView
    {
        public AuctionSettlementView(int winnerSlot, int winningBid)
        {
            WinnerSlot = winnerSlot;
            WinningBid = winningBid;
        }

        public int WinnerSlot { get; }
        public int WinningBid { get; }
    }
}
