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
    }
}
