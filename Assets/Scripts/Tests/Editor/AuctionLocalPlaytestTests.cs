using System;
using System.Linq;
using AuctionGame.Gameplay;
using NUnit.Framework;

namespace AuctionGame.Tests
{
    [TestFixture]
    public sealed class AuctionLocalPlaytestTests
    {
        [Test]
        public void LocalPlaytestUsesTheConfiguredPlayerCountToFillAiSeats()
        {
            var rules = AuctionRules.CreateDemo(2, 100)
                .WithPhaseDurations(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.Zero)
                .WithAiMaximumActionDelay(TimeSpan.Zero);
            var playtest = new AuctionLocalPlaytest(rules, new SequenceRandomSource(0));

            playtest.Start();
            playtest.SelectPrivateClue(playtest.CurrentView.PrivateClueChoices[0].Id);
            playtest.AdvanceTime(TimeSpan.FromSeconds(1));
            playtest.SubmitBid(75);
            playtest.AdvanceTime(TimeSpan.FromSeconds(1));

            var reveal = playtest.CurrentView.RoundReveal.Seats;
            Assert.That(reveal, Has.Length.EqualTo(rules.PlayerCount));
            Assert.That(reveal.Count(item => item.Bid == 0), Is.EqualTo(rules.PlayerCount - 1));
            Assert.That(reveal.Where(item => item.Bid == 0).All(item => item.ClueKind != null), Is.True);
        }

        [Test]
        public void LocalPlaytestFillsAiSeatsCarriesAssetsAndCanReset()
        {
            var rules = AuctionRules.CreateDemo(4, 100)
                .WithPhaseDurations(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.Zero)
                .WithAiMaximumActionDelay(TimeSpan.Zero);
            var playtest = new AuctionLocalPlaytest(rules, new SequenceRandomSource(0));

            playtest.Start();

            Assert.That(playtest.CurrentView.Phase, Is.EqualTo(AuctionPhase.Analysis));

            playtest.SelectPrivateClue(playtest.CurrentView.PrivateClueChoices[0].Id);
            playtest.AdvanceTime(TimeSpan.FromSeconds(1));
            playtest.SubmitBid(75);
            playtest.AdvanceTime(TimeSpan.FromSeconds(1));

            Assert.That(playtest.CurrentView.Phase, Is.EqualTo(AuctionPhase.Settlement));
            Assert.That(playtest.CurrentView.AvailableAssets, Is.EqualTo(125));
            Assert.That(playtest.CurrentView.RoundReveal.Seats.Count(item => item.Bid == 0), Is.EqualTo(3));

            playtest.AdvanceTime(TimeSpan.FromSeconds(3));

            Assert.That(playtest.CurrentView.Phase, Is.EqualTo(AuctionPhase.Analysis));
            Assert.That(playtest.CurrentView.AvailableAssets, Is.EqualTo(125));

            playtest.Reset();

            Assert.That(playtest.CurrentView.Phase, Is.EqualTo(AuctionPhase.Analysis));
            Assert.That(playtest.CurrentView.AvailableAssets, Is.EqualTo(100));
        }
    }
}
