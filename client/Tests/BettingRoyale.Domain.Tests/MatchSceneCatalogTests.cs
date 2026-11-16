using System;
using BettingRoyale.Data.Scenes;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    [TestFixture]
    public sealed class MatchSceneCatalogTests
    {
        [Test]
        public void MvpDefaultKeysStayIndependentFromBuiltInSceneNames()
        {
            Assert.That(MatchSceneCatalog.DefaultPreparation01.Value, Is.EqualTo("map/default/preparation/01"));
            Assert.That(MatchSceneCatalog.DefaultCombat01.Value, Is.EqualTo("map/default/combat/01"));
            Assert.That(MatchSceneCatalog.DefaultCombat02.Value, Is.EqualTo("map/default/combat/02"));
            Assert.That(MatchSceneCatalog.DefaultCombat, Is.EqualTo(MatchSceneCatalog.DefaultCombat02));

            Assert.That(
                MatchSceneCatalog.TryGet(
                    MatchSceneCatalog.DefaultPreparation01,
                    out MatchSceneDescriptor preparation),
                Is.True);
            Assert.That(preparation.BuiltInSceneName, Is.EqualTo("Map_Default_Preparation_01"));
        }

        [Test]
        public void ProductCombatRouteResolvesCombat02AndNeverFallsBackToCombat01()
        {
            Assert.That(MatchSceneCatalog.DefaultCombat, Is.EqualTo(MatchSceneCatalog.DefaultCombat02));
            Assert.That(MatchSceneCatalog.DefaultCombat, Is.Not.EqualTo(MatchSceneCatalog.DefaultCombat01));
            Assert.That(MatchSceneCatalog.DefaultCombat.Value, Is.EqualTo("map/default/combat/02"));

            Assert.That(
                MatchSceneCatalog.TryGet(MatchSceneCatalog.DefaultCombat, out MatchSceneDescriptor combat),
                Is.True);
            Assert.That(combat.BuiltInSceneName, Is.EqualTo("Map_Default_Combat_02"));
            Assert.That(combat.Phase, Is.EqualTo(MatchScenePhase.Combat));
            Assert.That(combat.MapId, Is.EqualTo("default"));
        }

        [Test]
        public void Combat01StaysRegisteredButOnlyAsAnExplicitDevRegressionFixture()
        {
            Assert.That(MatchSceneCatalog.IsDevRegressionFixture(MatchSceneCatalog.DefaultCombat01), Is.True);
            Assert.That(MatchSceneCatalog.IsDevRegressionFixture(MatchSceneCatalog.DefaultPreparation01), Is.True);
            Assert.That(MatchSceneCatalog.IsDevRegressionFixture(MatchSceneCatalog.DefaultCombat), Is.False);
            Assert.That(MatchSceneCatalog.IsDevRegressionFixture(MatchSceneCatalog.DefaultPreparation), Is.False);
            Assert.That(
                MatchSceneCatalog.IsDevRegressionFixture(new MatchSceneKey("lab/combat/unknown")),
                Is.False);

            // fixture 키는 Dev Lab이 명시적으로 요청할 때만 쓰이므로 카탈로그에는 남아 있어야 한다.
            Assert.That(MatchSceneCatalog.TryGet(MatchSceneCatalog.DefaultCombat01, out MatchSceneDescriptor fixture), Is.True);
            Assert.That(fixture.BuiltInSceneName, Is.EqualTo("Map_Default_Combat_01"));
        }

        [Test]
        public void TrainingAcademyKeyIsNotPartOfTheProductMatchCatalog()
        {
            var trainingKey = new MatchSceneKey("lab/training/map_training_academy_01");

            Assert.That(MatchSceneCatalog.TryGet(trainingKey, out _), Is.False);
            foreach (MatchSceneDescriptor combat in MatchSceneCatalog.Combat)
            {
                Assert.That(combat.Key, Is.Not.EqualTo(trainingKey));
                Assert.That(combat.BuiltInSceneName, Does.Not.Contain("Training"));
            }
        }

        [Test]
        public void RoyalColosseumIsAdditionalPreparationMapAndNeverACombatChoice()
        {
            var key = new MatchSceneKey("map/default/preparation/02");
            Assert.That(MatchSceneCatalog.TryGet(key, out MatchSceneDescriptor map), Is.True);
            Assert.That(map.BuiltInSceneName, Is.EqualTo("Map_Default_Preparation_02"));
            Assert.That(map.Phase, Is.EqualTo(MatchScenePhase.Preparation));
            Assert.That(MatchSceneCatalog.All[0].Key, Is.EqualTo(MatchSceneCatalog.DefaultPreparation01));
            foreach (MatchSceneDescriptor combat in MatchSceneCatalog.Combat)
                Assert.That(combat.Key, Is.Not.EqualTo(key));
        }

        [Test]
        public void DefaultPreparationRoutesToRoyalColosseumWhileLegacyKeyRemainsAvailable()
        {
            Assert.That(MatchSceneCatalog.DefaultPreparation, Is.EqualTo(MatchSceneCatalog.DefaultPreparation02));
            Assert.That(MatchSceneCatalog.TryGet(MatchSceneCatalog.DefaultPreparation, out var selected), Is.True);
            Assert.That(selected.BuiltInSceneName, Is.EqualTo("Map_Default_Preparation_02"));
            Assert.That(MatchSceneCatalog.TryGet(MatchSceneCatalog.DefaultPreparation01, out var legacy), Is.True);
            Assert.That(legacy.BuiltInSceneName, Is.EqualTo("Map_Default_Preparation_01"));
        }

        [Test]
        public void DefaultMapSelectionRequiresMatchingRegisteredPreparationAndCombatPair()
        {
            Assert.That(
                MatchSceneCatalog.IsValidSelection(
                    MatchSceneCatalog.DefaultPreparation,
                    MatchSceneCatalog.DefaultCombat),
                Is.True);
            Assert.That(
                MatchSceneCatalog.IsValidSelection(
                    MatchSceneCatalog.DefaultPreparation,
                    MatchSceneCatalog.DefaultPreparation01),
                Is.False,
                "서로 다른 저작 맵 묶음은 Host가 매치 맵으로 확정할 수 없어야 합니다.");
            Assert.That(
                MatchSceneCatalog.TryGetForPhase(
                    MatchSceneCatalog.DefaultCombat,
                    MatchScenePhase.Preparation,
                    out _),
                Is.False);
        }

        [TestCase("map/default/preparation/01")]
        [TestCase("map/christmas/combat/12")]
        [TestCase("theme_2/map-a")]
        public void StableSceneKeyAcceptsCanonicalValues(string value)
        {
            Assert.That(MatchSceneKey.TryCreate(value, out MatchSceneKey key), Is.True);
            Assert.That(key.Value, Is.EqualTo(value));
        }

        [TestCase("")]
        [TestCase("Map/Default/Combat/01")]
        [TestCase("/map/default/combat/01")]
        [TestCase("map//default")]
        [TestCase("map forest")]
        public void StableSceneKeyRejectsUnstableValues(string value)
        {
            Assert.That(MatchSceneKey.TryCreate(value, out _), Is.False);
            Assert.That(() => new MatchSceneKey(value), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void SceneKeysUseOrdinalValueEquality()
        {
            var left = new MatchSceneKey("map/default/combat/01");
            var right = new MatchSceneKey("map/default/combat/01");

            Assert.That(left, Is.EqualTo(right));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }
    }
}
