using System;
using System.Linq;
using AuctionGame;
using NUnit.Framework;

namespace AuctionGame.Tests
{
    [TestFixture]
    public sealed class AuctionLifecycleAndSettlementTests
    {
        [Test]
        public void TimeoutsLeavePrivateCluesEmptyAndEndPostFifthRoundPassesWithoutSettlementChanges()
        {
            var rules = AuctionRules.CreateDemo(4, 100).WithAuctionLifecycle(new[] { 1m }, 1m, 1, 1m);
            var match = AuctionMatch.Create(rules, new SequenceRandomSource(0));
            var human = match.ConnectHuman("human");
            match.Start();

            for (var round = 1; round <= 5; round++)
            {
                match.AdvanceTime(rules.AnalysisDuration);
                Assert.That(match.GetSeatView(human).PrivateClueResult, Is.Null);
                match.AdvanceTime(rules.BiddingDuration);
                Assert.That(match.GetSeatView(human).RoundReveal.Seats.Single(item => item.SeatIndex == human).Bid, Is.EqualTo(0));
                match.AdvanceTime(rules.RoundRevealDuration);
            }

            var settlement = match.GetSeatView(human);
            Assert.That(settlement.Phase, Is.EqualTo(AuctionPhase.Settlement));
            Assert.That(settlement.Settlement.WinnerSlot, Is.EqualTo(-1));
            Assert.That(settlement.AvailableAssets, Is.EqualTo(100));
        }

        [Test]
        public void MultipleBidsBelowTheConfiguredMultiplierCarryIntoTheNextRoundWithoutAssetChanges()
        {
            var rules = AuctionRules.CreateDemo(4, 100).WithAuctionLifecycle(new[] { 2m }, 1m, 3, 1m);
            var match = AuctionMatch.Create(rules, new SequenceRandomSource(0));
            var first = match.ConnectHuman("first");
            var second = match.ConnectHuman("second");
            match.Start();
            match.AdvanceTime(rules.AnalysisDuration);
            match.SubmitBid(first, 100);
            match.SubmitBid(second, 60);
            match.AdvanceTime(rules.BiddingDuration);

            var reveal = match.GetSeatView(first);
            Assert.That(reveal.Phase, Is.EqualTo(AuctionPhase.RoundReveal));
            Assert.That(reveal.RoundReveal.Result.WinnerSlot, Is.EqualTo(-1));
            Assert.That(reveal.AvailableAssets, Is.EqualTo(100));
            match.AdvanceTime(rules.RoundRevealDuration);
            Assert.That(match.GetSeatView(first).Phase, Is.EqualTo(AuctionPhase.Analysis));
        }

        [Test]
        public void TimeAdvanceCarriesOverflowIntoTheNextPhase()
        {
            var rules = AuctionRules.CreateDemo(4, 100)
                .WithPhaseDurations(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.Zero)
                .WithAiMaximumActionDelay(TimeSpan.Zero);
            var match = AuctionMatch.Create(rules, new SequenceRandomSource(0));
            var human = match.ConnectHuman("human");
            match.Start();

            match.AdvanceTime(TimeSpan.FromSeconds(1.5));

            var view = match.GetSeatView(human);
            Assert.That(view.Phase, Is.EqualTo(AuctionPhase.Bidding));
            Assert.That(view.Remaining, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
        }

        [Test]
        public void AFailedBidRoundResetsThePostFifthRoundConsecutivePassCounter()
        {
            var rules = AuctionRules.CreateDemo(4, 100)
                .WithAuctionLifecycle(new[] { 10m }, 10m, 2, 1m)
                .WithAiMaximumActionDelay(TimeSpan.Zero);
            var match = AuctionMatch.Create(rules, new SequenceRandomSource(0));
            var first = match.ConnectHuman("first");
            var second = match.ConnectHuman("second");
            match.Start();

            for (var round = 1; round <= 5; round++)
            {
                match.AdvanceTime(rules.AnalysisDuration);
                match.AdvanceTime(rules.BiddingDuration);
                match.AdvanceTime(rules.RoundRevealDuration);
            }

            match.AdvanceTime(rules.AnalysisDuration);
            match.SubmitBid(first, 20);
            match.SubmitBid(second, 19);
            match.AdvanceTime(rules.BiddingDuration);
            match.AdvanceTime(rules.RoundRevealDuration);
            match.AdvanceTime(rules.AnalysisDuration);
            match.AdvanceTime(rules.BiddingDuration);
            match.AdvanceTime(rules.RoundRevealDuration);

            Assert.That(match.GetSeatView(first).Phase, Is.EqualTo(AuctionPhase.Analysis));
        }

        [Test]
        public void RoundRevealIsHiddenUntilBiddingEndsAndSettlementIncludesPackageTruth()
        {
            var rules = AuctionRules.CreateDemo(4, 200).WithAuctionLifecycle(new[] { 1m }, 1m, 3, 1m);
            var match = AuctionMatch.Create(rules, new SequenceRandomSource(0));
            var first = match.ConnectHuman("first");
            var second = match.ConnectHuman("second");
            match.Start();
            match.SelectPrivateClue(first, match.GetSeatView(first).PrivateClueChoices[0].Id);
            match.SelectPrivateClue(second, match.GetSeatView(second).PrivateClueChoices[0].Id);
            match.AdvanceTime(rules.AnalysisDuration);
            match.SubmitBid(first, 120);
            match.SubmitBid(second, 50);

            Assert.That(match.GetSeatView(first).RoundReveal, Is.Null);
            match.AdvanceTime(rules.BiddingDuration);

            var reveal = match.GetSeatView(first);
            Assert.That(reveal.Phase, Is.EqualTo(AuctionPhase.RoundReveal));
            Assert.That(reveal.RoundReveal.Seats, Has.Length.EqualTo(rules.PlayerCount));
            Assert.That(reveal.RoundReveal.Seats.Single(item => item.SeatIndex == first).Bid, Is.EqualTo(120));
            Assert.That(reveal.RoundReveal.Seats.Single(item => item.SeatIndex == second).ClueKind, Is.Not.Null);
            Assert.That(reveal.SettlementPackage, Is.Empty);
            match.AdvanceTime(rules.RoundRevealDuration);

            var settlement = match.GetSeatView(first);
            Assert.That(settlement.SettlementPackage, Is.Not.Empty);
            Assert.That(settlement.SettlementPackage.Sum(item => item.Value), Is.EqualTo(100));
        }

        [Test]
        public void EqualBidsUseAuthorityOrderAndOverpaymentDistributesIntegerLosses()
        {
            var rules = AuctionRules.CreateDemo(4, 200).WithAuctionLifecycle(
                new[] { 1m }, 1m, 3, 1m);
            var match = AuctionMatch.Create(rules, new SequenceRandomSource(0));
            var first = match.ConnectHuman("first");
            var second = match.ConnectHuman("second");
            var third = match.ConnectHuman("third");
            match.Start();
            match.AdvanceTime(rules.AnalysisDuration);

            match.SubmitBid(second, 150);
            match.SubmitBid(first, 150);
            match.SubmitBid(third, 10);
            match.AdvanceTime(rules.BiddingDuration);
            match.AdvanceTime(rules.RoundRevealDuration);

            Assert.That(match.GetSeatView(second).Settlement.WinnerSlot, Is.EqualTo(second));
            Assert.That(match.GetSeatView(second).AvailableAssets, Is.EqualTo(150));
            Assert.That(match.GetSeatView(first).AvailableAssets, Is.EqualTo(216));
            Assert.That(match.GetSeatView(third).AvailableAssets, Is.EqualTo(216));
        }
    }
}
