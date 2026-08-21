using System.Linq;
using TMPro;
using UnityEngine;

namespace AuctionGame
{
    public sealed class OverlayManager : MonoBehaviour
    {
        [SerializeField] private GameObject matchPage;
        [SerializeField] private GameObject clueSelectionOverlay;
        [SerializeField] private GameObject bidInputOverlay;
        [SerializeField] private GameObject bidRevealOverlay;
        [SerializeField] private GameObject bidHistoryOverlay;
        [SerializeField] private GameObject catalogOverlay;
        [SerializeField] private GameObject settlementPage;
        [SerializeField] private GameObject overlayBlocker;
        [SerializeField] private BidInputPanel bidInputPanel;
        [SerializeField] private ClueSelectionPanel clueSelectionPanel;
        [SerializeField] private RoundRevealUI roundRevealUI;
        [SerializeField] private SettlementPanel settlementPanel;
        [SerializeField] private BidHistoryPanel bidHistoryPanel;
        [SerializeField] private GameObject unknownBanner;

        private AuctionManager _manager;
        private Coroutine _unknownBannerCoroutine;
        private MatchPhase? _lastPhase;

        private void OnEnable()
        {
            GameManager.Instance.CurrentAuctionManagerChanged += OnManagerChanged;
            OnManagerChanged(GameManager.Instance.CurrentAuctionManager);
        }

        private void OnDisable()
        {
            GameManager.Instance.CurrentAuctionManagerChanged -= OnManagerChanged;
            Unsubscribe(_manager);
            _manager = null;
        }

        private void OnManagerChanged(AuctionManager manager)
        {
            if (manager == _manager)
            {
                return;
            }

            Unsubscribe(_manager);
            _manager = manager;
            if (manager != null)
            {
                manager.VisibleRecordChanged += OnRecordChanged;
                manager.RequestStateChanged += OnRequest;
                manager.ConnectionChanged += OnConnection;
            }

            if (manager != null)
            {
                Render(manager.HumanVisibleRecord);
            }
        }

        private void Unsubscribe(AuctionManager manager)
        {
            if (manager == null)
            {
                return;
            }

            manager.VisibleRecordChanged -= OnRecordChanged;
            manager.RequestStateChanged -= OnRequest;
            manager.ConnectionChanged -= OnConnection;
        }

        private void OnRecordChanged(VisibleRecord record) => Render(record);

        private void Render(VisibleRecord record)
        {
            if (record == null)
            {
                CloseAll();
                return;
            }

            DispatchData(record);
            DispatchVisibility(record);
        }

        private void DispatchData(VisibleRecord record)
        {
            if (clueSelectionPanel != null)
            {
                clueSelectionPanel.SetChoices(record.ClueChoices);
            }

            if (bidInputPanel != null)
            {
                int ownLastBid = 0;
                string myAssetOwnerId = GameManager.Instance.HumanAssetOwnerId;
                if (myAssetOwnerId != null && record.BidHistory.Count > 0)
                {
                    RoundReveal last = record.BidHistory[record.BidHistory.Count - 1];
                    RoundParticipantReveal participantReveal = last.Participants
                        .FirstOrDefault(item => item.AssetOwnerId == myAssetOwnerId);
                    if (participantReveal != null)
                    {
                        ownLastBid = participantReveal.Bid;
                    }
                }

                bidInputPanel.SetInputState(record.OwnAssets, ownLastBid, record.Round, record.CanRequestAction);
            }

            if (roundRevealUI != null && record.RoundReveal != null && record.RoundReveal.Round == record.Round)
            {
                roundRevealUI.SetReveal(record.RoundReveal);
            }

            if (bidHistoryPanel != null)
            {
                bidHistoryPanel.SetHistory(record.BidHistory, record.Participants);
            }

            if (settlementPanel != null && record.Settlement != null)
            {
                settlementPanel.SetSettlement(record.Settlement, record.VisibleBoard);
            }
        }

        private void DispatchVisibility(VisibleRecord record)
        {
            if (record.Phase == _lastPhase)
            {
                return;
            }

            _lastPhase = record.Phase;
            switch (record.Phase)
            {
                case MatchPhase.Waiting:
                    CloseAll();
                    if (matchPage != null)
                    {
                        matchPage.SetActive(true);
                    }
                    break;
                case MatchPhase.Analysis:
                    CloseAll();
                    if (matchPage != null)
                    {
                        matchPage.SetActive(true);
                    }
                    Show(clueSelectionOverlay);
                    break;
                case MatchPhase.Bidding:
                    CloseAll();
                    if (matchPage != null)
                    {
                        matchPage.SetActive(true);
                    }
                    if (record.CanRequestAction)
                    {
                        Show(bidInputOverlay);
                    }
                    break;
                case MatchPhase.Revealing:
                    CloseAll();
                    if (matchPage != null)
                    {
                        matchPage.SetActive(true);
                    }
                    Show(bidRevealOverlay);
                    break;
                case MatchPhase.Settlement:
                    CloseAll();
                    if (matchPage != null)
                    {
                        matchPage.SetActive(false);
                    }
                    Show(settlementPage);
                    break;
            }
        }

        public void OnHistoryClicked()
        {
            CloseAll();
            Show(bidHistoryOverlay);
        }

        public void OnCatalogClicked()
        {
            CloseAll();
            Show(catalogOverlay);
        }

        public void OnOpenClueSelection()
        {
            CloseAll();
            Show(clueSelectionOverlay);
        }

        public void OnOpenBidInput()
        {
            CloseAll();
            Show(bidInputOverlay);
        }

        public void OnClosePanel()
        {
            CloseAll();
        }

        private void OnRequest(RequestStateChange state)
        {
            if (clueSelectionPanel != null)
            {
                clueSelectionPanel.SetRequestState(state.State, state.Reason);
            }
            if (bidInputPanel != null)
            {
                bidInputPanel.SetRequestState(state.State, state.Reason);
            }

            if (state.State == PendingRequestState.Unknown)
            {
                ShowUnknownBanner("连接中断，结果未知");
            }
            else if (state.State == PendingRequestState.Pending || state.State == PendingRequestState.Accepted)
            {
                if (bidInputOverlay != null)
                {
                    bidInputOverlay.SetActive(false);
                }
                if (clueSelectionOverlay != null)
                {
                    clueSelectionOverlay.SetActive(false);
                }
            }
        }

        private void OnConnection(string state)
        {
            if (state == "Connected")
            {
                HideUnknownBanner();
            }
            else if (state == "Disconnected" || state == "Reconnecting" ||
                     state == "ReconnectExhausted" || state.StartsWith("Failed"))
            {
                ShowUnknownBanner("连接中断，结果未知");
            }
        }

        private void ShowUnknownBanner(string message)
        {
            if (unknownBanner == null)
            {
                return;
            }

            TMP_Text text = unknownBanner.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.SetText(message);
            }

            unknownBanner.SetActive(true);
            if (_unknownBannerCoroutine != null)
            {
                StopCoroutine(_unknownBannerCoroutine);
            }
            _unknownBannerCoroutine = StartCoroutine(HideUnknownBannerAfterDelay());
        }

        private void HideUnknownBanner()
        {
            if (_unknownBannerCoroutine != null)
            {
                StopCoroutine(_unknownBannerCoroutine);
                _unknownBannerCoroutine = null;
            }

            if (unknownBanner != null)
            {
                unknownBanner.SetActive(false);
            }
        }

        private System.Collections.IEnumerator HideUnknownBannerAfterDelay()
        {
            yield return new WaitForSeconds(2f);
            HideUnknownBanner();
        }

        private void CloseAll()
        {
            if (clueSelectionOverlay != null)
            {
                clueSelectionOverlay.SetActive(false);
            }
            if (bidInputOverlay != null)
            {
                bidInputOverlay.SetActive(false);
            }
            if (bidRevealOverlay != null)
            {
                bidRevealOverlay.SetActive(false);
            }
            if (bidHistoryOverlay != null)
            {
                bidHistoryOverlay.SetActive(false);
            }
            if (catalogOverlay != null)
            {
                catalogOverlay.SetActive(false);
            }
            if (settlementPage != null)
            {
                settlementPage.SetActive(false);
            }
            if (overlayBlocker != null)
            {
                overlayBlocker.SetActive(false);
            }
        }

        private void Show(GameObject overlay)
        {
            if (overlay == null)
            {
                return;
            }

            if (overlayBlocker != null)
            {
                overlayBlocker.SetActive(true);
            }
            overlay.SetActive(true);
        }
    }
}


