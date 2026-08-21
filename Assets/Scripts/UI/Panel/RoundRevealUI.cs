using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame
{
    [Serializable]
    public sealed class RoundRevealRow
    {
        public TMP_Text nameText;
        public Image clueImage;
        public TMP_Text bidText;
        public TMP_Text rankText;
    }

    public sealed class RoundRevealUI : MonoBehaviour
    {
        [SerializeField] private ClueCatalog clueCatalog;
        [SerializeField] private Sprite fallbackClueImage;
        [SerializeField] private RoundRevealRow[] rows;

        public void SetReveal(RoundReveal reveal)
        {
            if (reveal == null)
            {
                return;
            }

            for (int index = 0; index < rows.Length; index++)
            {
                RoundRevealRow row = rows[index];
                if (row == null)
                {
                    continue;
                }

                GameObject rowObject = row.nameText != null
                    ? row.nameText.transform.parent.gameObject
                    : null;
                if (index >= reveal.Participants.Count)
                {
                    if (rowObject != null)
                    {
                        rowObject.SetActive(false);
                    }
                    if (row.nameText != null)
                    {
                        row.nameText.SetText(string.Empty);
                    }
                    if (row.bidText != null)
                    {
                        row.bidText.SetText(string.Empty);
                    }
                    if (row.rankText != null)
                    {
                        row.rankText.SetText(string.Empty);
                    }
                    if (row.clueImage != null)
                    {
                        row.clueImage.sprite = null;
                    }
                    continue;
                }

                if (rowObject != null)
                {
                    rowObject.SetActive(true);
                }

                RoundParticipantReveal participantReveal = reveal.Participants[index];
                bool isWinner = !string.IsNullOrEmpty(reveal.WinnerAssetOwnerId) &&
                    participantReveal.AssetOwnerId == reveal.WinnerAssetOwnerId;
                if (row.nameText != null)
                {
                    row.nameText.SetText($"{participantReveal.AssetOwnerId}{(isWinner ? " 中标" : string.Empty)}");
                }
                if (row.bidText != null)
                {
                    row.bidText.SetText(participantReveal.Bid.ToString("N0"));
                }
                if (row.rankText != null)
                {
                    row.rankText.SetText((index + 1).ToString());
                }

                Clue clue = participantReveal.ClueId.HasValue && clueCatalog != null
                    ? clueCatalog.Find(participantReveal.ClueId.Value)
                    : null;
                Sprite sprite = clue != null ? clue.Image : null;
                if (row.clueImage != null)
                {
                    row.clueImage.sprite = sprite != null ? sprite : fallbackClueImage;
                }
            }
        }
    }
}


