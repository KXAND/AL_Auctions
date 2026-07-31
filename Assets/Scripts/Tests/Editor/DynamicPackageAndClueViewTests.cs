using System.Collections.Generic;
using System.Linq;
using AuctionGame.Gameplay;
using NUnit.Framework;

namespace AuctionGame.Tests
{
    [TestFixture]
    public sealed class DynamicPackageAndClueViewTests
    {
        [Test]
        public void DynamicPackageHonoursConstraintsAndSharedCluesKeepResultsPrivate()
        {
            var catalogue = new[]
            {
                new CollectibleDefinition("bronze", "青铜徽章", "bronze-badge", 1, 1, CollectibleRarity.N, 10),
                new CollectibleDefinition("silver", "白银奖杯", "silver-trophy", 1, 1, CollectibleRarity.R, 20),
                new CollectibleDefinition("jade", "青玉印章", "jade-seal", 1, 2, CollectibleRarity.SR, 30)
            };
            var rules = AuctionRules.CreateWithContent(
                playerCount: 4,
                initialAssets: 100,
                gridWidth: 4,
                packageItemCount: 3,
                catalogue: catalogue,
                packageConstraints: new PackageConstraints(
                    new IntRange(60, 60),
                    new Dictionary<CollectibleRarity, IntRange>
                    {
                        [CollectibleRarity.N] = new IntRange(10, 10),
                        [CollectibleRarity.R] = new IntRange(20, 20),
                        [CollectibleRarity.SR] = new IntRange(30, 30)
                    }),
                candidateClueCount: 4);
            var random = new SequenceRandomSource(0, 1, 2);
            var package = new PackageGenerator(rules, random).Generate();

            Assert.That(package.TotalValue, Is.EqualTo(60));
            Assert.That(package.Items.Select(item => item.Definition.Id), Is.EquivalentTo(new[] { "bronze", "silver", "jade" }));
            Assert.That(package.Layout.Width, Is.EqualTo(4));
            Assert.That(package.Layout.Height, Is.GreaterThan(0));
            AssertLayoutHasNoOverlapsOrColumnHoles(package.Layout);

            var match = AuctionMatch.Create(rules, new SequenceRandomSource(0, 1, 2));
            var firstHumanSlot = match.ConnectHuman("first-human");
            var secondHumanSlot = match.ConnectHuman("second-human");
            match.Start();

            var firstView = match.GetSeatView(firstHumanSlot);
            var secondView = match.GetSeatView(secondHumanSlot);
            Assert.That(firstView.Grid.Width, Is.EqualTo(4));
            Assert.That(firstView.Grid.KnownItems, Is.Empty);

            var rarityShapeChoice = firstView.PrivateClueChoices.Single(choice => choice.Id == "rarity-SR");
            match.SelectPrivateClue(firstHumanSlot, rarityShapeChoice.Id);
            match.SelectPrivateClue(secondHumanSlot, rarityShapeChoice.Id);

            var firstResult = match.GetSeatView(firstHumanSlot).PrivateClueResult;
            Assert.That(firstResult.Knowledge, Is.Not.Empty);
            Assert.That(firstResult.Knowledge.All(item => item.Name == null && item.Value == null), Is.True);
            Assert.That(match.GetSeatView(secondHumanSlot).PrivateClueResult.Knowledge, Is.Not.Empty);
        }

        private static void AssertLayoutHasNoOverlapsOrColumnHoles(PackageLayout layout)
        {
            var occupied = new HashSet<string>();
            foreach (var item in layout.Items)
            {
                for (var x = item.X; x < item.X + item.Width; x++)
                {
                    for (var y = item.Y; y < item.Y + item.Height; y++)
                    {
                        Assert.That(occupied.Add($"{x}:{y}"), Is.True);
                    }
                }
            }

            foreach (var cell in occupied)
            {
                var coordinates = cell.Split(':');
                var x = int.Parse(coordinates[0]);
                var y = int.Parse(coordinates[1]);
                for (var below = 0; below < y; below++)
                {
                    Assert.That(occupied, Does.Contain($"{x}:{below}"));
                }
            }
        }
    }
}
