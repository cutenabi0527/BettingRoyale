using System;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복과 강화가 <b>같은 스탯 시트 하나</b>에 합쳐지는지 본다 (S-GD-A2YDDJ, S-CLI-VJ8HKS).
    /// 합성은 <see cref="BlessingRuntime.Rebuild"/> 의 콜백에서 일어나고, 소비 측은
    /// <see cref="BlessingRuntime.TryGetSheet"/> 로 발행된 시트를 읽는다.
    /// </summary>
    [TestFixture]
    public sealed class BlessingUpgradeRuntimeTests
    {
        private const int PlayerId = 3;
        private const float SwordBaseAttackPower = 25f;

        private StatRegistry registry;
        private BlessingCombatResolver resolver;
        private BlessingRuntime runtime;
        private UpgradeState upgrade;
        private Wallet wallet;

        [SetUp]
        public void SetUp()
        {
            registry = PlaceholderBlessingBalance.CreateStatRegistry();
            resolver = new BlessingCombatResolver(
                new DamagePipeline(0f), new NoPlayerMap(), registry);
            runtime = new BlessingRuntime(
                registry, PlaceholderBlessingBalance.CreateEffectCatalog(), resolver);
            upgrade = new UpgradeState(PlaceholderUpgradeCatalog.Create(), WeaponType.Sword, UpgradeSuccessRoll.AlwaysSucceeds);
            wallet = new Wallet(10000);
        }

        [Test]
        public void ApplyTo_SwordFiveAndArmorFive_AddsWeaponAndSetAttackPowerOnce()
        {
            UpgradeBothSlotsToFive();
            var sheet = new StatSheet(registry);
            sheet.SetBase(DamageStatKeys.AttackPower, SwordBaseAttackPower);

            upgrade.ApplyTo(sheet);

            // 검 5강 +16 과 세트 5강 +3 만 얹는다(엑셀, S15P21A606-304). 25 → 44.
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(44f).Within(0.0001f));

            // 피해 증가는 공격력과 다른 스탯이다 — 44 안에 섞여 들어가면 안 된다. 세트 3% + 3% + 4%.
            Assert.That(sheet.Get(DamageStatKeys.DamageBonus), Is.EqualTo(0.10f).Within(0.0001f));
        }

        [Test]
        public void Rebuild_ComposesUpgradeAfterBlessing_StacksAdditivelyOnOneSheet()
        {
            UpgradeBothSlotsToFive();

            runtime.Rebuild(PlayerId, new[] { AttackBlessing(), DefenseBlessing(), GuardianBlessing() }, Compose);

            Assert.That(runtime.TryGetSheet(PlayerId, out StatSheet sheet), Is.True);

            // 무기 기준 25 + 검 5강 16 + 세트 3 + 전투 본능 t3 16
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(60f).Within(0.0001f));

            // 방어구 5강 6 + 세트 2 + 강철 심장 t1 5 + 수호의 맹세 t1 8 (S-GD-2TZT7S)
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(21f).Within(0.0001f));
        }

        [Test]
        public void Rebuild_CalledTwiceWithSameInput_DoesNotDoubleAdd()
        {
            UpgradeBothSlotsToFive();
            var acquired = new[] { AttackBlessing(), DefenseBlessing() };

            runtime.Rebuild(PlayerId, acquired, Compose);
            runtime.Rebuild(PlayerId, acquired, Compose);

            Assert.That(runtime.TryGetSheet(PlayerId, out StatSheet sheet), Is.True);
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(60f).Within(0.0001f));
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(13f).Within(0.0001f));
        }

        [Test]
        public void Rebuild_AfterUpgradeReset_DropsUpgradeContributionAndKeepsBlessing()
        {
            UpgradeBothSlotsToFive();
            var acquired = new[] { AttackBlessing(), DefenseBlessing() };
            runtime.Rebuild(PlayerId, acquired, Compose);

            upgrade.ResetForNextRound();
            runtime.Rebuild(PlayerId, acquired, Compose);

            Assert.That(runtime.TryGetSheet(PlayerId, out StatSheet sheet), Is.True);
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(41f).Within(0.0001f));
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(5f).Within(0.0001f));
            Assert.That(sheet.Get(UpgradeStatKeys.MaxHp), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(sheet.Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(sheet.Get(UpgradeStatKeys.AttackSpeed), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Rebuild_PublishedSheet_CarriesMaxHpAndSpeedForNonDamageConsumers()
        {
            UpgradeBothSlotsToFive();

            runtime.Rebuild(PlayerId, new[] { AttackBlessing() }, Compose);

            Assert.That(runtime.TryGetSheet(PlayerId, out StatSheet sheet), Is.True);

            // 최대 체력 기준선은 전투원의 직렬화 값이라 시트는 증가분만 들고 있다.
            Assert.That(sheet.Get(UpgradeStatKeys.MaxHp), Is.EqualTo(65f).Within(0.0001f));

            // 배율은 1 × (1 + Σ) 다. 세트 1·3·5강 이동 속도 0.10, 검 5강 공속 0.25 (엑셀, S15P21A606-304).
            Assert.That(sheet.Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1.10f).Within(0.0001f));
            Assert.That(sheet.Get(UpgradeStatKeys.AttackSpeed), Is.EqualTo(1.25f).Within(0.0001f));
        }

        [Test]
        public void Rebuild_ComposeThrows_KeepsPreviouslyPublishedSheet()
        {
            UpgradeBothSlotsToFive();
            runtime.Rebuild(PlayerId, new[] { AttackBlessing() }, Compose);
            Assert.That(runtime.TryGetSheet(PlayerId, out StatSheet published), Is.True);

            Assert.Throws<InvalidOperationException>(
                () => runtime.Rebuild(
                    PlayerId,
                    new[] { AttackBlessing() },
                    _ => throw new InvalidOperationException("합성 실패")));

            // 반쪽 시트가 발행되지 않았다. 이전 결과가 그대로 살아 있어야 전투가 무보정으로 시작되지 않는다.
            Assert.That(runtime.TryGetSheet(PlayerId, out StatSheet afterThrow), Is.True);
            Assert.That(afterThrow, Is.SameAs(published));
            Assert.That(afterThrow.Get(DamageStatKeys.AttackPower), Is.EqualTo(60f).Within(0.0001f));
            Assert.That(resolver.HasSheet(PlayerId), Is.True);
        }

        [Test]
        public void Clear_RemovesPublishedSheet()
        {
            runtime.Rebuild(PlayerId, new[] { AttackBlessing() }, Compose);

            runtime.Clear();

            Assert.That(runtime.TryGetSheet(PlayerId, out StatSheet _), Is.False);
            Assert.That(runtime.Has(PlayerId), Is.False);
            Assert.That(resolver.HasSheet(PlayerId), Is.False);
        }

        [Test]
        public void Rebuild_WithoutCompose_StillPublishesBlessingOnlySheet()
        {
            runtime.Rebuild(PlayerId, new[] { DefenseBlessing() });

            Assert.That(runtime.TryGetSheet(PlayerId, out StatSheet sheet), Is.True);
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(5f).Within(0.0001f));
        }

        /// <summary>축복 뒤에 붙는 합성 단계. 무기 기준값 주입과 강화가 같은 시트로 들어간다.</summary>
        private void Compose(StatSheet sheet)
        {
            sheet.SetBase(DamageStatKeys.AttackPower, SwordBaseAttackPower);
            upgrade.ApplyTo(sheet);
        }

        private void UpgradeBothSlotsToFive()
        {
            for (int level = 0; level < 5; level++)
            {
                Assert.That(
                    upgrade.TryUpgrade(UpgradeSlot.Weapon, wallet), Is.EqualTo(UpgradeAttemptResult.Success));
                Assert.That(
                    upgrade.TryUpgrade(UpgradeSlot.Armor, wallet), Is.EqualTo(UpgradeAttemptResult.Success));
            }

            Assert.That(upgrade.SetLevel, Is.EqualTo(5));
        }

        private static BlessingDefinition AttackBlessing() =>
            new BlessingDefinition("combat-instinct-t3", "전투 본능", WeaponTag.Common, BlessingTier.Platinum);

        private static BlessingDefinition DefenseBlessing() =>
            new BlessingDefinition("steel-heart-t1", "강철 심장", WeaponTag.Common, BlessingTier.Silver);

        private static BlessingDefinition GuardianBlessing() =>
            new BlessingDefinition("guardians-oath-t1", "수호의 맹세", WeaponTag.Sword, BlessingTier.Silver);

        /// <summary>시트만 보는 시험이라 피해 계산을 부르지 않는다. 매핑은 비어 있어도 된다.</summary>
        private sealed class NoPlayerMap : ICombatantPlayerMap
        {
            public bool TryGetPlayerId(CombatantId combatant, out int playerId)
            {
                playerId = 0;
                return false;
            }
        }
    }
}
