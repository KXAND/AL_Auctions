using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame
{
    public enum AuctionActionType
    {
        Bid, Clue
    }
    public enum MatchPhase
    {
        Waiting, Analysis, Bidding, Revealing, Settlement
    }

    public enum PendingRequestState
    {
        Pending, Accepted, Rejected, Unknown
    }
    public enum AuctionManagerMode
    {
        None, Local, ServerAI, OnlineClient
    }

    public sealed class ActionRequest
    {
        public ActionRequest(
            string requestId,
            string matchId,
            int roundId,
            AuctionActionType actionType,
            int actionValue)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("请求 ID 不能为空。", nameof(requestId));
            }
            RequestId = requestId;
            MatchId = matchId;
            RoundId = roundId;
            ActionType = actionType;
            ActionValue = actionValue;
        }

        public string RequestId { get; }
        public string MatchId { get; }
        public int RoundId { get; }
        public AuctionActionType ActionType { get; }
        public int ActionValue { get; }

        public static ActionRequest CreateBid(string requestId, string matchId, int roundId, int amount)
        {
            return new ActionRequest(requestId, matchId, roundId, AuctionActionType.Bid, amount);
        }

        public static ActionRequest CreateClue(string requestId, string matchId, int roundId, int clueId)
        {
            return new ActionRequest(requestId, matchId, roundId, AuctionActionType.Clue, clueId);
        }
    }

    public sealed class MatchParticipant
    {
        public MatchParticipant(
            string playerIdentity,
            int startingAssets,
            bool isAI,
            string assetOwnerId)
        {
            if (string.IsNullOrWhiteSpace(playerIdentity))
            {
                throw new ArgumentException("玩家身份不能为空。", nameof(playerIdentity));
            }
            if (startingAssets < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            PlayerIdentity = playerIdentity;
            StartingAssets = startingAssets;
            IsAI = isAI;
            AssetOwnerId = assetOwnerId;
        }

        public string PlayerIdentity { get; }
        public int StartingAssets { get; }
        public bool IsAI { get; internal set; }
        public string AssetOwnerId { get; }
    }

    public sealed class AuthorityResult
    {
        public AuthorityResult(
            string targetPlayer,
            string requestId,
            bool accepted,
            string rejectedReason,
            VisibleRecord visibleRecord)
        {
            TargetPlayer = targetPlayer;
            RequestId = requestId;
            Accepted = accepted;
            RejectedReason = rejectedReason;
            VisibleRecord = visibleRecord;
        }

        public string TargetPlayer { get; }
        public string RequestId { get; }
        public bool Accepted { get; }
        public string RejectedReason { get; }
        public VisibleRecord VisibleRecord { get; }
    }

    public sealed class AuthorityState
    {
        public AuthorityState(string targetPlayer, long stateVersion, VisibleRecord visibleRecord)
        {
            TargetPlayer = targetPlayer;
            StateVersion = stateVersion;
            VisibleRecord = visibleRecord;
        }

        public string TargetPlayer { get; }
        public long StateVersion { get; }
        public VisibleRecord VisibleRecord { get; }
    }

    public sealed class VisibleRecord
    {
        public VisibleRecord(
            string matchId,
            int ownAssets,
            MatchPhase phase,
            int round,
            TimeSpan remainingTime,
            bool canRequestAction,
            VisibleBoard visibleBoard,
            IReadOnlyList<int> clueChoices,
            IReadOnlyList<RoundReveal> bidHistory,
            IReadOnlyList<ClueRecord> publicClueHistory,
            IReadOnlyList<ClueRecord> privateClueHistory,
            IReadOnlyList<ParticipantVisibleState> participants,
            RoundReveal roundReveal,
            SettlementView settlement)
        {
            MatchId = matchId;
            OwnAssets = ownAssets;
            Phase = phase;
            Round = round;
            RemainingTime = remainingTime;
            CanRequestAction = canRequestAction;
            VisibleBoard = visibleBoard ?? VisibleBoard.Empty;
            ClueChoices = Freeze(clueChoices);
            BidHistory = Freeze(bidHistory);
            PublicClueHistory = Freeze(publicClueHistory);
            PrivateClueHistory = Freeze(privateClueHistory);
            Participants = Freeze(participants);
            RoundReveal = roundReveal;
            Settlement = settlement;
        }

        public string MatchId { get; }
        public int OwnAssets { get; }
        public MatchPhase Phase { get; }
        public int Round { get; }
        public TimeSpan RemainingTime { get; }
        public bool CanRequestAction { get; }
        public VisibleBoard VisibleBoard { get; }
        public IReadOnlyList<int> ClueChoices { get; }
        public IReadOnlyList<RoundReveal> BidHistory { get; }
        public IReadOnlyList<ClueRecord> PublicClueHistory { get; }
        public IReadOnlyList<ClueRecord> PrivateClueHistory { get; }
        public IReadOnlyList<ParticipantVisibleState> Participants { get; }
        public RoundReveal RoundReveal { get; }
        public SettlementView Settlement { get; }

        public static VisibleRecord Waiting(string matchId, int ownAssets)
        {
            return new VisibleRecord(
                matchId,
                ownAssets,
                MatchPhase.Waiting,
                0,
                TimeSpan.Zero,
                false,
                VisibleBoard.Empty,
                Array.Empty<int>(),
                Array.Empty<RoundReveal>(),
                Array.Empty<ClueRecord>(),
                Array.Empty<ClueRecord>(),
                Array.Empty<ParticipantVisibleState>(),
                null,
                null);
        }

        private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> source)
        {
            return new List<T>(source ?? Enumerable.Empty<T>()).AsReadOnly();
        }
    }

    public sealed class VisibleBoard
    {
        public static readonly VisibleBoard Empty = new VisibleBoard(0, 0, Array.Empty<VisibleItem>());

        public VisibleBoard(int width, int height, IReadOnlyList<VisibleItem> items)
        {
            Width = width;
            Height = height;
            Items = new List<VisibleItem>(items ?? Array.Empty<VisibleItem>()).AsReadOnly();
        }

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<VisibleItem> Items { get; }
    }

    public sealed class VisibleItem
    {
        public VisibleItem(
            int instanceId,
            int x,
            int y,
            int width,
            int height,
            ItemRarity rarity,
            string itemId,
            int? value)
        {
            InstanceId = instanceId;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Rarity = rarity;
            ItemId = itemId;
            Value = value;
        }

        public int InstanceId { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public ItemRarity Rarity { get; }
        public string ItemId { get; }
        public int? Value { get; }
        public bool HasIdentity => !string.IsNullOrEmpty(ItemId);
    }

    public sealed class ClueRecord
    {
        public ClueRecord(
            int round,
            int clueId,
            IReadOnlyList<VisibleItem> knowledge)
        {
            Round = round;
            ClueId = clueId;
            Knowledge = new List<VisibleItem>(knowledge ?? Array.Empty<VisibleItem>()).AsReadOnly();
        }

        public int Round { get; }
        public int ClueId { get; }
        public IReadOnlyList<VisibleItem> Knowledge { get; }
    }

    public sealed class ParticipantVisibleState
    {
        public ParticipantVisibleState(string assetOwnerId, bool isAI, bool hasCompletedAction)
        {
            AssetOwnerId = assetOwnerId;
            IsAI = isAI;
            HasCompletedAction = hasCompletedAction;
        }

        public string AssetOwnerId { get; }
        public bool IsAI { get; }
        public bool HasCompletedAction { get; }
    }

    public sealed class RoundReveal
    {
        public RoundReveal(
            int round,
            IReadOnlyList<RoundParticipantReveal> participants,
            string winnerAssetOwnerId,
            int winningBid)
        {
            Round = round;
            Participants = new List<RoundParticipantReveal>(participants ?? Array.Empty<RoundParticipantReveal>()).AsReadOnly();
            WinnerAssetOwnerId = winnerAssetOwnerId;
            WinningBid = winningBid;
        }

        public int Round { get; }
        public IReadOnlyList<RoundParticipantReveal> Participants { get; }
        public string WinnerAssetOwnerId { get; }
        public int WinningBid { get; }
    }

    public sealed class RoundParticipantReveal
    {
        public RoundParticipantReveal(string assetOwnerId, int bid, int? clueId)
        {
            AssetOwnerId = assetOwnerId;
            Bid = bid;
            ClueId = clueId;
        }

        public string AssetOwnerId { get; }
        public int Bid { get; }
        public int? ClueId { get; }
    }

    public sealed class SettlementView
    {
        public SettlementView(
            string settlementId,
            string winnerAssetOwnerId,
            int winningBid,
            int packageTotalValue,
            int ownAssetChange,
            int finalVisibleAssets)
        {
            SettlementId = settlementId;
            WinnerAssetOwnerId = winnerAssetOwnerId;
            WinningBid = winningBid;
            PackageTotalValue = packageTotalValue;
            OwnAssetChange = ownAssetChange;
            FinalVisibleAssets = finalVisibleAssets;
        }

        public string SettlementId { get; }
        public string WinnerAssetOwnerId { get; }
        public int WinningBid { get; }
        public int PackageTotalValue { get; }
        public int OwnAssetChange { get; }
        public int FinalVisibleAssets { get; }
    }

    public sealed class PendingRequest
    {
        internal PendingRequest(ActionRequest request, IAuctionController controller)
        {
            Request = request;
            Controller = controller;
            State = PendingRequestState.Pending;
            PendingTime = TimeSpan.Zero;
        }

        public string RequestId => Request.RequestId;
        public ActionRequest Request { get; }
        public IAuctionController Controller { get; }
        public TimeSpan PendingTime { get; internal set; }
        public PendingRequestState State { get; internal set; }
        public string Reason { get; internal set; }
    }

    public sealed class RequestStateChange
    {
        public RequestStateChange(string targetPlayer, string requestId, PendingRequestState state, string reason)
        {
            TargetPlayer = targetPlayer;
            RequestId = requestId;
            State = state;
            Reason = reason;
        }

        public string TargetPlayer { get; }
        public string RequestId { get; }
        public PendingRequestState State { get; }
        public string Reason { get; }
    }

    public sealed class SettlementRecord
    {
        public SettlementRecord(
            string matchId,
            string settlementId,
            IReadOnlyDictionary<string, int> finalVisibleAssets)
        {
            MatchId = matchId;
            SettlementId = settlementId;
            FinalVisibleAssets = finalVisibleAssets ?? new Dictionary<string, int>();
        }

        public string MatchId { get; }
        public string SettlementId { get; }
        public IReadOnlyDictionary<string, int> FinalVisibleAssets { get; }
    }
}



