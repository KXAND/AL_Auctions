using System;
using System.Linq;
using AuctionGame;
using NUnit.Framework;

namespace AuctionGame.Tests
{
    [TestFixture]
    public sealed class AiSlotsAndConnectionLifecycleTests
    {
        [Test]
        public void AiActionsUseTheSeatViewAndAreCappedAfterHumanCompletion()
        {
            var rules = AuctionRules.CreateDemo(4, 100).WithAiMaximumActionDelay(TimeSpan.FromSeconds(5));
            var match = AuctionMatch.Create(rules, new SequenceRandomSource(9_999));
            var human = match.ConnectHuman("human");
            match.Start();

            Assert.That(match.IsAiControlled(1), Is.True);
            Assert.That(match.GetSeatView(1).PrivateClueResult, Is.Null);
            match.SelectPrivateClue(human, match.GetSeatView(human).PrivateClueChoices[0].Id);
            match.AdvanceTime(TimeSpan.FromMilliseconds(999));
            Assert.That(match.GetSeatView(1).PrivateClueResult, Is.Null);
            match.AdvanceTime(TimeSpan.FromMilliseconds(1));
            Assert.That(match.GetSeatView(1).PrivateClueResult, Is.Not.Null);
            Assert.That(new SimpleRuleAi().ChoosePrivateClue(match.GetSeatView(1)), Is.Not.Null);
            Assert.That(new AiActionScheduler(new SequenceRandomSource(9_999)).Schedule(TimeSpan.FromSeconds(5), true), Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void AiSeatViewDoesNotExposeAnotherSeatsPrivateFullRevealOrSettlementTruth()
        {
            var rules = AuctionRules.CreateDemo(4, 100).WithAiMaximumActionDelay(TimeSpan.FromSeconds(5));
            var match = AuctionMatch.Create(rules, new SequenceRandomSource(0, 0, 0, 0, 0, 0, 0, 9_999));
            var human = match.ConnectHuman("human");
            match.Start();

            var fullReveal = match.GetSeatView(human).PrivateClueChoices.Single(choice => choice.Kind == ClueKind.RandomFullReveal);
            match.SelectPrivateClue(human, fullReveal.Id);
            var aiView = match.GetSeatView(1);

            Assert.That(match.GetSeatView(human).PrivateClueResult.Knowledge.Single().Value, Is.Not.Null);
            Assert.That(aiView.PrivateClueResult, Is.Null);
            Assert.That(aiView.Knowledge.All(item => item.Name == null && item.Value == null), Is.True);
            Assert.That(aiView.SettlementPackage, Is.Empty);
            Assert.That(new SimpleRuleAi().ChoosePrivateClue(aiView), Is.Not.Null);
        }

        [Test]
        public void DisconnectTransfersTheSeatToAiWithoutSettlingTheOldConnection()
        {
            var rules = AuctionRules.CreateDemo(4, 200).WithAuctionLifecycle(new[] { 1m }, 1m, 3, 1m);
            var session = new AuctionSession(rules, new SequenceRandomSource(0));
            var oldConnection = session.OpenConnection("old-connection");
            var opponent = session.OpenConnection("opponent");
            var match = session.StartNextMatch();
            var oldSeat = session.SeatOf(oldConnection);
            var opponentSeat = session.SeatOf(opponent);

            session.Disconnect(oldConnection);
            match.AdvanceTime(rules.AnalysisDuration);
            match.SubmitBid(opponentSeat, 150);
            match.AdvanceTime(rules.BiddingDuration);

            Assert.That(match.IsAiControlled(oldSeat), Is.True);
            Assert.That(oldConnection.IsConnected, Is.False);
            Assert.That(oldConnection.AvailableAssets, Is.EqualTo(200));
            Assert.That(match.GetSeatView(oldSeat).AvailableAssets, Is.EqualTo(216));
            Assert.That(opponent.AvailableAssets, Is.EqualTo(150));
        }

        [Test]
        public void ReconnectingCreatesANewAccountWhileAnActiveMatchKeepsItsSeatsLocked()
        {
            var session = new AuctionSession(AuctionRules.CreateDemo(4, 100), new SequenceRandomSource(0));
            var first = session.OpenConnection("first");
            session.StartNextMatch();
            var reconnected = session.OpenConnection("first");

            Assert.That(session.IsWaiting(reconnected), Is.True);
            Assert.That(reconnected.AvailableAssets, Is.EqualTo(100));
            Assert.Throws<InvalidOperationException>(() => session.StartNextMatch());
        }

        [Test]
        public void AConnectedAccountCarriesItsSettledAssetsIntoTheNextMatch()
        {
            var rules = AuctionRules.CreateDemo(4, 100);
            var session = new AuctionSession(rules, new SequenceRandomSource(0));
            var connection = session.OpenConnection("player");
            var firstMatch = session.StartNextMatch();
            var firstSeat = session.SeatOf(connection);

            firstMatch.AdvanceTime(rules.AnalysisDuration);
            firstMatch.SubmitBid(firstSeat, 75);
            firstMatch.AdvanceTime(rules.BiddingDuration);
            firstMatch.AdvanceTime(rules.RoundRevealDuration);
            var nextMatch = session.StartNextMatch();

            Assert.That(connection.AvailableAssets, Is.EqualTo(125));
            Assert.That(nextMatch.GetSeatView(session.SeatOf(connection)).AvailableAssets, Is.EqualTo(125));
        }
    }
}
