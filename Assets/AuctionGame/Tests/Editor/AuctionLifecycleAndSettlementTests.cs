using System;
using AuctionGame;
using NUnit.Framework;

namespace AuctionGame.Tests
{
    [TestFixture]
    public sealed class AuctionLifecycleAndSettlementTests
    {
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

            Assert.That(match.GetSeatView(second).Settlement.WinnerSlot, Is.EqualTo(second));
            Assert.That(match.GetSeatView(second).AvailableAssets, Is.EqualTo(150));
            Assert.That(match.GetSeatView(first).AvailableAssets, Is.EqualTo(216));
            Assert.That(match.GetSeatView(third).AvailableAssets, Is.EqualTo(216));
        }
    }
}
