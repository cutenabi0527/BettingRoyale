using System;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    [TestFixture]
    public class WeaponUpgradeDataProviderTests
    {
        [TestCase(1, 100, 100, 10)]
        [TestCase(2, 200, 70, 25)]
        [TestCase(3, 400, 40, 50)]
        public void 목업_공급자는_승인된_단계별_데이터를_반환한다(
            int targetLevel,
            int expectedCostGold,
            int expectedSuccessRatePercent,
            int expectedAttackPowerBonusPercent)
        {
            IWeaponUpgradeDataProvider provider = new MockWeaponUpgradeDataProvider();

            var found = provider.TryGetTargetLevel(targetLevel, out var levelData);

            Assert.That(found, Is.True);
            Assert.That(levelData.TargetLevel, Is.EqualTo(targetLevel));
            Assert.That(levelData.CostGold, Is.EqualTo(expectedCostGold));
            Assert.That(levelData.SuccessRatePercent, Is.EqualTo(expectedSuccessRatePercent));
            Assert.That(levelData.AttackPowerBonusPercent, Is.EqualTo(expectedAttackPowerBonusPercent));
        }

        [TestCase(0)]
        [TestCase(4)]
        public void 정의되지_않은_단계는_조회되지_않는다(int targetLevel)
        {
            IWeaponUpgradeDataProvider provider = new MockWeaponUpgradeDataProvider();

            var found = provider.TryGetTargetLevel(targetLevel, out var levelData);

            Assert.That(found, Is.False);
            Assert.That(levelData, Is.EqualTo(default(WeaponUpgradeLevelData)));
        }

        [Test]
        public void 강화_단계가_1부터_연속되지_않으면_테이블을_거절한다()
        {
            var rows = new[] { new WeaponUpgradeLevelData(2, 100, 100, 10) };

            Assert.That(() => new WeaponUpgradeTable(rows), Throws.ArgumentException);
        }

        [Test]
        public void 공격력_보너스가_이전_단계보다_낮으면_테이블을_거절한다()
        {
            var rows = new[]
            {
                new WeaponUpgradeLevelData(1, 100, 100, 20),
                new WeaponUpgradeLevelData(2, 200, 70, 10),
            };

            Assert.That(() => new WeaponUpgradeTable(rows), Throws.ArgumentException);
        }

        [TestCase(-1, 100, 10)]
        [TestCase(100, -1, 10)]
        [TestCase(100, 101, 10)]
        [TestCase(100, 100, -1)]
        public void 유효하지_않은_행_값을_거절한다(
            int costGold,
            int successRatePercent,
            int attackPowerBonusPercent)
        {
            Assert.That(
                () => new WeaponUpgradeLevelData(1, costGold, successRatePercent, attackPowerBonusPercent),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void 소비자는_공급자_구현과_무관하게_같은_계약으로_조회한다()
        {
            IWeaponUpgradeDataProvider provider = new WeaponUpgradeTable(new[]
            {
                new WeaponUpgradeLevelData(1, 7, 55, 13),
            });

            Assert.That(provider.TryGetTargetLevel(1, out var levelData), Is.True);
            Assert.That(levelData.CostGold, Is.EqualTo(7));
            Assert.That(levelData.SuccessRatePercent, Is.EqualTo(55));
            Assert.That(levelData.AttackPowerBonusPercent, Is.EqualTo(13));
        }
    }
}
