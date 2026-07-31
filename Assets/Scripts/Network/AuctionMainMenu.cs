using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AuctionGame.Network
{
    public sealed class AuctionMainMenu : MonoBehaviour
    {
        private const string AuctionDemoSceneName = "AuctionDemo";

        [SerializeField] private TextMeshProUGUI offlineButtonLabel;

        private void Start()
        {
            offlineButtonLabel.text = "Play Offline";
        }

        public void PlayOnline()
        {
            AuctionDemoLaunch.RequestOnline();
            SceneManager.LoadScene(AuctionDemoSceneName, LoadSceneMode.Single);
        }

        public void PlayOffline()
        {
            AuctionDemoLaunch.RequestOffline();
            SceneManager.LoadScene(AuctionDemoSceneName, LoadSceneMode.Single);
        }
    }
}
