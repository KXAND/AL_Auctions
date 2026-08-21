using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace AuctionGame
{
    public sealed class SettlementPanel : MonoBehaviour
    {
        [SerializeField] private BoardManager board;
        [SerializeField] private TMP_Text winnerText;
        [SerializeField] private TMP_Text packageValueText;
        [SerializeField] private TMP_Text winningBidText;
        [SerializeField] private TMP_Text profitText;
        [SerializeField] private TMP_Text shareText;
        [SerializeField] private float revealInterval = 0.3f;
        [SerializeField] private Color positiveColor = new Color(0.2f, 0.9f, 0.3f, 1f);
        [SerializeField] private Color negativeColor = new Color(0.95f, 0.25f, 0.25f, 1f);

        private List<VisibleItem> _items = new List<VisibleItem>();
        private int _currentIndex;
        private int _accumulatedValue;
        private int _winningBid;
        private bool _validWinner;
        private bool _finished;
        private float _timer;
        private string _signature;

        public void SetSettlement(SettlementView settlement, VisibleBoard packageBoard)
        {
            if (settlement == null || packageBoard == null)
            {
                return;
            }

            string signature = $"{settlement.SettlementId}|{settlement.WinnerAssetOwnerId}|{settlement.WinningBid}|{packageBoard.Items.Count}";
            if (_signature == signature)
            {
                return;
            }
            _signature = signature;

            _items = packageBoard.Items
                .OrderByDescending(item => item.Y)
                .ThenBy(item => item.X)
                .ToList();
            _currentIndex = 0;
            _accumulatedValue = 0;
            _winningBid = settlement.WinningBid;
            _validWinner = !string.IsNullOrEmpty(settlement.WinnerAssetOwnerId);
            _finished = _items.Count == 0;
            _timer = 0f;

            if (board != null)
            {
                board.InitializeBoard(packageBoard.Width, packageBoard.Height);
            }

            if (winnerText != null)
            {
                winnerText.SetText(_validWinner ? settlement.WinnerAssetOwnerId : "--");
            }
            if (winningBidText != null)
            {
                winningBidText.SetText(_validWinner ? settlement.WinningBid.ToString() : "--");
            }
            if (packageValueText != null)
            {
                packageValueText.SetText("0");
            }
            SetColoredText(profitText, 0);
            if (shareText != null)
            {
                shareText.SetText(string.Empty);
            }

            if (_finished)
            {
                FinishReveal();
            }
        }

        private void Update()
        {
            if (_finished)
            {
                return;
            }

            _timer += Time.deltaTime;
            if (_timer < revealInterval)
            {
                return;
            }

            _timer = 0f;
            RevealNext();
        }

        private void RevealNext()
        {
            VisibleItem item = _items[_currentIndex];
            _currentIndex++;

            if (board != null)
            {
                board.RevealItem(new ItemUIDataInfo
                {
                    itemId = item.ItemId,
                    rarity = item.Rarity,
                    topLeft = new Vector2Int(item.X, item.Y),
                    BottomRight = new Vector2Int(item.X + item.Width - 1, item.Y + item.Height - 1),
                    revealLevel = RevealLevel.RevealDetailed
                });
            }

            _accumulatedValue += item.Value ?? 0;
            if (packageValueText != null)
            {
                packageValueText.SetText(_accumulatedValue.ToString());
            }

            int profit = _validWinner ? _accumulatedValue - _winningBid : 0;
            SetColoredText(profitText, profit);

            if (_currentIndex >= _items.Count)
            {
                FinishReveal();
            }
        }

        private void FinishReveal()
        {
            _finished = true;

            int profit = _validWinner ? _accumulatedValue - _winningBid : 0;
            int share = 0;
            if (profit < 0)
            {
                share = (int)Math.Floor(
                GlobalSettings.LossDistributionRatio * (-profit) / (GlobalSettings.PlayerCount - 1));
            }

            SetColoredText(shareText, share);
        }

        private void SetColoredText(TMP_Text text, int value)
        {
            if (text == null)
            {
                return;
            }

            text.SetText(value.ToString());
            text.color = value > 0 ? positiveColor : negativeColor;
        }
    }
}

