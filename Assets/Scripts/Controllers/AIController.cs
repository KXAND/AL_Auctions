using System;
using System.Linq;

namespace AuctionGame
{
    public sealed class AIController : IAuctionController
    {
        private readonly AuctionManager _auctionManager;
        private readonly Random _random;
        private TimeSpan _remainingThinkTime;
        private bool _scheduled;

        public AIController(AuctionManager auctionManager, Random random)
        {
            _auctionManager = auctionManager != null ? auctionManager : throw new ArgumentNullException(nameof(auctionManager));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public VisibleRecord VisibleRecord { get; private set; }

        public int TemporaryAssets => VisibleRecord?.OwnAssets ?? 0;

        public void RequestBid(int amount)
        {
            _auctionManager.RequestAction(this, AuctionActionType.Bid, amount);
        }

        public void RequestClue(int clueId)
        {
            _auctionManager.RequestAction(this, AuctionActionType.Clue, clueId);
        }

        public void OnTakeOver()
        {
            ScheduleThink();
        }

        internal void ReceiveVisibleRecord(VisibleRecord record)
        {
            MatchPhase? previousPhase = VisibleRecord?.Phase;
            bool phaseEntered = !previousPhase.HasValue || previousPhase.Value != record.Phase;
            VisibleRecord = record;
            if (_scheduled && AllHumansCompleted(record))
            {
                TimeSpan cap = TimeSpan.FromSeconds(1);
                if (_remainingThinkTime > cap)
                {
                    _remainingThinkTime = cap;
                }
            }
            if (phaseEntered && (record.Phase == MatchPhase.Analysis || record.Phase == MatchPhase.Bidding))
            {
                ScheduleThink();
            }
        }

        internal void ReceiveAuthorityResult(AuthorityResult result)
        {
            if (!result.Accepted)
            {
                ScheduleThink();
            }
        }

        internal void Update(TimeSpan deltaTime)
        {
            if (!_scheduled || VisibleRecord == null)
            {
                return;
            }
            _remainingThinkTime -= deltaTime;
            if (_remainingThinkTime > TimeSpan.Zero)
            {
                return;
            }
            _scheduled = false;
            Think();
        }

        private void Think()
        {
            VisibleRecord record = VisibleRecord;
            if (record == null || !record.CanRequestAction)
            {
                return;
            }
            if (record.Phase == MatchPhase.Analysis)
            {
                int clue = record.ClueChoices.FirstOrDefault();
                if (clue != 0)
                {
                    RequestClue(clue);
                }
            }
            else if (record.Phase == MatchPhase.Bidding)
            {
                int bid = record.OwnAssets == 0 ? 0 : _random.Next(record.OwnAssets + 1);
                RequestBid(bid);
            }
        }

        private void ScheduleThink()
        {
            int maximumMilliseconds = (int)Math.Min(int.MaxValue, GlobalSettings.AiMaximumActionDelay.TotalMilliseconds);
            int milliseconds = maximumMilliseconds <= 0 ? 0 : _random.Next(maximumMilliseconds + 1);
            _remainingThinkTime = TimeSpan.FromMilliseconds(milliseconds);
            _scheduled = true;
        }

        private static bool AllHumansCompleted(VisibleRecord record)
        {
            return record.Participants
                .Where(participant => !participant.IsAI)
                .All(participant => participant.HasCompletedAction);
        }
    }
}
