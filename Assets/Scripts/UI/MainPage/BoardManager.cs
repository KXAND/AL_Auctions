using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace AuctionGame
{
    public class ItemUIDataInfo
    {
        public string itemId;
        public ItemRarity rarity = ItemRarity.UNKNOWN;
        public Vector2Int topLeft;
        public Vector2Int BottomRight;
        public RevealLevel revealLevel = RevealLevel.RevealPos;
    }

    public sealed class BoardManager : MonoBehaviour
    {
        [SerializeField] private BoardGraphics board;
        [SerializeField] private TMP_Text prediction;
        [SerializeField] private bool usePredictValue = true;
        [SerializeField] private bool listenToAuctionManager = true;
        [SerializeField] private ItemCatalog itemCatalog;

        private AuctionManager auctionManager;
        private int minPredictValue;
        private int maxPredictValue;

        // TODO: 棋盘点击联动图鉴——点击已识别/部分识别的藏品，将已知信息作为筛选条件打开 CatalogOverlay；暂不实现。
        public event Action<VisibleItem> CatalogRequested;

        private void Awake()
        {
            if (board == null)
            {
                board = GetComponentInChildren<BoardGraphics>(true);
            }

            if (prediction == null)
            {
                Transform predictionRoot = transform.Find("Prediction");
                if (predictionRoot != null)
                {
                    prediction = predictionRoot.GetComponentInChildren<TMP_Text>(true);
                }
            }
        }

        private void OnEnable()
        {
            if (!listenToAuctionManager)
            {
                return;
            }

            GameManager.Instance.CurrentAuctionManagerChanged += OnAuctionManagerChanged;
            OnAuctionManagerChanged(GameManager.Instance.CurrentAuctionManager);
        }

        private void OnDisable()
        {
            if (!listenToAuctionManager)
            {
                return;
            }

            GameManager.Instance.CurrentAuctionManagerChanged -= OnAuctionManagerChanged;

            if (auctionManager != null)
            {
                auctionManager.VisibleRecordChanged -= OnRecordChanged;
            }

            auctionManager = null;
        }

        private void OnAuctionManagerChanged(AuctionManager currentManager)
        {
            if (currentManager == auctionManager)
            {
                return;
            }

            if (auctionManager != null)
            {
                auctionManager.VisibleRecordChanged -= OnRecordChanged;
            }

            auctionManager = currentManager;

            if (auctionManager != null)
            {
                auctionManager.VisibleRecordChanged += OnRecordChanged;
            }

            Render(auctionManager != null
                ? auctionManager.HumanVisibleRecord?.VisibleBoard
                : null);
        }

        private void OnRecordChanged(VisibleRecord record)
        {
            Render(record.VisibleBoard);
        }

        private void Render(VisibleBoard visibleBoard)
        {
            if (board != null)
            {
                board.clearItems();
            }

            minPredictValue = 0;
            maxPredictValue = 0;

            if (visibleBoard == null || board == null)
            {
                UpdatePrediction();
                return;
            }

            if (visibleBoard.Width > 0 && visibleBoard.Height > 0)
            {
                board.SetGrid(visibleBoard.Width, visibleBoard.Height, board.LineThickness, board.color);
            }

            foreach (VisibleItem item in visibleBoard.Items)
            {
                ItemUIDataInfo itemUIDataInfo = new ItemUIDataInfo
                {
                    itemId = item.ItemId,
                    rarity = item.Rarity,
                    topLeft = new Vector2Int(item.X, item.Y),
                    BottomRight = new Vector2Int(item.X + item.Width - 1, item.Y + item.Height - 1),
                    revealLevel = item.HasIdentity
                        ? RevealLevel.RevealDetailed
                        : item.Rarity == ItemRarity.UNKNOWN
                            ? RevealLevel.RevealSize
                            : RevealLevel.RevealSizeAndRarity
                };

                RevealItem(itemUIDataInfo);
            }

            UpdatePrediction();
        }

        public void InitializeBoard(int width, int height)
        {
            if (board == null)
            {
                return;
            }

            board.clearItems();
            if (width > 0 && height > 0)
            {
                board.SetGrid(width, height, board.LineThickness, board.color);
            }
        }

        public void RevealItem(ItemUIDataInfo itemUIDataInfo)
        {
            if (itemUIDataInfo == null || board == null)
            {
                return;
            }

            if (itemUIDataInfo.revealLevel == RevealLevel.RevealDetailed &&
                !string.IsNullOrEmpty(itemUIDataInfo.itemId))
            {
                ItemData item = itemCatalog.FindById(itemUIDataInfo.itemId);
                Sprite itemImage = item == null ? null : item.FullSprite;
                Vector2Int itemSize = item == null
                    ? Vector2Int.zero
                    : new Vector2Int(item.Size.x, item.Size.y);

                board.paintItem(
                    itemImage,
                    itemUIDataInfo.rarity,
                    itemUIDataInfo.topLeft,
                    itemUIDataInfo.BottomRight,
                    itemSize);
            }
            else if (itemUIDataInfo.revealLevel == RevealLevel.RevealPos ||
                     itemUIDataInfo.revealLevel == RevealLevel.RevealPosAndRarity)
            {
                board.revealOneGrid(itemUIDataInfo.topLeft, itemUIDataInfo.rarity);
            }
            else if (itemUIDataInfo.revealLevel == RevealLevel.RevealSize ||
                     itemUIDataInfo.revealLevel == RevealLevel.RevealSizeAndRarity)
            {
                board.revealSize(
                    itemUIDataInfo.topLeft,
                    itemUIDataInfo.BottomRight,
                    itemUIDataInfo.rarity);
            }

            if (usePredictValue)
            {
                AddPredictValue(itemUIDataInfo);
            }
        }

        private void AddPredictValue(ItemUIDataInfo itemUIDataInfo)
        {
            IReadOnlyList<ItemData> candidates = QueryCatalog(itemUIDataInfo);
            if (candidates.Count == 0)
            {
                return;
            }

            minPredictValue += candidates.Min(item => item.BaseValue);
            maxPredictValue += candidates.Max(item => item.BaseValue);
        }

        private IReadOnlyList<ItemData> QueryCatalog(ItemUIDataInfo itemUIDataInfo)
        {
            if (itemUIDataInfo.revealLevel == RevealLevel.RevealDetailed)
            {
                ItemData item = itemCatalog.FindById(itemUIDataInfo.itemId);
                return item == null
                    ? Array.Empty<ItemData>()
                    : new[] { item };
            }

            bool knowsSize = itemUIDataInfo.revealLevel == RevealLevel.RevealSize ||
                             itemUIDataInfo.revealLevel == RevealLevel.RevealSizeAndRarity;
            bool knowsRarity = itemUIDataInfo.revealLevel == RevealLevel.RevealPosAndRarity ||
                               itemUIDataInfo.revealLevel == RevealLevel.RevealSizeAndRarity;
            int width = itemUIDataInfo.BottomRight.x - itemUIDataInfo.topLeft.x + 1;
            int height = itemUIDataInfo.BottomRight.y - itemUIDataInfo.topLeft.y + 1;

            return itemCatalog.GetAllItems().Where(item =>
                (!knowsSize ||
                 item.Size.x == width && item.Size.y == height ||
                 item.Size.x == height && item.Size.y == width) &&
                (!knowsRarity || item.Rarity == itemUIDataInfo.rarity)).ToArray();
        }

        private void UpdatePrediction()
        {
            if (prediction == null)
            {
                return;
            }

            prediction.text = usePredictValue
                ? $"仓库显示区价值预估：{minPredictValue}-{maxPredictValue}"
                : string.Empty;
        }

    }
}



