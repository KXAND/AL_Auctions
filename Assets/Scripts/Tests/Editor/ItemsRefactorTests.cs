using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace AuctionGame.Tests
{
    public sealed class ItemsRefactorTests
    {
        private static readonly Assembly GameAssembly = typeof(Authority).Assembly;

        [Test]
        public void ItemTypesUseCanonicalNames()
        {
            Assert.That(GameAssembly.GetType("AuctionGame.ItemData"), Is.Not.Null);
            Assert.That(GameAssembly.GetType("AuctionGame.ItemRarity"), Is.Not.Null);
            Assert.That(GameAssembly.GetType("AuctionGame.ItemCatalog"), Is.Not.Null);
            Assert.That(GameAssembly.GetType("AuctionGame.VisibleItem"), Is.Not.Null);
            Assert.That(GameAssembly.GetType("AuctionGame.CollectibleCatalog"), Is.Null);
            Assert.That(GameAssembly.GetType("AuctionGame.VisibleCollectible"), Is.Null);
        }

        [Test]
        public void RemovedItemAbstractionsNoLongerExist()
        {
            string[] removedTypes =
            {
                "AuctionGame.IntRange",
                "AuctionGame.IRandomSource",
                "AuctionGame.SystemRandomSource",
                "AuctionGame.SequenceRandomSource",
                "AuctionGame.CollectibleKnownInformation",
                "AuctionGame.GeneratedPackage",
                "AuctionGame.PackageLayoutItem"
            };

            Assert.That(removedTypes.Where(typeName => GameAssembly.GetType(typeName) != null), Is.Empty);
        }

        [Test]
        public void PackageGeneratorOwnsItsRuntimeDataTypes()
        {
            Type generator = GameAssembly.GetType("AuctionGame.PackageGenerator");

            Assert.That(generator, Is.Not.Null);
            Assert.That(generator.GetNestedType("GeneratedPackage", BindingFlags.NonPublic), Is.Not.Null);
            Assert.That(generator.GetNestedType("PackageLayoutItem", BindingFlags.NonPublic), Is.Not.Null);
        }

        [Test]
        public void RuntimeRandomConsumersUseSystemRandomDirectly()
        {
            Type itemCatalog = GameAssembly.GetType("AuctionGame.ItemCatalog");

            Assert.That(
                typeof(Authority).GetConstructor(new[] { itemCatalog, typeof(ClueCatalog), typeof(Random) }),
                Is.Not.Null);
            Assert.That(
                typeof(AIController).GetConstructor(new[] { typeof(AuctionManager), typeof(Random) }),
                Is.Not.Null);
            Assert.That(
                typeof(ClueCatalog).GetMethod(nameof(ClueCatalog.Pick), new[] { typeof(Random) }),
                Is.Not.Null);
            Assert.That(
                typeof(ClueCatalog).GetMethod(nameof(ClueCatalog.PickDistinct), new[] { typeof(Random), typeof(int) }),
                Is.Not.Null);
        }

        [Test]
        public void PackageConstraintsUseExplicitConstants()
        {
            Assert.That(GlobalSettings.PackageTotalValueMinimum, Is.EqualTo(90));
            Assert.That(GlobalSettings.PackageTotalValueMaximum, Is.EqualTo(110));
            Assert.That(GlobalSettings.PackageSsrValueMinimum, Is.EqualTo(90));
            Assert.That(GlobalSettings.PackageSsrValueMaximum, Is.EqualTo(110));
        }

        [Test]
        public void ItemCatalogExposesOnlyListAndIdLookup()
        {
            Type itemCatalog = GameAssembly.GetType("AuctionGame.ItemCatalog");
            string[] publicMethods = itemCatalog
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(method => method.Name)
                .ToArray();

            Assert.That(publicMethods, Is.EquivalentTo(new[]
            {
                "GetAllItems",
                "FindById"
            }));
        }

        [Test]
        public void GameplayMessagesUseCanonicalNames()
        {
            Assert.That(GameAssembly.GetType("AuctionGame.MatchPhase"), Is.Not.Null);
            Assert.That(GameAssembly.GetType("AuctionGame.SessionPhase"), Is.Null);
            Assert.That(GameAssembly.GetType("AuctionGame.RoundParticipantReveal"), Is.Not.Null);
            Assert.That(GameAssembly.GetType("AuctionGame.RoundSeatReveal"), Is.Null);

            Type roundReveal = GameAssembly.GetType("AuctionGame.RoundReveal");
            Type settlementView = GameAssembly.GetType("AuctionGame.SettlementView");
            Assert.That(roundReveal.GetProperty("WinnerAssetOwnerId"), Is.Not.Null);
            Assert.That(roundReveal.GetProperty("WinnerOwner"), Is.Null);
            Assert.That(settlementView.GetProperty("WinnerAssetOwnerId"), Is.Not.Null);
            Assert.That(settlementView.GetProperty("WinnerOwner"), Is.Null);
        }

        [Test]
        public void MatchParticipantDoesNotModelSeat()
        {
            Type participant = GameAssembly.GetType("AuctionGame.MatchParticipant");
            string[] constructorParameters = participant
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .Single()
                .GetParameters()
                .Select(parameter => parameter.Name)
                .ToArray();

            Assert.That(participant.GetProperty("SeatId"), Is.Null);
            Assert.That(constructorParameters, Is.EqualTo(new[]
            {
                "playerIdentity",
                "startingAssets",
                "isAI",
                "assetOwnerId"
            }));
        }

        [Test]
        public void CatalogButtonUsesCatalogTerminology()
        {
            Assert.That(typeof(OverlayManager).GetMethod("OnCatalogClicked"), Is.Not.Null);
            Assert.That(typeof(OverlayManager).GetMethod("OnCollectionClicked"), Is.Null);
        }
    }
}
