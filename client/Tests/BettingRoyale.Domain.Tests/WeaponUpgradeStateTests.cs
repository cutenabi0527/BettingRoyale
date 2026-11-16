using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    [TestFixture]
    public class WeaponUpgradeStateTests
    {
        [Test]
        public void 성공하면_비용을_차감하고_단계와_공격력_보너스를_올린다()
        {
            var state = CreateState(new WeaponUpgradeLevelData(1, 100, 100, 10));
            var wallet = new Wallet(100);

            var result = state.TryUpgrade(wallet);

            Assert.That(result, Is.EqualTo(WeaponUpgradeAttemptResult.Success));
            Assert.That(wallet.Gold, Is.EqualTo(0));
            Assert.That(state.Level, Is.EqualTo(1));
            Assert.That(state.AttackPowerBonusPercent, Is.EqualTo(10));
        }

        [Test]
        public void 실패해도_비용은_차감하고_단계는_유지한다()
        {
            var state = CreateState(new WeaponUpgradeLevelData(1, 100, 0, 10));
            var wallet = new Wallet(100);

            var result = state.TryUpgrade(wallet);

            Assert.That(result, Is.EqualTo(WeaponUpgradeAttemptResult.Failed));
            Assert.That(wallet.Gold, Is.EqualTo(0));
            Assert.That(state.Level, Is.EqualTo(0));
        }

        [Test]
        public void 골드가_부족하면_판정하지_않고_상태와_잔액을_유지한다()
        {
            var state = CreateState(new WeaponUpgradeLevelData(1, 101, 100, 10));
            var wallet = new Wallet(100);

            var result = state.TryUpgrade(wallet);

            Assert.That(result, Is.EqualTo(WeaponUpgradeAttemptResult.NotEnoughGold));
            Assert.That(wallet.Gold, Is.EqualTo(100));
            Assert.That(state.Level, Is.EqualTo(0));
        }

        [Test]
        public void 최대_단계에서는_추가_비용을_차감하지_않는다()
        {
            var state = CreateState(new WeaponUpgradeLevelData(1, 100, 100, 10));
            var wallet = new Wallet(300);
            Assert.That(state.TryUpgrade(wallet), Is.EqualTo(WeaponUpgradeAttemptResult.Success));

            var result = state.TryUpgrade(wallet);

            Assert.That(result, Is.EqualTo(WeaponUpgradeAttemptResult.MaxLevelReached));
            Assert.That(wallet.Gold, Is.EqualTo(200));
            Assert.That(state.Level, Is.EqualTo(1));
        }

        [Test]
        public void 전투_라운드가_끝나면_0강과_보너스_0으로_초기화한다()
        {
            var state = CreateState(new WeaponUpgradeLevelData(1, 100, 100, 10));
            var wallet = new Wallet(100);
            state.TryUpgrade(wallet);

            state.ResetAfterCombatRound();

            Assert.That(state.Level, Is.EqualTo(0));
            Assert.That(state.AttackPowerBonusPercent, Is.EqualTo(0));
        }

        private static WeaponUpgradeState CreateState(params WeaponUpgradeLevelData[] rows) =>
            new(new WeaponUpgradeTable(rows), new DeterministicRandom(1234));
    }
}
