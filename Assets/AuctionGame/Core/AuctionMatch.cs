using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame
{
    public enum AuctionPhase { WaitingForPlayers, Analysis, Bidding, Settlement }
    public enum ClueKind { RarityShape, RandomFullReveal }

    public sealed class AuctionMatch
    {
        private readonly AuctionRules _rules; private readonly IRandomSource _random; private readonly GeneratedPackage _package; private readonly List<AuctionSeat> _seats; private List<PrivateClueChoice> _choices; private AuctionPhase _phase; private TimeSpan _remaining; private AuctionSettlement _settlement; private PublicClueView _publicClue;
        private AuctionMatch(AuctionRules rules, IRandomSource random)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules)); _random = random ?? new SystemRandomSource(); _package = new PackageGenerator(rules, _random).Generate();
            _seats = Enumerable.Range(0, rules.PlayerCount).Select(index => new AuctionSeat(index)).ToList(); _choices = new List<PrivateClueChoice>(); _phase = AuctionPhase.WaitingForPlayers;
        }
        public static AuctionMatch CreateDemo(AuctionRules rules) => Create(rules, new SystemRandomSource());
        public static AuctionMatch Create(AuctionRules rules, IRandomSource random) => new AuctionMatch(rules, random);
        public int ConnectHuman(string connectionId)
        {
            if (_phase != AuctionPhase.WaitingForPlayers) throw new InvalidOperationException("对局开始后不能加入席位。");
            var seat = _seats.FirstOrDefault(item => item.Controller == SeatController.None) ?? throw new InvalidOperationException("没有可用席位。"); seat.AssignHuman(_rules.InitialAssets); return seat.Index;
        }
        public void Start()
        {
            if (_phase != AuctionPhase.WaitingForPlayers) throw new InvalidOperationException("对局已经开始。");
            foreach (var seat in _seats.Where(item => item.Controller == SeatController.None)) seat.AssignAi(_rules.InitialAssets);
            BeginAnalysis();
        }
        public void SelectPrivateClue(int seatIndex, string clueId)
        {
            Ensure(AuctionPhase.Analysis); var seat = Seat(seatIndex); if (seat.PrivateClueResult != null) throw new InvalidOperationException("每轮只能选择一条私有线索。");
            var choice = _choices.SingleOrDefault(item => item.Id == clueId) ?? throw new ArgumentOutOfRangeException(nameof(clueId));
            seat.SetPrivateClue(CreateClueResult(choice));
        }
        public void SubmitBid(int seatIndex, int amount)
        {
            Ensure(AuctionPhase.Bidding); var seat = Seat(seatIndex); if (amount < 0 || amount > seat.AvailableAssets) throw new ArgumentOutOfRangeException(nameof(amount)); if (seat.HasSubmittedBid) throw new InvalidOperationException("该席位已提交出价。"); seat.SetBid(amount);
        }
        public void AdvanceTime(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed)); if (_phase != AuctionPhase.Analysis && _phase != AuctionPhase.Bidding) return; _remaining -= elapsed; if (_remaining > TimeSpan.Zero) return;
            if (_phase == AuctionPhase.Analysis) { _phase = AuctionPhase.Bidding; _remaining = _rules.BiddingDuration; return; }
            CompleteDemoAuction();
        }
        public AuctionSeatView GetSeatView(int seatIndex)
        {
            var seat = Seat(seatIndex); var knowledge = _publicClue.Knowledge.Concat(seat.Knowledge).ToArray();
            return new AuctionSeatView(_phase, seat.AvailableAssets, new AuctionGridView(_package.Layout.Width, _package.Layout.Height), _publicClue, _phase == AuctionPhase.Analysis ? _choices.ToArray() : Array.Empty<PrivateClueChoice>(), seat.PrivateClueResult, knowledge, _settlement == null ? null : new AuctionSettlementView(_settlement.WinnerSlot, _settlement.WinningBid));
        }
        private void BeginAnalysis()
        {
            _phase = AuctionPhase.Analysis; _remaining = _rules.AnalysisDuration; foreach (var seat in _seats) seat.BeginRound();
            var offers = AllOffers(); _choices = offers.OrderBy(_ => _random.Next(int.MaxValue)).Take(Math.Min(_rules.CandidateClueCount, offers.Count)).ToList();
            _publicClue = new PublicClueView(CreateClueResult(offers[_random.Next(offers.Count)]));
        }
        private List<PrivateClueChoice> AllOffers()
        {
            var offers = _rules.Catalogue.Select(item => item.Rarity).Distinct().Select(rarity => new PrivateClueChoice($"rarity-{rarity}", $"查看 {rarity} 珍稀度形状", ClueKind.RarityShape, rarity)).ToList(); offers.Add(new PrivateClueChoice("random-full", "随机完整揭示", ClueKind.RandomFullReveal, null)); return offers;
        }
        private PrivateClueResult CreateClueResult(PrivateClueChoice choice)
        {
            var items = choice.Kind == ClueKind.RarityShape ? _package.Items.Where(item => item.Definition.Rarity == choice.Rarity).ToArray() : new[] { _package.Items[_random.Next(_package.Items.Count)] };
            return new PrivateClueResult(choice.Id, choice.Kind == ClueKind.RarityShape ? $"{choice.Rarity} 珍稀度形状" : "随机完整揭示", items.Select(item => Knowledge(item, choice.Kind == ClueKind.RandomFullReveal)).ToArray());
        }
        private CollectibleKnowledge Knowledge(CollectibleInstance instance, bool full)
        {
            var place = _package.Layout.For(instance.Id); return new CollectibleKnowledge(place.X, place.Y, place.Width, place.Height, instance.Definition.Rarity, full ? instance.Definition.Name : null, full ? instance.Definition.AppearanceKey : null, full ? (int?)instance.Definition.Value : null);
        }
        private void CompleteDemoAuction()
        {
            foreach (var seat in _seats.Where(item => !item.HasSubmittedBid)) seat.SetBid(0); var winner = _seats.Where(item => item.Bid > 0).OrderByDescending(item => item.Bid).ThenBy(item => item.Index).FirstOrDefault();
            if (winner == null) _settlement = new AuctionSettlement(-1, 0); else { winner.ChangeAssets(_package.TotalValue - winner.Bid); _settlement = new AuctionSettlement(winner.Index, winner.Bid); }
            _phase = AuctionPhase.Settlement; _remaining = TimeSpan.Zero;
        }
        private AuctionSeat Seat(int index) => index >= 0 && index < _seats.Count ? _seats[index] : throw new ArgumentOutOfRangeException(nameof(index));
        private void Ensure(AuctionPhase phase) { if (_phase != phase) throw new InvalidOperationException("当前不接受该席位动作。"); }
        private sealed class AuctionSeat
        {
            public AuctionSeat(int index) { Index = index; } public int Index { get; } public SeatController Controller { get; private set; } public int AvailableAssets { get; private set; } public int Bid { get; private set; } public bool HasSubmittedBid { get; private set; } public PrivateClueResult PrivateClueResult { get; private set; } public List<CollectibleKnowledge> Knowledge { get; } = new List<CollectibleKnowledge>();
            public void AssignHuman(int assets) { Controller = SeatController.Human; AvailableAssets = assets; } public void AssignAi(int assets) { Controller = SeatController.Ai; AvailableAssets = assets; } public void BeginRound() { Bid = 0; HasSubmittedBid = false; PrivateClueResult = null; } public void SetPrivateClue(PrivateClueResult value) { PrivateClueResult = value; Knowledge.AddRange(value.Knowledge); } public void SetBid(int value) { Bid = value; HasSubmittedBid = true; } public void ChangeAssets(int value) => AvailableAssets += value;
        }
        private sealed class AuctionSettlement { public AuctionSettlement(int winnerSlot, int winningBid) { WinnerSlot = winnerSlot; WinningBid = winningBid; } public int WinnerSlot { get; } public int WinningBid { get; } }
        private enum SeatController { None, Human, Ai }
    }
    public sealed class AuctionSeatView
    {
        public AuctionSeatView(AuctionPhase phase, int assets, AuctionGridView grid, PublicClueView publicClue, IReadOnlyList<PrivateClueChoice> choices, PrivateClueResult result, IReadOnlyList<CollectibleKnowledge> knowledge, AuctionSettlementView settlement) { Phase = phase; AvailableAssets = assets; Grid = grid; PublicClue = publicClue; PrivateClueChoices = choices; PrivateClueResult = result; Knowledge = knowledge; Settlement = settlement; }
        public AuctionPhase Phase { get; } public int AvailableAssets { get; } public AuctionGridView Grid { get; } public PublicClueView PublicClue { get; } public IReadOnlyList<PrivateClueChoice> PrivateClueChoices { get; } public PrivateClueResult PrivateClueResult { get; } public IReadOnlyList<CollectibleKnowledge> Knowledge { get; } public AuctionSettlementView Settlement { get; }
    }
    public sealed class AuctionGridView { public AuctionGridView(int width, int height) { Width = width; Height = height; KnownItems = Array.Empty<CollectibleKnowledge>(); } public int Width { get; } public int Height { get; } public IReadOnlyList<CollectibleKnowledge> KnownItems { get; } }
    public sealed class PublicClueView { public PublicClueView(PrivateClueResult result) { Text = $"公共线索：{result.Text}"; Knowledge = result.Knowledge; } public string Text { get; } public IReadOnlyList<CollectibleKnowledge> Knowledge { get; } }
    public sealed class PrivateClueChoice { public PrivateClueChoice(string id, string label, ClueKind kind, CollectibleRarity? rarity) { Id = id; Label = label; Kind = kind; Rarity = rarity; } public string Id { get; } public string Label { get; } public ClueKind Kind { get; } public CollectibleRarity? Rarity { get; } }
    public sealed class PrivateClueResult { public PrivateClueResult(string clueId, string text, IReadOnlyList<CollectibleKnowledge> knowledge) { ClueId = clueId; Text = text; Knowledge = knowledge; } public string ClueId { get; } public string Text { get; } public IReadOnlyList<CollectibleKnowledge> Knowledge { get; } }
    public sealed class CollectibleKnowledge { public CollectibleKnowledge(int x, int y, int width, int height, CollectibleRarity rarity, string name, string appearanceKey, int? value) { X = x; Y = y; Width = width; Height = height; Rarity = rarity; Name = name; AppearanceKey = appearanceKey; Value = value; } public int X { get; } public int Y { get; } public int Width { get; } public int Height { get; } public CollectibleRarity Rarity { get; } public string Name { get; } public string AppearanceKey { get; } public int? Value { get; } }
    public sealed class AuctionSettlementView { public AuctionSettlementView(int winnerSlot, int winningBid) { WinnerSlot = winnerSlot; WinningBid = winningBid; } public int WinnerSlot { get; } public int WinningBid { get; } }
}
