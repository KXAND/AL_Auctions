using System;
using UnityEngine;

namespace AuctionGame.Fusion
{
    public sealed class AuctionLocalSession : MonoBehaviour, IAuctionPresentationSession
    {
        [SerializeField] private int initialAssets = 100;
        [SerializeField] private int playerCount = 4;

        private AuctionLocalPlaytest _playtest;

        public AuctionWireView CurrentView { get; private set; }
        public string Status { get; private set; } = "尚未开始本地试玩";
        public bool IsRunning => _playtest != null && _playtest.IsRunning;

        private void Update()
        {
            if (!IsRunning)
            {
                return;
            }

            _playtest.AdvanceTime(TimeSpan.FromSeconds(Time.deltaTime));
            SyncView();
        }

        public void StartLocalPlaytest()
        {
            _playtest = new AuctionLocalPlaytest(
                AuctionRules.CreateDemo(playerCount, initialAssets),
                new SystemRandomSource());
            _playtest.Start();
            Status = "本地试玩进行中（未连接网络）";
            SyncView();
        }

        public void ResetLocalPlaytest()
        {
            if (!IsRunning)
            {
                StartLocalPlaytest();
                return;
            }

            _playtest.Reset();
            Status = "本地试玩已重置";
            SyncView();
        }

        public void SelectPrivateClue(string clueId)
        {
            ExecuteAction(() => _playtest.SelectPrivateClue(clueId));
        }

        public void SubmitBid(int amount)
        {
            ExecuteAction(() => _playtest.SubmitBid(amount));
        }

        private void ExecuteAction(Action action)
        {
            if (!IsRunning)
            {
                return;
            }

            try
            {
                action();
                SyncView();
            }
            catch (Exception exception)
            {
                Status = $"本地操作被拒绝：{exception.Message}";
                Debug.LogWarning(Status);
            }
        }

        private void SyncView()
        {
            CurrentView = AuctionWireView.From(_playtest.LocalSeatIndex, _playtest.CurrentView);
        }
    }
}
