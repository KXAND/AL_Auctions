using System;
using System.Collections.Generic;
using UnityEngine;


namespace AuctionGame
{
    public enum RevealLevel
    {
        RevealPos,
        RevealSize,
        RevealSizeAndRarity,
        RevealPosAndRarity,
        RevealDetailed,
    }
    public enum ItemRarity
    {
        UNKNOWN, N, R, SR, SSR, UR
    }

    public static class GlobalSettings
    {
        public const int PlayerCount = 4;
        public const int InitialAssets = 100;
        public const int GridWidth = 10;
        public const int PackageItemCount = 2;
        public const int CandidateClueCount = 4;
        public const int PackageTotalValueMinimum = 90;
        public const int PackageTotalValueMaximum = 110;
        public const int PackageSsrValueMinimum = 90;
        public const int PackageSsrValueMaximum = 110;
        public const int PackageGenerationAttemptLimit = 128;

        public static readonly TimeSpan AnalysisDuration = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan BiddingDuration = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan RoundRevealDuration = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan AiMaximumActionDelay = TimeSpan.FromSeconds(5);

        public static readonly IReadOnlyList<decimal> WinningMultipliers =
            Array.AsReadOnly(new[] { 2m, 1.6m, 1.3m, 1.1m });
        public const decimal FinalWinningMultiplier = 1m;
        public const int ConsecutivePassLimit = 3;
        public const decimal LossDistributionRatio = 1m;

        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
        public const int ReconnectAttempts = 3;
        public static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(1);

        public readonly struct BoardRarityColor
        {
            public static readonly Color SSR = new Color(1f, 0.82f, 0.32f, 1f);
            public static readonly Color SR = new Color(0.78f, 0.58f, 1f, 1f);
            public static readonly Color R = new Color(0.46f, 0.78f, 1f, 1f);
            public static readonly Color N = Color.white;
        }
    }
}
