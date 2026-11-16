using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    [TestFixture]
    public sealed class StatusVfxSourceTests
    {
        [TearDown] public void ResetContent() => GameContentProvider.Reset();

        private static BlessingCombatState Create(params string[] ids)
        {
            var acquired = new List<BlessingDefinition>();
            foreach (string id in ids)
            {
                Assert.That(BlessingMvpCatalog.TryGet(id, out var entry), Is.True, id);
                acquired.Add(entry.Definition);
            }
            var state = new BlessingCombatState(acquired);
            state.Tick(1f, 0f, true, false);
            return state;
        }

        [Test]
        public void ShieldProjectionRetainsSourceAndDoesNotMutateDamageState()
        {
            var state = Create("safe-asset-t1", "spare-chip-t1");
            state.Tick(10f, 9f, true, false);
            state.BeginAttack(10f, WeaponTag.Sword);
            float total = state.Shield;
            Assert.That(total, Is.GreaterThan(0f));
            Assert.That(state.ShieldAmountFor("safe-asset"), Is.EqualTo(total));
            Assert.That(state.ShieldAmountFor("spare-chip"), Is.Zero);
            Assert.That(state.ShieldAmountFor("unknown"), Is.Zero);
            Assert.That(state.ShieldRemainingFor("unknown"), Is.Zero);
            Assert.That(state.SourceBlessingIdFor("safe-asset"), Is.EqualTo("safe-asset-t1"));
            Assert.That(state.SourceBlessingIdFor("unknown"), Is.Empty);
            Assert.That(state.Shield, Is.EqualTo(total));
            state.Tick(100f, 99f, true, false);
            Assert.That(state.ShieldAmountFor("safe-asset"), Is.Zero);
            Assert.That(state.ShieldRemainingFor("safe-asset"), Is.Zero);
        }

        [Test]
        public void FourShieldSourcesCoexistAndExpireIndependently()
        {
            var state = Create("safe-asset-t1", "spare-chip-t1", "insurance-t2", "parry-dividend-t3");
            state.Tick(10f, 9f, true, false);
            state.BeginAttack(10f, WeaponTag.Sword);
            state.AfterDamage(10.1f, 40f, 40f, 100f, true);
            Assert.That(state.NotifyDashEvade(10.2f), Is.True);
            state.Tick(11.2f, 10.2f, true, false);
            string[] effects = { "safe-asset", "spare-chip", "insurance", "parry-dividend" };
            float sum = 0f;
            foreach (var effect in effects)
            {
                Assert.That(state.ShieldAmountFor(effect), Is.GreaterThan(0), effect);
                sum += state.ShieldAmountFor(effect);
            }
            Assert.That(sum, Is.EqualTo(state.Shield).Within(.001f));
            float firstExpiry = float.PositiveInfinity;
            string firstSource = null;
            foreach (var effect in effects)
                if (state.ShieldRemainingFor(effect) < firstExpiry)
                { firstExpiry = state.ShieldRemainingFor(effect); firstSource = effect; }
            state.Tick(11.2f + firstExpiry + .01f, 10.2f + firstExpiry + .01f, true, false);
            Assert.That(state.ShieldAmountFor(firstSource), Is.Zero);
            Assert.That(state.Shield, Is.GreaterThan(0f), "Later shield sources must survive first expiry");
        }

        [Test]
        public void ShieldAbsorptionReportsOnlyConsumedSourcesAndClearsOnNextHitAndReset()
        {
            var state = Create("safe-asset-t1", "spare-chip-t1", "insurance-t2", "parry-dividend-t3");
            state.Tick(10f, 9f, true, false);
            state.BeginAttack(10f, WeaponTag.Sword);
            state.AfterDamage(10.1f, 40f, 40f, 100f, true);
            state.NotifyDashEvade(10.2f);
            state.Tick(11.2f, 10.2f, true, false);
            string[] effects = { "safe-asset", "spare-chip", "insurance", "parry-dividend" };
            var amounts = new float[effects.Length];
            for (int i = 0; i < effects.Length; i++) amounts[i] = state.ShieldAmountFor(effects[i]);
            float before = state.Shield;
            state.BeforeDamage(11.2f, 1f, 100f, 100f, false, 0f, true, false);
            float sum = 0;
            for (int i = 0; i < effects.Length; i++)
            {
                float absorbed = state.LastHitAbsorbedByShieldFor(effects[i]);
                Assert.That(absorbed, Is.EqualTo(amounts[i] - state.ShieldAmountFor(effects[i])).Within(.001f));
                sum += absorbed;
            }
            Assert.That(sum, Is.EqualTo(1f).Within(.001f));
            Assert.That(state.Shield, Is.EqualTo(before - 1f).Within(.001f));
            state.BeforeDamage(11.2f, float.NaN, 100f, 100f, false, 0f, true, false);
            foreach (string effect in effects) Assert.That(state.LastHitAbsorbedByShieldFor(effect), Is.Zero);
            state.BeforeDamage(11.2f, 100000f, 100f, 100f, false, 0f, true, false);
            sum = 0;
            foreach (string effect in effects) sum += state.LastHitAbsorbedByShieldFor(effect);
            Assert.That(sum, Is.EqualTo(before - 1f).Within(.001f));
            Assert.That(state.Shield, Is.Zero);
            state.ResetRound(11.2f);
            foreach (string effect in effects) Assert.That(state.LastHitAbsorbedByShieldFor(effect), Is.Zero);
            Assert.That(state.LastHitAbsorbedByShieldFor("unknown"), Is.Zero);
        }

        [Test]
        public void ResetClearsSourceShieldProjection()
        {
            var state = Create("safe-asset-t1");
            state.Tick(10f, 9f, true, false);
            state.BeginAttack(10f, WeaponTag.Sword);
            Assert.That(state.ShieldAmountFor("safe-asset"), Is.GreaterThan(0));
            state.ResetRound(11f);
            Assert.That(state.ShieldAmountFor("safe-asset"), Is.Zero);
            Assert.That(state.ShieldRemainingFor("safe-asset"), Is.Zero);
        }

        [Test]
        public void TokenTossSignalRequiresActualEligibleHitAndRespectsCooldown()
        {
            var state = Create("token-toss-t1");
            var bow = state.BeginAttack(1f, WeaponTag.Bow);
            Assert.That(state.ConfirmHit(bow, 2, 1f, 0f, 0f).TokenTossTriggered, Is.False);
            var sword = state.BeginAttack(2f, WeaponTag.Sword);
            Assert.That(state.ConfirmHit(sword, 3, 2f, 0f, 0f).TokenTossTriggered, Is.True);
            var second = state.BeginAttack(2.1f, WeaponTag.Sword);
            Assert.That(state.ConfirmHit(second, 4, 2.1f, 0f, 0f).TokenTossTriggered, Is.False);
            state.ResetRound(3f);
            Assert.That(state.ConfirmHit(sword, 5, 3f, 0f, 0f).TokenTossTriggered, Is.False);
        }
    }
}
