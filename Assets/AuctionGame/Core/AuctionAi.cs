using System;
using System.Linq;

namespace AuctionGame
{
    public sealed class SimpleRuleAi
    {
        public string ChoosePrivateClue(AuctionSeatView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            return view.PrivateClueChoices.FirstOrDefault()?.Id;
        }

        public int ChooseBid(AuctionSeatView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            return 0;
        }
    }

    public sealed class AiActionScheduler
    {
        private readonly IRandomSource _random;

        public AiActionScheduler(IRandomSource random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public TimeSpan Schedule(TimeSpan maximumRandomDelay, bool allHumansCompleted)
        {
            if (maximumRandomDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumRandomDelay));
            var milliseconds = maximumRandomDelay.TotalMilliseconds <= 0 ? 0 : _random.Next((int)maximumRandomDelay.TotalMilliseconds + 1);
            var delay = TimeSpan.FromMilliseconds(milliseconds);
            return allHumansCompleted && delay > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
        }
    }
}
