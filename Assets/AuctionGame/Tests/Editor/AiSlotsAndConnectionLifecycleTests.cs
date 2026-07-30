using System;
using AuctionGame;
using NUnit.Framework;

namespace AuctionGame.Tests
{
    [TestFixture]
    public sealed class AiSlotsAndConnectionLifecycleTests
    {
        [Test]
        public void DisconnectReplacesTheSeatWithAiAndPreventsTheOldConnectionFromSettling()
        {
            var match = AuctionMatch.CreateDemo(AuctionRules.CreateDemo(4, 100));
            var human = match.ConnectHuman("old-connection");
            match.Start();

            Assert.That(match.IsAiControlled(1), Is.True);
            match.DisconnectHuman(human);

            Assert.That(match.IsAiControlled(human), Is.True);
            Assert.Throws<InvalidOperationException>(() => match.ConnectHuman("replacement"));
            Assert.That(new AiActionScheduler(new SequenceRandomSource(9_999)).Schedule(TimeSpan.FromSeconds(5), true), Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(new SimpleRuleAi().ChoosePrivateClue(match.GetSeatView(human)), Is.Not.Null);
        }
    }
}
