using System;

namespace AuctionGame.Gameplay
{
    public sealed class AuctionLocalPlaytest
    {
        private static readonly TimeSpan SettlementDisplayDuration = TimeSpan.FromSeconds(3);

        private readonly AuctionRules _rules;
        private readonly IRandomSource _random;
        private AuctionSession _session;
        private AuctionConnection _localPlayer;
        private AuctionMatch _match;
        private TimeSpan _settlementElapsed;

        public AuctionLocalPlaytest(AuctionRules rules, IRandomSource random)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _random = random ?? new SystemRandomSource();
        }

        public int LocalSeatIndex { get; private set; } = -1;
        public bool IsRunning => _match != null;
        public AuctionSeatView CurrentView => _match == null ? null : _match.GetSeatView(LocalSeatIndex);

        public void Start()
        {
            if (IsRunning) throw new InvalidOperationException("本地试玩已经开始。");
            CreateSession();
        }

        public void Reset()
        {
            CreateSession();
        }

        public void SelectPrivateClue(string clueId)
        {
            EnsureStarted();
            _match.SelectPrivateClue(LocalSeatIndex, clueId);
        }

        public void SubmitBid(int amount)
        {
            EnsureStarted();
            _match.SubmitBid(LocalSeatIndex, amount);
        }

        public void AdvanceTime(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (!IsRunning || elapsed == TimeSpan.Zero) return;

            if (_match.Phase != AuctionPhase.Settlement)
            {
                _match.AdvanceTime(elapsed);
                return;
            }

            _settlementElapsed += elapsed;
            if (_settlementElapsed < SettlementDisplayDuration) return;

            _match = _session.StartNextMatch();
            LocalSeatIndex = _session.SeatOf(_localPlayer);
            _settlementElapsed = TimeSpan.Zero;
        }

        private void CreateSession()
        {
            _session = new AuctionSession(_rules, _random);
            _localPlayer = _session.OpenConnection("local-playtest");
            _match = _session.StartNextMatch();
            LocalSeatIndex = _session.SeatOf(_localPlayer);
            _settlementElapsed = TimeSpan.Zero;
        }

        private void EnsureStarted()
        {
            if (!IsRunning) throw new InvalidOperationException("本地试玩尚未开始。");
        }
    }
}
