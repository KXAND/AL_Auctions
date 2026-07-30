using System;
using System.Linq;
using UnityEngine;

namespace AuctionGame.Fusion
{
    public static class AuctionDedicatedServerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDedicatedServerForCommandLineLaunch()
        {
            if (!Application.isBatchMode && !Environment.GetCommandLineArgs().Any(argument => argument == "-auctionServer"))
            {
                return;
            }

            if (UnityEngine.Object.FindFirstObjectByType<AuctionFusionSession>() != null)
            {
                return;
            }

            var server = new GameObject("Auction Dedicated Server");
            UnityEngine.Object.DontDestroyOnLoad(server);
            server.AddComponent<AuctionFusionSession>();
        }
    }
}
