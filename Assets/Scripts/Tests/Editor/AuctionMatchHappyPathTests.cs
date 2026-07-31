using System.Linq;
using AuctionGame.Gameplay;
using NUnit.Framework;

namespace AuctionGame.Tests
{
    [TestFixture]
    public sealed class AuctionMatchHappyPathTests
    {
        [Test]
        public void HumanPlayerCanCompleteTheFirstAuctionWithoutReceivingPackageTruth()
        {
            var rules = AuctionRules.CreateDemo(playerCount: 4, initialAssets: 100);
            var match = AuctionMatch.CreateDemo(rules);
            var humanSlot = match.ConnectHuman("human-connection");

            match.Start();

            var analysisView = match.GetSeatView(humanSlot);
            Assert.That(analysisView.Phase, Is.EqualTo(AuctionPhase.Analysis));
            Assert.That(analysisView.AvailableAssets, Is.EqualTo(100));
            Assert.That(analysisView.Grid.Width, Is.EqualTo(rules.GridWidth));
            Assert.That(analysisView.PrivateClueChoices, Is.Not.Empty);

            match.SelectPrivateClue(humanSlot, analysisView.PrivateClueChoices[0].Id);

            Assert.That(match.GetSeatView(humanSlot).PrivateClueResult, Is.Not.Null);

            match.AdvanceTime(rules.AnalysisDuration);
            Assert.That(match.GetSeatView(humanSlot).Phase, Is.EqualTo(AuctionPhase.Bidding));

            match.SubmitBid(humanSlot, 75);
            match.AdvanceTime(rules.BiddingDuration);
            Assert.That(match.GetSeatView(humanSlot).Phase, Is.EqualTo(AuctionPhase.RoundReveal));
            match.AdvanceTime(rules.RoundRevealDuration);

            var settlementView = match.GetSeatView(humanSlot);
            Assert.That(settlementView.Phase, Is.EqualTo(AuctionPhase.Settlement));
            Assert.That(settlementView.AvailableAssets, Is.EqualTo(125));
            Assert.That(settlementView.Settlement.WinnerSlot, Is.EqualTo(humanSlot));
            Assert.That(settlementView.Settlement.WinningBid, Is.EqualTo(75));
            Assert.That(
                typeof(AuctionSeatView).GetProperties().Select(property => property.Name),
                Does.Not.Contain("PackageTotalValue"));
        }
    }
}
