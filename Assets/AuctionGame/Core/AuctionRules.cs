using System;

namespace AuctionGame
{
    public sealed class AuctionRules
    {
        public int PlayerCount { get; }
        public int InitialAssets { get; }
        public int GridWidth { get; }
        public TimeSpan AnalysisDuration { get; }
        public TimeSpan BiddingDuration { get; }

        private AuctionRules(
            int playerCount,
            int initialAssets,
            int gridWidth,
            TimeSpan analysisDuration,
            TimeSpan biddingDuration)
        {
            if (playerCount < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(playerCount));
            }

            if (initialAssets < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialAssets));
            }

            PlayerCount = playerCount;
            InitialAssets = initialAssets;
            GridWidth = gridWidth;
            AnalysisDuration = analysisDuration;
            BiddingDuration = biddingDuration;
        }

        public static AuctionRules CreateDemo(int playerCount, int initialAssets)
        {
            return new AuctionRules(
                playerCount,
                initialAssets,
                gridWidth: 5,
                analysisDuration: TimeSpan.FromSeconds(20),
                biddingDuration: TimeSpan.FromSeconds(20));
        }
    }
}
