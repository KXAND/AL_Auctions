using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame
{
    public sealed class Authority
    {
        private readonly ItemCatalog _itemCatalog;
        private readonly Random _random;
        private readonly Dictionary<string, AuthorityResult> _processedRequests = new Dictionary<string, AuthorityResult>();
        private readonly Dictionary<string, long> _stateVersions = new Dictionary<string, long>();
        private readonly List<ClueRecord> _publicClueHistory = new List<ClueRecord>();
        private readonly Dictionary<int, VisibleItem> _publicKnowledge = new Dictionary<int, VisibleItem>();
        private readonly List<RoundReveal> _bidHistory = new List<RoundReveal>();
        private readonly ClueCatalog _clueCatalog;

        private IReadOnlyList<ParticipantState> _participants = Array.Empty<ParticipantState>();
        private Clue[] _clueChoices = Array.Empty<Clue>();
        private PackageGenerator.GeneratedPackage _package;
        private RoundReveal _roundReveal;
        private SettlementRecord _settlementRecord;
        private int _round;

        private int _consecutivePasses;
        private long _acceptedBidOrder;
        private TimeSpan _remainingTime;
        private bool _prepared;
        private bool _completed;
        private bool _settleAfterRevealing;


        public Authority(ItemCatalog itemCatalog, ClueCatalog clueCatalog, Random random)
        {
            _itemCatalog = itemCatalog ?? throw new ArgumentNullException(nameof(itemCatalog));
            _clueCatalog = clueCatalog ?? throw new ArgumentNullException(nameof(clueCatalog));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            CurrentPhase = MatchPhase.Waiting;
        }

        public string MatchId { get; private set; }
        public MatchPhase CurrentPhase { get; private set; }

        public event Action<AuthorityResult> ResultCreated;
        public event Action<AuthorityState> StateCreated;
        public event Action<SettlementRecord> SettlementCreated;
        public event Action<string> MatchCompleted;

        public void PrepareMatch(string matchId, IReadOnlyList<MatchParticipant> participants)
        {
            if (_prepared)
            {
                throw new InvalidOperationException("Authority 只能准备一局对局。");
            }
            if (string.IsNullOrWhiteSpace(matchId))
            {
                throw new ArgumentException("对局 ID 不能为空。", nameof(matchId));
            }
            if (participants == null || participants.Count != GlobalSettings.PlayerCount)
            {
                throw new ArgumentException("参赛者数量与全局配置不一致。", nameof(participants));
            }
            if (participants.Select(item => item.PlayerIdentity).Distinct().Count() != participants.Count)
            {
                throw new ArgumentException("参赛身份必须唯一。", nameof(participants));
            }

            MatchId = matchId;
            _participants = participants.Select(item => new ParticipantState(item)).ToArray();
            _package = new PackageGenerator(_itemCatalog, _random).Generate();
            _round = 0;
            CurrentPhase = MatchPhase.Waiting;
            _remainingTime = TimeSpan.Zero;
            _prepared = true;
            BroadcastStates();
        }

        public void StartMatch()
        {
            if (!_prepared)
            {
                throw new InvalidOperationException("必须先准备对局。");
            }
            if (_round != 0)
            {
                throw new InvalidOperationException("对局已经开始。");
            }
            _round = 1;
            BeginAnalysisPhase();
        }

        public void HandleRequest(string playerIdentity, ActionRequest request)
        {
            HandleRequestCore(playerIdentity, request);
        }

        public void AdvanceTime(TimeSpan deltaTime)
        {
            if (deltaTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }
            if (!_prepared || _completed)
            {
                return;
            }

            TimeSpan remainingDelta = deltaTime;
            while (!_completed)
            {
                if (!IsTimedPhase())
                {
                    break;
                }
                if (_remainingTime <= TimeSpan.Zero)
                {
                    CompleteTimedPhase();
                    continue;
                }
                if (remainingDelta <= TimeSpan.Zero)
                {
                    break;
                }

                TimeSpan step = remainingDelta < _remainingTime ? remainingDelta : _remainingTime;
                _remainingTime -= step;
                remainingDelta -= step;
                BroadcastStates();
            }
        }

        public VisibleRecord GetVisibleRecord(string playerIdentity)
        {
            return BuildVisibleRecord(Participant(playerIdentity));
        }

        private void HandleRequestCore(string playerIdentity, ActionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            string key = $"{playerIdentity}\n{request.RequestId}";
            if (_processedRequests.TryGetValue(key, out AuthorityResult previousResult))
            {
                ResultCreated?.Invoke(previousResult);
                return;
            }

            ParticipantState participant;
            try
            {
                participant = Participant(playerIdentity);
            }
            catch (Exception exception)
            {
                AuthorityResult unknownResult = new AuthorityResult(playerIdentity, request.RequestId, false, exception.Message, null);
                _processedRequests[key] = unknownResult;
                ResultCreated?.Invoke(unknownResult);
                return;
            }

            string rejection = null;
            if (_completed)
            {
                rejection = "对局已经结束。";
            }
            else if (request.MatchId != MatchId)
            {
                rejection = "请求不属于当前对局。";
            }
            else if (request.RoundId != _round)
            {
                rejection = "请求不属于当前竞拍轮。";
            }
            else if (request.ActionType == AuctionActionType.Clue && CurrentPhase != MatchPhase.Analysis)
            {
                rejection = "当前阶段不接受私有线索选择。";
            }
            else if (request.ActionType == AuctionActionType.Bid && CurrentPhase != MatchPhase.Bidding)
            {
                rejection = "当前阶段不接受出价。";
            }

            if (rejection == null)
            {
                rejection = request.ActionType == AuctionActionType.Clue
                    ? TryHandleClue(participant, request.ActionValue)
                    : TryHandleBid(participant, request.ActionValue);
            }

            bool accepted = rejection == null;
            AuthorityResult result = new AuthorityResult(
                playerIdentity,
                request.RequestId,
                accepted,
                rejection,
                BuildVisibleRecord(participant));
            _processedRequests[key] = result;
            ResultCreated?.Invoke(result);

            if (accepted)
            {
                BroadcastStates();
                TryCompleteCurrentPhase();
            }
        }

        private string TryHandleClue(ParticipantState participant, int clueId)
        {
            if (participant.ClueSubmitted)
            {
                return "本轮已经选择私有线索。";
            }

            Clue choice = _clueChoices.SingleOrDefault(item => item.Id == clueId);
            if (choice == null)
            {
                return "所选线索不在本轮共享候选线索集中。";
            }

            ClueRecord clue = CreateClueRecord(choice);
            participant.ClueSubmitted = true;
            participant.SelectedClue = clue;
            participant.PrivateClueHistory.Add(clue);

            AddKnowledge(participant.Knowledge, clue.Knowledge);
            return null;
        }

        private string TryHandleBid(ParticipantState participant, int amount)
        {
            if (participant.BidSubmitted)
            {
                return "本轮已经提交出价。";
            }
            if (amount < 0 || amount > participant.Assets)
            {
                return "出价不能小于零或超过可用资产。";
            }

            participant.BidSubmitted = true;
            participant.Bid = amount;
            participant.AcceptedBidOrder = ++_acceptedBidOrder;
            return null;
        }

        private void BeginAnalysisPhase()
        {
            CurrentPhase = MatchPhase.Analysis;
            _remainingTime = GlobalSettings.AnalysisDuration;
            _roundReveal = null;
            foreach (ParticipantState participant in _participants)
            {
                participant.ClueSubmitted = false;
                participant.SelectedClue = null;
                participant.BidSubmitted = false;
                participant.Bid = 0;
                participant.AcceptedBidOrder = 0;
            }

            // 从线索池抽取候选集与公共线索
            _clueChoices = _clueCatalog.PickDistinct(_random, GlobalSettings.CandidateClueCount).ToArray();
            Clue publicClue = _clueCatalog.Pick(_random);
            ClueRecord publicRecord = CreateClueRecord(publicClue);
            _publicClueHistory.Add(publicRecord);
            AddKnowledge(_publicKnowledge, publicRecord.Knowledge);

            BroadcastStates();
        }

        private void CompleteAnalysisPhase()
        {
            BeginBiddingPhase();
        }

        private void BeginBiddingPhase()
        {
            CurrentPhase = MatchPhase.Bidding;
            _remainingTime = GlobalSettings.BiddingDuration;
            BroadcastStates();
        }

        private void CompleteBiddingPhase()
        {
            foreach (ParticipantState participant in _participants.Where(item => !item.BidSubmitted))
            {
                participant.BidSubmitted = true;
                participant.Bid = 0;
                participant.AcceptedBidOrder = ++_acceptedBidOrder;
            }

            ParticipantState[] ranked = _participants
                .Where(item => item.Bid > 0)
                .OrderByDescending(item => item.Bid)
                .ThenBy(item => item.AcceptedBidOrder)
                .ToArray();
            ParticipantState winner = null;
            int winningBid = 0;
            bool shouldSettle = false;

            if (ranked.Length == 0)
            {
                if (_round >= 5)
                {
                    _consecutivePasses++;
                }
                shouldSettle = _round >= 5 && _consecutivePasses >= GlobalSettings.ConsecutivePassLimit;
            }
            else
            {
                _consecutivePasses = 0;
                ParticipantState first = ranked[0];
                int secondBid = ranked.Length == 1 ? 0 : ranked[1].Bid;
                decimal multiplier = WinningMultiplierFor(_round);
                if (ranked.Length == 1 || first.Bid >= multiplier * secondBid)
                {
                    winner = first;
                    winningBid = first.Bid;
                    shouldSettle = true;
                }
            }

            string winnerAssetOwnerId = winner?.AssetOwnerId;
            _roundReveal = new RoundReveal(
                _round,
                _participants.OrderByDescending(item => item.Bid).ThenBy(item => item.AcceptedBidOrder)
                    .Select(item => new RoundParticipantReveal(
                        item.AssetOwnerId,
                        item.Bid,
                        item.SelectedClue == null ? (int?)null : item.SelectedClue.ClueId))
                    .ToArray(),
                winnerAssetOwnerId,
                winningBid);
            _bidHistory.Add(_roundReveal);
            BeginRevealingPhase(shouldSettle);
        }

        private void BeginRevealingPhase(bool settleAfterRevealing)
        {
            CurrentPhase = MatchPhase.Revealing;
            _remainingTime = GlobalSettings.RoundRevealDuration;
            _settleAfterRevealing = settleAfterRevealing;
            BroadcastStates();
            CompleteZeroDurationPhases();
        }

        private void CompleteRevealingPhase()
        {
            if (_settleAfterRevealing)
            {
                SettleMatch(_roundReveal.WinnerAssetOwnerId, _roundReveal.WinningBid);
                return;
            }

            _round++;
            BeginAnalysisPhase();
        }

        private void SettleMatch(string winnerAssetOwnerId, int winningBid)
        {
            Dictionary<string, int> changesByParticipant = _participants.ToDictionary(item => item.PlayerIdentity, _ => 0);
            if (!string.IsNullOrEmpty(winnerAssetOwnerId))
            {
                ParticipantState winningParticipant = _participants.Single(item => item.AssetOwnerId == winnerAssetOwnerId);
                int winnerChange = _package.TotalValue - winningBid;
                winningParticipant.Assets += winnerChange;
                changesByParticipant[winningParticipant.PlayerIdentity] += winnerChange;
                if (winnerChange < 0)
                {
                    int each = (int)Math.Floor(
                        GlobalSettings.LossDistributionRatio * -winnerChange / (_participants.Count - 1));
                    foreach (ParticipantState participant in _participants.Where(item => item != winningParticipant))
                    {
                        participant.Assets += each;
                        changesByParticipant[participant.PlayerIdentity] += each;
                    }
                }
            }

            string settlementId = $"{MatchId}:settlement";
            Dictionary<string, int> finalAssets = new Dictionary<string, int>();
            foreach (ParticipantState participant in _participants)
            {
                int change = changesByParticipant[participant.PlayerIdentity];
                finalAssets[participant.AssetOwnerId] = participant.StartingAssets + change;
            }

            _settlementRecord = new SettlementRecord(MatchId, settlementId, finalAssets);
            CurrentPhase = MatchPhase.Settlement;
            _remainingTime = TimeSpan.Zero;
            _completed = true;
            SettlementCreated?.Invoke(_settlementRecord);
            BroadcastStates();
            MatchCompleted?.Invoke(MatchId);
        }

        private VisibleRecord BuildVisibleRecord(ParticipantState target)
        {
            IReadOnlyList<VisibleItem> knownItems = CurrentPhase == MatchPhase.Settlement
                ? FullPackage()
                : MergeKnowledge(_publicKnowledge.Values, target.Knowledge.Values);
            VisibleBoard board = new VisibleBoard(GlobalSettings.GridWidth, _package?.Height ?? 0, knownItems);
            bool canAct = CurrentPhase == MatchPhase.Analysis
                ? !target.ClueSubmitted
                : CurrentPhase == MatchPhase.Bidding && !target.BidSubmitted;
            SettlementView settlement = null;
            if (CurrentPhase == MatchPhase.Settlement)
            {
                string winnerAssetOwnerId = _roundReveal?.WinnerAssetOwnerId;
                int winningBid = _roundReveal?.WinningBid ?? 0;
                int ownChange = target.Assets - target.StartingAssets;
                settlement = new SettlementView(
                    _settlementRecord.SettlementId,
                    winnerAssetOwnerId,
                    winningBid,
                    _package.TotalValue,
                    ownChange,
                    target.Assets);
            }

            return new VisibleRecord(
                MatchId,
                target.Assets,
                CurrentPhase,
                _round,
                _remainingTime,
                canAct,
                board,
                CurrentPhase == MatchPhase.Analysis
                    ? _clueChoices.Select(clue => clue.Id).ToArray()
                    : Array.Empty<int>(),
                _bidHistory.ToArray(),
                _publicClueHistory.ToArray(),
                target.PrivateClueHistory.ToArray(),
                _participants.Select(item => new ParticipantVisibleState(
                    item.AssetOwnerId,
                    item.IsAI,
                    CurrentPhase == MatchPhase.Analysis
                        ? item.ClueSubmitted
                        : CurrentPhase != MatchPhase.Bidding || item.BidSubmitted)).ToArray(),
                _roundReveal,
                settlement);
        }

        private void BroadcastStates()
        {
            foreach (ParticipantState participant in _participants)
            {
                if (!_stateVersions.ContainsKey(participant.PlayerIdentity))
                {
                    _stateVersions[participant.PlayerIdentity] = 0;
                }

                StateCreated?.Invoke(
                    new AuthorityState(
                        participant.PlayerIdentity,
                        ++_stateVersions[participant.PlayerIdentity],
                        BuildVisibleRecord(participant)));
            }
        }

        private void TryCompleteCurrentPhase()
        {
            if (CurrentPhase == MatchPhase.Analysis && _participants.All(item => item.ClueSubmitted))
            {
                CompleteAnalysisPhase();
            }
            else if (CurrentPhase == MatchPhase.Bidding && _participants.All(item => item.BidSubmitted))
            {
                CompleteBiddingPhase();
            }
        }

        private void CompleteTimedPhase()
        {
            if (CurrentPhase == MatchPhase.Revealing)
            {
                CompleteRevealingPhase();
                return;
            }

            if (CurrentPhase == MatchPhase.Analysis)
            {
                CompleteAnalysisPhase();
            }
            else if (CurrentPhase == MatchPhase.Bidding)
            {
                CompleteBiddingPhase();
            }
        }

        private void CompleteZeroDurationPhases()
        {
            while (!_completed && IsTimedPhase() && _remainingTime <= TimeSpan.Zero)
            {
                CompleteTimedPhase();
            }
        }

        private bool IsTimedPhase()
        {
            return CurrentPhase == MatchPhase.Analysis ||
                   CurrentPhase == MatchPhase.Bidding ||
                   CurrentPhase == MatchPhase.Revealing;
        }

        private decimal WinningMultiplierFor(int round)
        {
            return round <= GlobalSettings.WinningMultipliers.Count
                ? GlobalSettings.WinningMultipliers[round - 1]
                : GlobalSettings.FinalWinningMultiplier;
        }

        private ClueRecord CreateClueRecord(Clue clue)
        {
            return new ClueRecord(
                _round,
                clue.Id,
                ResolveClue(clue));
        }

        private IReadOnlyList<VisibleItem> ResolveClue(Clue clue)
        {
            switch (clue.Id)
            {
                case 1:
                    return RarityShapeKnowledge(ItemRarity.N);
                case 2:
                    return RarityShapeKnowledge(ItemRarity.R);
                case 3:
                    return RarityShapeKnowledge(ItemRarity.SR);
                case 4:
                    return RarityShapeKnowledge(ItemRarity.SSR);
                case 5:
                    return RandomFullKnowledge();
                case 9:
                    return RarityFullKnowledge(ItemRarity.N);
                case 10:
                    return RarityFullKnowledge(ItemRarity.R);
                case 11:
                    return RarityFullKnowledge(ItemRarity.SR);
                case 12:
                    return RarityFullKnowledge(ItemRarity.SSR);
                case 13:
                    return RarityFullKnowledge(ItemRarity.UR);
                default:
                    return SizeShapeKnowledge(clue.Id);
            }
        }

        private IReadOnlyList<VisibleItem> RarityShapeKnowledge(ItemRarity rarity)
        {
            return _package.Items
                .Where(pair => pair.Value.Rarity == rarity)
                .Select(pair => Visible(pair.Key, pair.Value, false))
                .ToArray();
        }

        private IReadOnlyList<VisibleItem> RarityFullKnowledge(ItemRarity rarity)
        {
            return _package.Items
                .Where(pair => pair.Value.Rarity == rarity)
                .Select(pair => Visible(pair.Key, pair.Value, true))
                .ToArray();
        }

        private IReadOnlyList<VisibleItem> RandomFullKnowledge()
        {
            KeyValuePair<int, ItemData> random = _package.Items.ElementAt(_random.Next(_package.Items.Count));
            return new[] { Visible(random.Key, random.Value, true) };
        }

        private IReadOnlyList<VisibleItem> SizeShapeKnowledge(int clueId)
        {
            int index = clueId - 14;
            if (index < 0 || index >= SizeTable.Length)
            {
                return Array.Empty<VisibleItem>();
            }

            (int width, int height) = SizeTable[index];
            return _package.Items
                .Where(pair => pair.Value.Size.x == width && pair.Value.Size.y == height)
                .Select(pair => VisibleShapeOnly(pair.Key, pair.Value))
                .ToArray();
        }

        private static readonly (int width, int height)[] SizeTable =
        {
            (1, 1), (1, 2), (2, 1), (2, 2), (2, 3),
            (3, 2), (3, 3), (3, 4), (4, 3), (4, 4),
        };

        private VisibleItem VisibleShapeOnly(int instanceId, ItemData itemData)
        {
            PackageGenerator.PackageLayoutItem placement = _package.PlacementOf(instanceId);
            return new VisibleItem(
                instanceId,
                placement.X,
                placement.Y,
                placement.Width,
                placement.Height,
                ItemRarity.UNKNOWN,
                null,
                null);
        }

        private VisibleItem Visible(int instanceId, ItemData itemData, bool full)
        {
            PackageGenerator.PackageLayoutItem placement = _package.PlacementOf(instanceId);
            return new VisibleItem(
                instanceId,
                placement.X,
                placement.Y,
                placement.Width,
                placement.Height,
                itemData.Rarity,
                full ? itemData.ItemId : null,
                full ? itemData.BaseValue : (int?)null);
        }

        private IReadOnlyList<VisibleItem> FullPackage()
        {
            return _package.Items
                .Select(pair => Visible(pair.Key, pair.Value, true))
                .OrderBy(item => item.InstanceId)
                .ToArray();
        }

        private static IReadOnlyList<VisibleItem> MergeKnowledge(
            IEnumerable<VisibleItem> publicItems,
            IEnumerable<VisibleItem> privateItems)
        {
            Dictionary<int, VisibleItem> merged = new Dictionary<int, VisibleItem>();
            AddKnowledge(merged, publicItems);
            AddKnowledge(merged, privateItems);
            return merged.Values.OrderBy(item => item.InstanceId).ToArray();
        }

        private static void AddKnowledge(
            IDictionary<int, VisibleItem> target,
            IEnumerable<VisibleItem> items)
        {
            foreach (VisibleItem item in items)
            {
                if (!target.TryGetValue(item.InstanceId, out VisibleItem existing) || item.HasIdentity || !existing.HasIdentity)
                {
                    target[item.InstanceId] = item;
                }
            }
        }

        private ParticipantState Participant(string playerIdentity)
        {
            return _participants.SingleOrDefault(item => item.PlayerIdentity == playerIdentity)
                   ?? throw new InvalidOperationException("玩家不属于当前对局。");
        }

        private sealed class ParticipantState
        {
            public ParticipantState(MatchParticipant participant)
            {
                PlayerIdentity = participant.PlayerIdentity;
                StartingAssets = participant.StartingAssets;
                Assets = participant.StartingAssets;
                IsAI = participant.IsAI;
                AssetOwnerId = participant.AssetOwnerId;
            }

            public string PlayerIdentity { get; }
            public int StartingAssets { get; }
            public int Assets { get; set; }
            public bool IsAI { get; }
            public string AssetOwnerId { get; }
            public bool ClueSubmitted { get; set; }
            public ClueRecord SelectedClue { get; set; }
            public bool BidSubmitted { get; set; }
            public int Bid { get; set; }
            public long AcceptedBidOrder { get; set; }
            public List<ClueRecord> PrivateClueHistory { get; } = new List<ClueRecord>();
            public Dictionary<int, VisibleItem> Knowledge { get; } = new Dictionary<int, VisibleItem>();
        }
    }
}

