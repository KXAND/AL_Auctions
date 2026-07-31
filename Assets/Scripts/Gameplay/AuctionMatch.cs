using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame.Gameplay
{
    public enum AuctionPhase { WaitingForPlayers, Analysis, Bidding, RoundReveal, Settlement }
    public enum ClueKind { RarityShape, RandomFullReveal }

    public sealed class AuctionMatch
    {
        private readonly AuctionRules _rules;
        private readonly IRandomSource _random;
        private readonly AiActionScheduler _aiScheduler;
        private readonly GeneratedPackage _package;
        private readonly List<AuctionSeat> _seats;
        private readonly List<ScheduledAiAction> _scheduledAiActions = new List<ScheduledAiAction>();
        private List<PrivateClueChoice> _choices;
        private AuctionPhase _phase;
        private TimeSpan _remaining;
        private TimeSpan _phaseElapsed;
        private AuctionSettlement _settlement;
        private AuctionRoundReveal _roundReveal;
        private bool _startNextRoundAfterReveal;
        private PublicClueView _publicClue;
        private int _round = 1;
        private int _consecutivePasses;
        private long _acceptedBidOrder;

        private AuctionMatch(AuctionRules rules, IRandomSource random)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _random = random ?? new SystemRandomSource();
            _aiScheduler = new AiActionScheduler(_random);
            _package = new PackageGenerator(rules, _random).Generate();
            _seats = Enumerable.Range(0, rules.PlayerCount).Select(index => new AuctionSeat(index)).ToList();
            _choices = new List<PrivateClueChoice>();
            _publicClue = new PublicClueView(new PrivateClueResult("none", null, "暂无公共线索", Array.Empty<CollectibleKnowledge>()));
            _phase = AuctionPhase.WaitingForPlayers;
        }

        public AuctionPhase Phase => _phase;

        public static AuctionMatch CreateDemo(AuctionRules rules) => Create(rules, new SystemRandomSource());
        public static AuctionMatch Create(AuctionRules rules, IRandomSource random) => new AuctionMatch(rules, random);

        public int ConnectHuman(string connectionId)
        {
            return ConnectHuman(new AuctionConnection(connectionId, _rules.InitialAssets));
        }

        public int ConnectHuman(AuctionConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (!connection.IsConnected) throw new InvalidOperationException("已断开的连接不能加入席位。");
            if (_phase != AuctionPhase.WaitingForPlayers) throw new InvalidOperationException("对局开始后不能加入席位。");
            var seat = _seats.FirstOrDefault(item => item.Controller == SeatController.None) ?? throw new InvalidOperationException("没有可用席位。");
            seat.AssignHuman(connection);
            return seat.Index;
        }

        public void DisconnectHuman(int seatIndex)
        {
            var seat = Seat(seatIndex);
            if (seat.Controller != SeatController.Human) throw new InvalidOperationException("只有真人席位可以断线接管。");
            seat.Disconnect();
            seat.AssignAi(seat.AvailableAssets, discardSettlement: true);
            ScheduleAiAction(seat);
            CapPendingAiActionsAfterHumanCompletion();
            ExecuteDueAiActions();
        }

        public bool IsAiControlled(int seatIndex) => Seat(seatIndex).Controller == SeatController.Ai;

        public void Start()
        {
            if (_phase != AuctionPhase.WaitingForPlayers) throw new InvalidOperationException("对局已经开始。");
            foreach (var seat in _seats.Where(item => item.Controller == SeatController.None)) seat.AssignAi(_rules.InitialAssets);
            BeginAnalysis();
            ExecuteDueAiActions();
        }

        public void SelectPrivateClue(int seatIndex, string clueId)
        {
            Ensure(AuctionPhase.Analysis);
            var seat = Seat(seatIndex);
            if (seat.PrivateClueResult != null) throw new InvalidOperationException("每轮只能选择一条私有线索。");
            var choice = _choices.SingleOrDefault(item => item.Id == clueId) ?? throw new ArgumentOutOfRangeException(nameof(clueId));
            seat.SetPrivateClue(CreateClueResult(choice));
            if (seat.Controller == SeatController.Human) CapPendingAiActionsAfterHumanCompletion();
        }

        public void SubmitBid(int seatIndex, int amount)
        {
            Ensure(AuctionPhase.Bidding);
            var seat = Seat(seatIndex);
            if (amount < 0 || amount > seat.AvailableAssets) throw new ArgumentOutOfRangeException(nameof(amount));
            if (seat.HasSubmittedBid) throw new InvalidOperationException("该席位已提交出价。");
            seat.SetBid(amount, ++_acceptedBidOrder);
            if (seat.Controller == SeatController.Human) CapPendingAiActionsAfterHumanCompletion();
        }

        public void AdvanceTime(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            while (elapsed > TimeSpan.Zero && IsTimedPhase())
            {
                ExecuteDueAiActions();
                CompleteElapsedPhases();
                if (!IsTimedPhase()) return;

                var step = elapsed < _remaining ? elapsed : _remaining;
                if (_phase == AuctionPhase.Analysis || _phase == AuctionPhase.Bidding)
                {
                    var untilAiAction = TimeUntilNextAiAction();
                    if (untilAiAction < step) step = untilAiAction;
                }

                if (step <= TimeSpan.Zero)
                {
                    ExecuteDueAiActions();
                    CompleteElapsedPhases();
                    continue;
                }

                _remaining -= step;
                _phaseElapsed += step;
                elapsed -= step;
                ExecuteDueAiActions();
                CompleteElapsedPhases();
            }
            CompleteElapsedPhases();
        }

        public AuctionSeatView GetSeatView(int seatIndex)
        {
            var seat = Seat(seatIndex);
            var knowledge = _publicClue.Knowledge.Concat(seat.Knowledge).ToArray();
            var settlementPackage = _phase == AuctionPhase.Settlement
                ? _package.Items.Select(item => Knowledge(item, true)).ToArray()
                : Array.Empty<CollectibleKnowledge>();
            return new AuctionSeatView(
                _phase,
                seat.AvailableAssets,
                _remaining,
                new AuctionGridView(_package.Layout.Width, _package.Layout.Height),
                _publicClue,
                _phase == AuctionPhase.Analysis ? _choices.ToArray() : Array.Empty<PrivateClueChoice>(),
                seat.PrivateClueResult,
                knowledge,
                _roundReveal,
                _settlement == null ? null : new AuctionSettlementView(_settlement.WinnerSlot, _settlement.WinningBid),
                settlementPackage);
        }

        private void BeginAnalysis()
        {
            _phase = AuctionPhase.Analysis;
            _remaining = _rules.AnalysisDuration;
            _phaseElapsed = TimeSpan.Zero;
            foreach (var seat in _seats) seat.BeginRound();
            var offers = AllOffers();
            _choices = offers.OrderBy(_ => _random.Next(int.MaxValue)).Take(Math.Min(_rules.CandidateClueCount, offers.Count)).ToList();
            _publicClue = new PublicClueView(CreateClueResult(offers[_random.Next(offers.Count)]));
            ScheduleAiActions();
        }

        private void BeginBidding()
        {
            _phase = AuctionPhase.Bidding;
            _remaining = _rules.BiddingDuration;
            _phaseElapsed = TimeSpan.Zero;
            ScheduleAiActions();
            ExecuteDueAiActions();
        }

        private void BeginRoundReveal(bool startNextRoundAfterReveal)
        {
            _phase = AuctionPhase.RoundReveal;
            _remaining = _rules.RoundRevealDuration;
            _phaseElapsed = TimeSpan.Zero;
            _startNextRoundAfterReveal = startNextRoundAfterReveal;
        }

        private void CompleteRoundReveal()
        {
            if (_startNextRoundAfterReveal)
            {
                _round++;
                BeginAnalysis();
                return;
            }

            _phase = AuctionPhase.Settlement;
            _remaining = TimeSpan.Zero;
            _phaseElapsed = TimeSpan.Zero;
        }

        private List<PrivateClueChoice> AllOffers()
        {
            var offers = _rules.Catalogue
                .Select(item => item.Rarity)
                .Distinct()
                .Select(rarity => new PrivateClueChoice($"rarity-{rarity}", $"查看 {rarity} 珍稀度形状", ClueKind.RarityShape, rarity))
                .ToList();
            offers.Add(new PrivateClueChoice("random-full", "随机完整揭示", ClueKind.RandomFullReveal, null));
            return offers;
        }

        private PrivateClueResult CreateClueResult(PrivateClueChoice choice)
        {
            var items = choice.Kind == ClueKind.RarityShape
                ? _package.Items.Where(item => item.Definition.Rarity == choice.Rarity).ToArray()
                : new[] { _package.Items[_random.Next(_package.Items.Count)] };
            return new PrivateClueResult(
                choice.Id,
                choice.Kind,
                choice.Kind == ClueKind.RarityShape ? $"{choice.Rarity} 珍稀度形状" : "随机完整揭示",
                items.Select(item => Knowledge(item, choice.Kind == ClueKind.RandomFullReveal)).ToArray());
        }

        private CollectibleKnowledge Knowledge(CollectibleInstance instance, bool full)
        {
            var place = _package.Layout.For(instance.Id);
            return new CollectibleKnowledge(
                place.X,
                place.Y,
                place.Width,
                place.Height,
                instance.Definition.Rarity,
                full ? instance.Definition.Name : null,
                full ? instance.Definition.AppearanceKey : null,
                full ? (int?)instance.Definition.Value : null);
        }

        private void ScheduleAiActions()
        {
            _scheduledAiActions.Clear();
            foreach (var seat in _seats.Where(item => item.Controller == SeatController.Ai)) ScheduleAiAction(seat);
        }

        private void ScheduleAiAction(AuctionSeat seat)
        {
            if ((_phase != AuctionPhase.Analysis && _phase != AuctionPhase.Bidding) || _scheduledAiActions.Any(item => item.SeatIndex == seat.Index)) return;
            var delay = _aiScheduler.Schedule(_rules.AiMaximumActionDelay, AllHumanActionsCompleted());
            _scheduledAiActions.Add(new ScheduledAiAction(seat.Index, _phaseElapsed + delay));
        }

        private void CapPendingAiActionsAfterHumanCompletion()
        {
            if (!AllHumanActionsCompleted()) return;

            var deadline = _phaseElapsed + TimeSpan.FromSeconds(1);
            foreach (var action in _scheduledAiActions)
            {
                if (action.DueAt > deadline) action.DueAt = deadline;
            }
            ExecuteDueAiActions();
        }

        private bool AllHumanActionsCompleted()
        {
            return _seats
                .Where(item => item.Controller == SeatController.Human)
                .All(item => _phase == AuctionPhase.Analysis ? item.PrivateClueResult != null : item.HasSubmittedBid);
        }

        private void ExecuteDueAiActions()
        {
            var due = _scheduledAiActions.Where(item => item.DueAt <= _phaseElapsed).ToArray();
            foreach (var action in due)
            {
                _scheduledAiActions.Remove(action);
                var seat = Seat(action.SeatIndex);
                if (seat.Controller != SeatController.Ai) continue;

                var view = GetSeatView(seat.Index);
                var ai = new SimpleRuleAi();
                if (_phase == AuctionPhase.Analysis && seat.PrivateClueResult == null)
                {
                    var clueId = ai.ChoosePrivateClue(view);
                    if (clueId != null) SelectPrivateClue(seat.Index, clueId);
                }
                else if (_phase == AuctionPhase.Bidding && !seat.HasSubmittedBid)
                {
                    SubmitBid(seat.Index, ai.ChooseBid(view));
                }
            }
        }

        private TimeSpan TimeUntilNextAiAction()
        {
            if (_scheduledAiActions.Count == 0) return _remaining;
            var dueAt = _scheduledAiActions.Min(item => item.DueAt);
            return dueAt <= _phaseElapsed ? TimeSpan.Zero : dueAt - _phaseElapsed;
        }

        private void CompleteElapsedPhases()
        {
            while (IsTimedPhase() && _remaining <= TimeSpan.Zero)
            {
                if (_phase == AuctionPhase.Analysis) BeginBidding();
                else if (_phase == AuctionPhase.Bidding) CompleteAuctionRound();
                else CompleteRoundReveal();
            }
        }

        private bool IsTimedPhase()
        {
            return _phase == AuctionPhase.Analysis || _phase == AuctionPhase.Bidding || _phase == AuctionPhase.RoundReveal;
        }

        private void CompleteAuctionRound()
        {
            _scheduledAiActions.Clear();
            foreach (var seat in _seats.Where(item => !item.HasSubmittedBid)) seat.SetBid(0, ++_acceptedBidOrder);
            var ranked = _seats.Where(item => item.Bid > 0).OrderByDescending(item => item.Bid).ThenBy(item => item.AcceptedBidOrder).ToArray();
            if (ranked.Length == 0)
            {
                SetRoundReveal(-1, 0);
                if (_round >= 5 && ++_consecutivePasses >= _rules.ConsecutivePassLimit)
                {
                    _settlement = new AuctionSettlement(-1, 0);
                    BeginRoundReveal(false);
                    return;
                }

                BeginRoundReveal(true);
                return;
            }

            _consecutivePasses = 0;
            var winner = ranked[0];
            var second = ranked.Length == 1 ? 0 : ranked[1].Bid;
            var multiplier = _round <= _rules.WinningMultipliers.Count ? _rules.WinningMultipliers[_round - 1] : _rules.FinalWinningMultiplier;
            if (ranked.Length > 1 && winner.Bid < multiplier * second)
            {
                SetRoundReveal(-1, 0);
                BeginRoundReveal(true);
                return;
            }

            var delta = _package.TotalValue - winner.Bid;
            if (!winner.DiscardSettlement) winner.ChangeAssets(delta);
            if (delta < 0)
            {
                var each = (int)Math.Floor(_rules.LossDistributionRatio * -delta / (_seats.Count - 1));
                foreach (var seat in _seats.Where(item => item != winner)) seat.ChangeAssets(each);
            }

            _settlement = new AuctionSettlement(winner.Index, winner.Bid);
            SetRoundReveal(winner.Index, winner.Bid);
            BeginRoundReveal(false);
        }

        private void SetRoundReveal(int winnerSlot, int winningBid)
        {
            _roundReveal = new AuctionRoundReveal(
                _round,
                _seats.Select(item => new AuctionRoundSeatReveal(item.Index, item.Bid, item.PrivateClueResult == null ? (ClueKind?)null : item.PrivateClueResult.Kind)).ToArray(),
                new AuctionSettlementView(winnerSlot, winningBid));
        }

        private AuctionSeat Seat(int index)
        {
            return index >= 0 && index < _seats.Count ? _seats[index] : throw new ArgumentOutOfRangeException(nameof(index));
        }

        private void Ensure(AuctionPhase phase)
        {
            if (_phase != phase) throw new InvalidOperationException("当前不接受该席位动作。");
        }

        private sealed class ScheduledAiAction
        {
            public ScheduledAiAction(int seatIndex, TimeSpan dueAt) { SeatIndex = seatIndex; DueAt = dueAt; }
            public int SeatIndex { get; }
            public TimeSpan DueAt { get; set; }
        }

        private sealed class AuctionSeat
        {
            public AuctionSeat(int index) { Index = index; }

            public int Index { get; }
            public SeatController Controller { get; private set; }
            public bool DiscardSettlement { get; private set; }
            public int AvailableAssets { get; private set; }
            public int Bid { get; private set; }
            public long AcceptedBidOrder { get; private set; }
            public bool HasSubmittedBid { get; private set; }
            public PrivateClueResult PrivateClueResult { get; private set; }
            public AuctionConnection Connection { get; private set; }
            public List<CollectibleKnowledge> Knowledge { get; } = new List<CollectibleKnowledge>();

            public void AssignHuman(AuctionConnection connection)
            {
                Controller = SeatController.Human;
                DiscardSettlement = false;
                Connection = connection;
                AvailableAssets = connection.AvailableAssets;
            }

            public void AssignAi(int assets, bool discardSettlement = false)
            {
                Controller = SeatController.Ai;
                DiscardSettlement = discardSettlement;
                Connection = null;
                AvailableAssets = assets;
            }

            public void Disconnect()
            {
                Connection?.Disconnect();
                Connection = null;
            }

            public void BeginRound()
            {
                Bid = 0;
                AcceptedBidOrder = 0;
                HasSubmittedBid = false;
                PrivateClueResult = null;
            }

            public void SetPrivateClue(PrivateClueResult value)
            {
                PrivateClueResult = value;
                Knowledge.AddRange(value.Knowledge);
            }

            public void SetBid(int value, long order)
            {
                Bid = value;
                AcceptedBidOrder = order;
                HasSubmittedBid = true;
            }

            public void ChangeAssets(int value)
            {
                AvailableAssets += value;
                if (Connection != null && Connection.IsConnected) Connection.SetAvailableAssets(AvailableAssets);
            }
        }

        private sealed class AuctionSettlement
        {
            public AuctionSettlement(int winnerSlot, int winningBid) { WinnerSlot = winnerSlot; WinningBid = winningBid; }
            public int WinnerSlot { get; }
            public int WinningBid { get; }
        }

        private enum SeatController { None, Human, Ai }
    }

    public sealed class AuctionSeatView
    {
        public AuctionSeatView(AuctionPhase phase, int assets, TimeSpan remaining, AuctionGridView grid, PublicClueView publicClue, IReadOnlyList<PrivateClueChoice> choices, PrivateClueResult result, IReadOnlyList<CollectibleKnowledge> knowledge, AuctionRoundReveal roundReveal, AuctionSettlementView settlement, IReadOnlyList<CollectibleKnowledge> settlementPackage)
        {
            Phase = phase;
            AvailableAssets = assets;
            Remaining = remaining;
            Grid = grid;
            PublicClue = publicClue;
            PrivateClueChoices = choices;
            PrivateClueResult = result;
            Knowledge = knowledge;
            RoundReveal = roundReveal;
            Settlement = settlement;
            SettlementPackage = settlementPackage;
        }

        public AuctionPhase Phase { get; }
        public int AvailableAssets { get; }
        public TimeSpan Remaining { get; }
        public AuctionGridView Grid { get; }
        public PublicClueView PublicClue { get; }
        public IReadOnlyList<PrivateClueChoice> PrivateClueChoices { get; }
        public PrivateClueResult PrivateClueResult { get; }
        public IReadOnlyList<CollectibleKnowledge> Knowledge { get; }
        public AuctionRoundReveal RoundReveal { get; }
        public AuctionSettlementView Settlement { get; }
        public IReadOnlyList<CollectibleKnowledge> SettlementPackage { get; }
    }

    public sealed class AuctionGridView
    {
        public AuctionGridView(int width, int height) { Width = width; Height = height; KnownItems = Array.Empty<CollectibleKnowledge>(); }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<CollectibleKnowledge> KnownItems { get; }
    }

    public sealed class PublicClueView
    {
        public PublicClueView(PrivateClueResult result) { Text = $"公共线索：{result.Text}"; Knowledge = result.Knowledge; }
        public string Text { get; }
        public IReadOnlyList<CollectibleKnowledge> Knowledge { get; }
    }

    public sealed class PrivateClueChoice
    {
        public PrivateClueChoice(string id, string label, ClueKind kind, CollectibleRarity? rarity) { Id = id; Label = label; Kind = kind; Rarity = rarity; }
        public string Id { get; }
        public string Label { get; }
        public ClueKind Kind { get; }
        public CollectibleRarity? Rarity { get; }
    }

    public sealed class PrivateClueResult
    {
        public PrivateClueResult(string clueId, ClueKind? kind, string text, IReadOnlyList<CollectibleKnowledge> knowledge) { ClueId = clueId; Kind = kind; Text = text; Knowledge = knowledge; }
        public string ClueId { get; }
        public ClueKind? Kind { get; }
        public string Text { get; }
        public IReadOnlyList<CollectibleKnowledge> Knowledge { get; }
    }

    public sealed class CollectibleKnowledge
    {
        public CollectibleKnowledge(int x, int y, int width, int height, CollectibleRarity rarity, string name, string appearanceKey, int? value) { X = x; Y = y; Width = width; Height = height; Rarity = rarity; Name = name; AppearanceKey = appearanceKey; Value = value; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public CollectibleRarity Rarity { get; }
        public string Name { get; }
        public string AppearanceKey { get; }
        public int? Value { get; }
    }

    public sealed class AuctionRoundReveal
    {
        public AuctionRoundReveal(int round, IReadOnlyList<AuctionRoundSeatReveal> seats, AuctionSettlementView result) { Round = round; Seats = seats; Result = result; }
        public int Round { get; }
        public IReadOnlyList<AuctionRoundSeatReveal> Seats { get; }
        public AuctionSettlementView Result { get; }
    }

    public sealed class AuctionRoundSeatReveal
    {
        public AuctionRoundSeatReveal(int seatIndex, int bid, ClueKind? clueKind) { SeatIndex = seatIndex; Bid = bid; ClueKind = clueKind; }
        public int SeatIndex { get; }
        public int Bid { get; }
        public ClueKind? ClueKind { get; }
    }

    public sealed class AuctionSettlementView
    {
        public AuctionSettlementView(int winnerSlot, int winningBid) { WinnerSlot = winnerSlot; WinningBid = winningBid; }
        public int WinnerSlot { get; }
        public int WinningBid { get; }
    }
}
