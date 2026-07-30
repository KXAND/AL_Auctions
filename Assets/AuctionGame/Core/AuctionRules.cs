using System;
using System.Collections.Generic;

namespace AuctionGame
{
    public sealed class AuctionRules
    {
        public int PlayerCount { get; }
        public int InitialAssets { get; }
        public int GridWidth { get; }
        public int PackageItemCount { get; }
        public int CandidateClueCount { get; }
        public TimeSpan AnalysisDuration { get; }
        public TimeSpan BiddingDuration { get; }
        public IReadOnlyList<CollectibleDefinition> Catalogue { get; }
        public PackageConstraints PackageConstraints { get; }
        public IReadOnlyList<decimal> WinningMultipliers { get; private set; }
        public decimal FinalWinningMultiplier { get; private set; }
        public int ConsecutivePassLimit { get; private set; }
        public decimal LossDistributionRatio { get; private set; }

        private AuctionRules(int playerCount, int initialAssets, int gridWidth, int packageItemCount, IReadOnlyList<CollectibleDefinition> catalogue, PackageConstraints packageConstraints, int candidateClueCount)
        {
            if (playerCount < 2 || initialAssets < 0 || gridWidth < 1 || packageItemCount < 1 || catalogue == null || catalogue.Count == 0 || candidateClueCount < 1)
            {
                throw new ArgumentOutOfRangeException();
            }

            PlayerCount = playerCount;
            InitialAssets = initialAssets;
            GridWidth = gridWidth;
            PackageItemCount = packageItemCount;
            Catalogue = catalogue;
            PackageConstraints = packageConstraints ?? throw new ArgumentNullException(nameof(packageConstraints));
            CandidateClueCount = candidateClueCount;
            AnalysisDuration = TimeSpan.FromSeconds(20);
            BiddingDuration = TimeSpan.FromSeconds(20);
            WinningMultipliers = new[] { 2m, 1.6m, 1.3m, 1.1m };
            FinalWinningMultiplier = 1m;
            ConsecutivePassLimit = 3;
            LossDistributionRatio = 1m;
        }

        public static AuctionRules CreateDemo(int playerCount, int initialAssets)
        {
            return CreateWithContent(playerCount, initialAssets, 5, 1,
                new[] { new CollectibleDefinition("demo-relic", "鎏金古印", "demo-relic", 2, 1, CollectibleRarity.SSR, 100) },
                new PackageConstraints(new IntRange(100, 100), new Dictionary<CollectibleRarity, IntRange> { [CollectibleRarity.SSR] = new IntRange(100, 100) }),
                2);
        }

        public static AuctionRules CreateWithContent(int playerCount, int initialAssets, int gridWidth, int packageItemCount, IReadOnlyList<CollectibleDefinition> catalogue, PackageConstraints packageConstraints, int candidateClueCount)
        {
            return new AuctionRules(playerCount, initialAssets, gridWidth, packageItemCount, catalogue, packageConstraints, candidateClueCount);
        }

        public AuctionRules WithAuctionLifecycle(IReadOnlyList<decimal> winningMultipliers, decimal finalWinningMultiplier, int consecutivePassLimit, decimal lossDistributionRatio)
        {
            if (winningMultipliers == null || winningMultipliers.Count == 0 || finalWinningMultiplier <= 0 || consecutivePassLimit < 1 || lossDistributionRatio < 0) throw new ArgumentOutOfRangeException();
            WinningMultipliers = winningMultipliers;
            FinalWinningMultiplier = finalWinningMultiplier;
            ConsecutivePassLimit = consecutivePassLimit;
            LossDistributionRatio = lossDistributionRatio;
            return this;
        }
    }
}
