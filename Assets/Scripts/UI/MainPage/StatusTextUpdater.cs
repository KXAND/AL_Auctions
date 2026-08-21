using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace AuctionGame
{
    public sealed class StatusTextUpdater : MonoBehaviour
    {
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private TMP_Text roundText;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_Text assetsText;
        [SerializeField] private TMP_Text publicClueText;
        [SerializeField] private TMP_Text privateClueText;
        [SerializeField] private TMP_Text rosterText;
        [SerializeField] private TMP_Text clueLogText;
        [SerializeField] private ClueCatalog clueCatalog;

        private AuctionManager _manager;
        private string _lastPhaseText;
        private string _lastRoundText;
        private string _lastCountdownText;
        private string _lastAssetsText;
        private string _lastPublicClueText;
        private string _lastPrivateClueText;
        private string _lastRosterText;
        private string _lastClueLogText;

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
            }

            Render(manager != null ? manager.HumanVisibleRecord : null);
        }

        private void Unsubscribe(AuctionManager manager)
        {
            if (manager != null)
            {
                manager.VisibleRecordChanged -= OnRecordChanged;
            }
        }

        private void OnRecordChanged(VisibleRecord record)
        {
            Render(record);
        }

        private void Render(VisibleRecord record)
        {
            if (record == null)
            {
                SetText(phaseText, ref _lastPhaseText, string.Empty);
                SetText(roundText, ref _lastRoundText, string.Empty);
                SetText(countdownText, ref _lastCountdownText, string.Empty);
                SetText(assetsText, ref _lastAssetsText, string.Empty);
                SetText(publicClueText, ref _lastPublicClueText, string.Empty);
                SetText(privateClueText, ref _lastPrivateClueText, string.Empty);
                SetText(rosterText, ref _lastRosterText, string.Empty);
                SetText(clueLogText, ref _lastClueLogText, string.Empty);

                return;
            }

            SetText(phaseText, ref _lastPhaseText, PhaseLabel(record.Phase));
            SetText(roundText, ref _lastRoundText, record.Round > 0 ? $"第 {record.Round} 轮" : string.Empty);
            SetText(countdownText, ref _lastCountdownText, FormatCountdown(record.RemainingTime));
            SetText(assetsText, ref _lastAssetsText, $"资产 {record.OwnAssets}");
            SetText(publicClueText, ref _lastPublicClueText, LatestClueText(record.PublicClueHistory));
            SetText(privateClueText, ref _lastPrivateClueText, LatestClueText(record.PrivateClueHistory));
            SetText(rosterText, ref _lastRosterText, FormatRoster(record.Participants));
            SetText(clueLogText, ref _lastClueLogText, FormatClueLog(record));
        }

        private string FormatClueLog(VisibleRecord record)
        {
            List<(int round, string source, string text)> entries = new List<(int round, string source, string text)>();
            foreach (ClueRecord clue in record.PublicClueHistory)
            {
                entries.Add((clue.Round, "公共", ClueDisplayName(clue.ClueId)));
            }
            foreach (ClueRecord clue in record.PrivateClueHistory)
            {
                entries.Add((clue.Round, "私有", ClueDisplayName(clue.ClueId)));
            }

            IOrderedEnumerable<(int round, string source, string text)> ordered = entries
                .OrderByDescending(entry => entry.round)
                .ThenBy(entry => entry.source == "公共" ? 0 : 1);
            return string.Join("\n", ordered.Select(entry => $"第{entry.round}轮 {entry.source}: {entry.text}"));
        }

        private static string FormatRoster(IReadOnlyList<ParticipantVisibleState> participants)
        {
            if (participants == null || participants.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("\n", participants.Select(participant =>
                $"{participant.AssetOwnerId}  " +
                (participant.HasCompletedAction ? "完成" : "等待")));
        }

        private static string PhaseLabel(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Waiting:
                    return "等待玩家";
                case MatchPhase.Analysis:
                    return "分析阶段";
                case MatchPhase.Bidding:
                    return "出价阶段";
                case MatchPhase.Revealing:
                    return "揭示阶段";
                case MatchPhase.Settlement:
                    return "结算";
                default:
                    return phase.ToString();
            }
        }

        private static string FormatCountdown(System.TimeSpan remaining)
        {
            if (remaining <= System.TimeSpan.Zero)
            {
                return string.Empty;
            }
            return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }

        private string LatestClueText(IReadOnlyList<ClueRecord> history)
        {
            if (history == null || history.Count == 0)
            {
                return string.Empty;
            }
            return ClueDisplayName(history[history.Count - 1].ClueId);
        }

        private string ClueDisplayName(int clueId)
        {
            Clue clue = clueCatalog == null ? null : clueCatalog.Find(clueId);
            return clue == null ? string.Empty : clue.DisplayName;
        }

        private static void SetText(TMP_Text text, ref string cached, string value)
        {
            if (text == null || cached == value)
            {
                return;
            }
            cached = value;
            text.SetText(value);
        }
    }
}

