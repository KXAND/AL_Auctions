using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace AuctionGame.Tests
{
    public sealed class RevealingPhaseTests
    {
        private const string MatchId = "revealing-phase-match";
        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        private Authority _authority;

        [SetUp]
        public void SetUp()
        {
            ItemData item = CreateScriptableObject<ItemData>();
            SetField(item, "itemId", "test-item");
            SetField(item, "displayName", "Test Item");
            SetField(item, "size", Vector2Int.one);
            SetField(item, "rarity", ItemRarity.SSR);
            SetField(item, "baseValue", 50);

            ItemCatalog itemCatalog = CreateScriptableObject<ItemCatalog>();
            SetField(itemCatalog, "items", new[] { item });

            Clue clue = CreateScriptableObject<Clue>();
            SetField(clue, "id", 1);
            SetField(clue, "displayName", "Test Clue");

            ClueCatalog clueCatalog = CreateScriptableObject<ClueCatalog>();
            SetField(clueCatalog, "clues", new[] { clue });

            _authority = new Authority(itemCatalog, clueCatalog, new System.Random(1));
            _authority.PrepareMatch(MatchId, CreateParticipants());
            _authority.StartMatch();
            SubmitAllClues();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object createdObject in _createdObjects)
            {
                if (createdObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObject);
                }
            }
        }

        [Test]
        public void MatchPhaseContainsRevealing()
        {
            Assert.That(Enum.GetNames(typeof(MatchPhase)), Does.Contain("Revealing"));
        }

        [Test]
        public void CompletedBidsEnterRevealingWithVisibleResult()
        {
            SubmitAllBids(0, 0, 0, 0);

            VisibleRecord record = _authority.GetVisibleRecord("player-0");
            Assert.That(_authority.CurrentPhase.ToString(), Is.EqualTo("Revealing"));
            Assert.That(record.Phase.ToString(), Is.EqualTo("Revealing"));
            Assert.That(record.Round, Is.EqualTo(1));
            Assert.That(record.RemainingTime, Is.EqualTo(GlobalSettings.RoundRevealDuration));
            Assert.That(record.CanRequestAction, Is.False);
            Assert.That(record.RoundReveal, Is.Not.Null);
            Assert.That(record.RoundReveal.Round, Is.EqualTo(1));
        }

        [Test]
        public void UnsettledRevealCompletesIntoNextAnalysisRound()
        {
            SubmitAllBids(0, 0, 0, 0);

            _authority.AdvanceTime(GlobalSettings.RoundRevealDuration - TimeSpan.FromMilliseconds(1));
            Assert.That(_authority.CurrentPhase.ToString(), Is.EqualTo("Revealing"));

            _authority.AdvanceTime(TimeSpan.FromMilliseconds(1));
            VisibleRecord record = _authority.GetVisibleRecord("player-0");
            Assert.That(_authority.CurrentPhase, Is.EqualTo(MatchPhase.Analysis));
            Assert.That(record.Round, Is.EqualTo(2));
            Assert.That(record.RoundReveal, Is.Null);
        }

        [Test]
        public void WinningRevealCompletesIntoSettlement()
        {
            SubmitAllBids(10, 0, 0, 0);

            Assert.That(_authority.CurrentPhase.ToString(), Is.EqualTo("Revealing"));
            Assert.That(_authority.GetVisibleRecord("player-0").Settlement, Is.Null);

            _authority.AdvanceTime(GlobalSettings.RoundRevealDuration);
            VisibleRecord record = _authority.GetVisibleRecord("player-0");
            Assert.That(_authority.CurrentPhase, Is.EqualTo(MatchPhase.Settlement));
            Assert.That(record.Settlement, Is.Not.Null);
            Assert.That(record.RoundReveal.WinnerAssetOwnerId, Is.EqualTo("owner-0"));
        }

        [Test]
        public void RevealingRejectsParticipantActionsByPhase()
        {
            SubmitAllBids(0, 0, 0, 0);
            AuthorityResult result = null;
            _authority.ResultCreated += createdResult =>
            {
                if (createdResult.RequestId == "reveal-bid")
                {
                    result = createdResult;
                }
            };

            _authority.HandleRequest(
                "player-0",
                ActionRequest.CreateBid("reveal-bid", MatchId, 1, 1));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.RejectedReason, Is.EqualTo("当前阶段不接受出价。"));
        }

        private void SubmitAllClues()
        {
            for (int index = 0; index < GlobalSettings.PlayerCount; index++)
            {
                _authority.HandleRequest(
                    $"player-{index}",
                    ActionRequest.CreateClue($"clue-{index}", MatchId, 1, 1));
            }

            Assert.That(_authority.CurrentPhase, Is.EqualTo(MatchPhase.Bidding));
        }

        private void SubmitAllBids(int first, int second, int third, int fourth)
        {
            int[] bids = { first, second, third, fourth };
            for (int index = 0; index < bids.Length; index++)
            {
                _authority.HandleRequest(
                    $"player-{index}",
                    ActionRequest.CreateBid($"bid-{index}", MatchId, 1, bids[index]));
            }
        }

        private static MatchParticipant[] CreateParticipants()
        {
            MatchParticipant[] participants = new MatchParticipant[GlobalSettings.PlayerCount];
            for (int index = 0; index < participants.Length; index++)
            {
                participants[index] = new MatchParticipant(
                    $"player-{index}",
                    GlobalSettings.InitialAssets,
                    false,
                    $"owner-{index}");
            }
            return participants;
        }

        private T CreateScriptableObject<T>() where T : ScriptableObject
        {
            T createdObject = ScriptableObject.CreateInstance<T>();
            _createdObjects.Add(createdObject);
            return createdObject;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
