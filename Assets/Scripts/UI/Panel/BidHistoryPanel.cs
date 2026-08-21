using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame
{
    public sealed class BidHistoryPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text[] playerNameTexts;
        [SerializeField] private RectTransform content;
        [SerializeField] private GameObject cardPrefab;
        [SerializeField] private ClueCatalog clueCatalog;
        [SerializeField] private Sprite fallbackClueImage;
        [SerializeField] private float columnWidth = 160f;

        private readonly List<GameObject> _columns = new List<GameObject>();
        private int _renderedColumns;

        public void SetHistory(IReadOnlyList<RoundReveal> history, IReadOnlyList<ParticipantVisibleState> participants)
        {
            if (content == null || cardPrefab == null)
            {
                return;
            }

            int rowCount = participants == null ? 0 : participants.Count;
            for (int index = 0; index < playerNameTexts.Length; index++)
            {
                if (index >= rowCount)
                {
                    playerNameTexts[index].SetText(string.Empty);
                    continue;
                }

                playerNameTexts[index].SetText(participants[index].AssetOwnerId);
            }

            if (history == null)
            {
                return;
            }

            for (int column = _renderedColumns; column < history.Count; column++)
            {
                CreateColumn(history[column], participants);
            }

            _renderedColumns = history.Count;
        }

        private void CreateColumn(RoundReveal reveal, IReadOnlyList<ParticipantVisibleState> participants)
        {
            GameObject column = new GameObject("Column" + _renderedColumns, typeof(RectTransform), typeof(VerticalLayoutGroup));
            column.transform.SetParent(content, false);
            RectTransform columnRect = column.GetComponent<RectTransform>();
            columnRect.sizeDelta = new Vector2(columnWidth, 0);
            VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            int rowCount = participants == null ? 0 : participants.Count;
            for (int row = 0; row < rowCount; row++)
            {
                string rowAssetOwnerId = participants == null || row >= participants.Count
                    ? null
                    : participants[row].AssetOwnerId;
                RoundParticipantReveal participantReveal = reveal.Participants
                    .FirstOrDefault(item => item.AssetOwnerId == rowAssetOwnerId);

                GameObject card = Instantiate(cardPrefab, column.transform);
                card.name = "Card" + row;

                Image clueImage = FindChild<Image>(card.transform);
                TMP_Text bidText = FindChild<TMP_Text>(card.transform);
                if (clueImage != null)
                {
                    Clue clue = participantReveal != null && participantReveal.ClueId.HasValue && clueCatalog != null
                        ? clueCatalog.Find(participantReveal.ClueId.Value)
                        : null;
                    Sprite sprite = clue != null ? clue.Image : null;
                    clueImage.sprite = sprite != null ? sprite : fallbackClueImage;
                }
                if (bidText != null)
                {
                    bidText.SetText(participantReveal != null ? participantReveal.Bid.ToString("N0") : string.Empty);
                }
            }

            _columns.Add(column);
        }

        private static T FindChild<T>(Transform parent) where T : Component
        {
            return parent.GetComponentsInChildren<T>(true).FirstOrDefault();
        }
    }
}

