using System;
using System.Linq;
using UnityEngine;

namespace AuctionGame.Network
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

            DisableClientUiForDedicatedServer();

            if (UnityEngine.Object.FindFirstObjectByType<AuctionFusionSession>() != null)
            {
                return;
            }

            var server = new GameObject("Auction Dedicated Server");
            UnityEngine.Object.DontDestroyOnLoad(server);
            server.AddComponent<AuctionFusionSession>();
            if (!Application.isBatchMode)
            {
                server.AddComponent<AuctionDedicatedServerGui>();
            }
        }

        private static void DisableClientUiForDedicatedServer()
        {
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                canvas.gameObject.SetActive(false);
            }
        }
    }

    public sealed class AuctionDedicatedServerGui : MonoBehaviour
    {
        private AuctionFusionSession _session;

        private void Awake()
        {
            _session = GetComponent<AuctionFusionSession>();
        }

        private void OnGUI()
        {
            if (_session == null || Application.isBatchMode)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(24, 24, 520, 120), GUI.skin.box);
            GUILayout.Label("竞拍专用服务端（调试）");
            GUILayout.Label(_session.Status);
            GUILayout.Label("详细日志：server.log");
            GUILayout.EndArea();
        }
    }
}
