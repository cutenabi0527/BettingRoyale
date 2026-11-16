using System;
using System.Globalization;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.LocalPlay;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public sealed class BlessingDescriptionTests
    {
        [Test]
        public void EveryOfferedTierHasANumericDescription()
        {
            Assert.That(LocalPlayBlessingCatalog.All.Count,
                Is.EqualTo(18 + BlessingMvpCatalog.GeneratedCount));
            foreach (var blessing in LocalPlayBlessingCatalog.All)
            {
                Assert.That(blessing.Description, Is.Not.Empty, blessing.Id);
                Assert.That(blessing.Description.Any(char.IsDigit), Is.True, blessing.Id);
                Assert.That(blessing.Description, Does.Not.Contain("설명이 들어갈 자리"));
            }
        }

        [Test]
        public void MvpCatalogHasTwentyUniqueDefinitionsPerTierAndExplicitMetadata()
        {
            Assert.That(BlessingMvpCatalog.All.Count, Is.EqualTo(BlessingMvpCatalog.GeneratedCount));
            Assert.That(BlessingMvpCatalog.All.Select(x => x.Definition.Id).Distinct().Count(),
                Is.EqualTo(BlessingMvpCatalog.All.Count));
            Assert.That(BlessingMvpCatalog.All.Select(x => x.Definition.DisplayName).Distinct().Count(),
                Is.EqualTo(BlessingMvpCatalog.All.Count));

            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                Assert.That(BlessingMvpCatalog.All.Count(x => x.Definition.Tier == tier),
                    Is.GreaterThanOrEqualTo(BlessingMvpCatalog.MinimumPerTier), tier.ToString());
            }

            Assert.That(BlessingMvpCatalog.MissingArtDefinitions, Is.EqualTo(40));
            Assert.That(BlessingMvpCatalog.All.All(x => x.StackRule >= BlessingStackRule.Unique
                && x.StackRule <= BlessingStackRule.Independent), Is.True);
            Assert.That(BlessingMvpCatalog.All.All(x => x.DurationKind >= BlessingDurationKind.Instant
                && x.DurationKind <= BlessingDurationKind.Active), Is.True);

            // 직접 눌러 쓰는 축복은 더블 다운·올인 둘뿐이고(S-GD-NJQ5NM §1), 정의의 액티브 표시와 늘 같이 간다.
            Assert.That(BlessingMvpCatalog.All.Where(x => x.DurationKind == BlessingDurationKind.Active)
                .Select(x => x.Definition.Id).OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[] { "all-in-t3", "double-down-t2" }));
            Assert.That(BlessingMvpCatalog.All.All(x =>
                x.Definition.IsActive == (x.DurationKind == BlessingDurationKind.Active)), Is.True);
        }

        [TestCase("quick-footwork-t1", "5%", "0.95배")]
        [TestCase("quick-footwork-t2", "10%", "0.9배")]
        [TestCase("quick-footwork-t3", "16%", "0.84배")]
        [TestCase("guardians-oath-t3", "+32", "+9%p")]
        [TestCase("precision-aim-t2", "+0.7", "치명타")]
        [TestCase("combat-instinct-t3", "+16", "소모품")]
        [TestCase("weak-point-analysis-t2", "+7", "초과 관통")]
        public void DescriptionExplainsTheActualUnits(string id, string value, string context)
        {
            string text = LocalPlayBlessingCatalog.All.Single(x => x.Id == id).Description;
            Assert.That(text, Does.Contain(value).And.Contain(context));
        }

        [Test]
        public void DescriptionUsesTheSuppliedEffectValuesEvenInCommaDecimalCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var rows = new[]
                {
                    new BlessingEffectRow("custom", DamageStatKeys.DamageTakenMult, "0.875", "MUL"),
                };
                string text = PlaceholderBlessingDescription.Create(rows);
                Assert.That(text, Does.Contain("12.5%").And.Contain("0.875배"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void UnsupportedEffectsCannotSilentlyBecomeMisleadingDescriptions()
        {
            Assert.Throws<ArgumentException>(() => PlaceholderBlessingDescription.Create(new[]
            {
                new BlessingEffectRow("custom", DamageStatKeys.Defense, "5", "MUL"),
            }));
            Assert.Throws<ArgumentException>(() => PlaceholderBlessingDescription.Create(new[]
            {
                new BlessingEffectRow("custom", DamageStatKeys.Defense, "5", "ADD", "HP_LT(50)"),
            }));
        }
    }
}
