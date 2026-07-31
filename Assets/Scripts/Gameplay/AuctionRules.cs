using System;
using System.Collections.Generic;

namespace AuctionGame.Gameplay
{
    public sealed class AuctionRules
    {
        public int PlayerCount { get; }
        public int InitialAssets { get; }
        public int GridWidth { get; }
        public int PackageItemCount { get; }
        public int CandidateClueCount { get; }
        public TimeSpan AnalysisDuration { get; private set; }
        public TimeSpan BiddingDuration { get; private set; }
        public TimeSpan RoundRevealDuration { get; private set; }
        public IReadOnlyList<CollectibleDefinition> Catalogue { get; }
        public PackageConstraints PackageConstraints { get; }
        public IReadOnlyList<decimal> WinningMultipliers { get; private set; }
        public decimal FinalWinningMultiplier { get; private set; }
        public int ConsecutivePassLimit { get; private set; }
        public decimal LossDistributionRatio { get; private set; }
        public TimeSpan AiMaximumActionDelay { get; private set; }

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
            RoundRevealDuration = TimeSpan.FromSeconds(3);
            WinningMultipliers = new[] { 2m, 1.6m, 1.3m, 1.1m };
            FinalWinningMultiplier = 1m;
            ConsecutivePassLimit = 3;
            LossDistributionRatio = 1m;
            AiMaximumActionDelay = TimeSpan.FromSeconds(5);
        }

        public static AuctionRules CreateDemo(int playerCount, int initialAssets)
        {
            return CreateWithContent(playerCount, initialAssets, 5, 2,
                new[]
                {
                    new CollectibleDefinition("demo-scroll", "金线古卷", "demo-scroll", 1, 1, CollectibleRarity.SSR, 50),
                    new CollectibleDefinition("demo-seal", "鎏金古印", "demo-seal", 2, 1, CollectibleRarity.SSR, 50),
                    new CollectibleDefinition("demo-jade", "龙纹玉璧", "demo-jade", 1, 2, CollectibleRarity.SSR, 50)
                },
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

        public AuctionRules WithAiMaximumActionDelay(TimeSpan maximumActionDelay)
        {
            if (maximumActionDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumActionDelay));
            AiMaximumActionDelay = maximumActionDelay;
            return this;
        }

        public AuctionRules WithPhaseDurations(TimeSpan analysisDuration, TimeSpan biddingDuration, TimeSpan roundRevealDuration)
        {
            if (analysisDuration <= TimeSpan.Zero || biddingDuration <= TimeSpan.Zero || roundRevealDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException();
            AnalysisDuration = analysisDuration;
            BiddingDuration = biddingDuration;
            RoundRevealDuration = roundRevealDuration;
            return this;
        }
    }
}
