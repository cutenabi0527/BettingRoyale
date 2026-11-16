using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 소모품(물약)의 스탯 효과는 축복·강화·장신구와 <b>합연산</b>이다 — 공격 속도 패널티만 곱연산 (S-GD-EM82G5 · S-GD-SH74SC).
    /// </summary>
    /// <remarks>
    /// 식은 <c>기본 × (1 + Σ보너스) × Π패널티</c> 하나다. 시트 밖에서 오는 물약 보너스는 시트 값에 <c>보너스 × Π</c> 를 더해
    /// 그 Σ 안에 넣는다 — 이 파일은 "시트에 같은 값을 넣은 것과 결과가 같다" 를 직접 비교한다.
    /// </remarks>
    [TestFixture]
    public class ConsumableAdditiveStackingTests
    {
        private const int AlicePlayer = 1;
        private const int BobPlayer = 2;
        private static readonly CombatantId Alice = new CombatantId(20_001);
        private static readonly CombatantId Bob = new CombatantId(20_002);

        private static StatRegistry Registry() => new StatRegistry(new List<StatDefinition>
        {
            new StatDefinition(DamageStatKeys.AttackPower, "공격력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.DamageBonus, "피해 증가", 0f, StatKind.Flat),
            new StatDefinition(DamageStatKeys.CritDamage, "치명타 배율", 2f, StatKind.Flat),
            new StatDefinition(DamageStatKeys.ArmorPen, "방어력 관통", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.Lifesteal, "흡혈", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.Defense, "방어력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.DamageTakenMult, "받는 피해", 1f, StatKind.Multiplier, min: 0f),
            new StatDefinition(DamageStatKeys.DamageTakenFixed, "받는 피해 고정", float.PositiveInfinity, StatKind.Min),
            new StatDefinition(DamageStatKeys.DamageTakenCap, "받는 피해 상한", float.PositiveInfinity, StatKind.Min),
            new StatDefinition(UpgradeStatKeys.AttackSpeed, "공격 속도", 1f, StatKind.Multiplier),
        });

        private sealed class Map : ICombatantPlayerMap
        {
            public bool TryGetPlayerId(CombatantId combatant, out int playerId)
            {
                playerId = combatant == Alice ? AlicePlayer : combatant == Bob ? BobPlayer : -1;
                return playerId > 0;
            }
        }

        private StatRegistry registry;
        private DamagePipeline pipeline;
        private BlessingCombatResolver resolver;
        private StatSheet bobSheet;

        [SetUp]
        public void SetUp()
        {
            registry = Registry();
            pipeline = new DamagePipeline(minDamageFloor: 0f);
            resolver = new BlessingCombatResolver(pipeline, new Map(), registry);
            resolver.SetSheet(AlicePlayer, new StatSheet(registry));
            bobSheet = new StatSheet(registry);
            resolver.SetSheet(BobPlayer, bobSheet);
        }

        private StatSheet Attacker(float attackPower)
        {
            var sheet = new StatSheet(registry);
            sheet.SetBase(DamageStatKeys.AttackPower, attackPower);
            return sheet;
        }

        // ── 받는 피해(철벽) ────────────────────────────────────────────────

        [Test]
        public void 철벽은_시트의_받는_피해_감소와_더해진다()
        {
            // 강화 세트 −5% + 철벽 −25% = −30% → ×0.70. 곱하면 0.95 × 0.75 = ×0.7125 다.
            var target = new StatSheet(registry);
            target.Add(DamageStatKeys.DamageTakenMult, -0.05f, StatStackMode.Add);

            DamageResult result = pipeline.Resolve(Attacker(100f), target,
                DamageRequest.Attack(damageTakenBonus: -0.25f), default);

            Assert.That(result.Amount, Is.EqualTo(70f).Within(0.001f));
        }

        [Test]
        public void 시트에_같은_감소를_넣은_것과_결과가_같다()
        {
            var withPotion = new StatSheet(registry);
            withPotion.Add(DamageStatKeys.DamageTakenMult, -0.05f, StatStackMode.Add);
            withPotion.Add(DamageStatKeys.DamageTakenMult, 1.2f, StatStackMode.Multiply);   // 곱연산 몫이 있어도
            var inSheet = new StatSheet(registry);
            inSheet.Add(DamageStatKeys.DamageTakenMult, -0.05f, StatStackMode.Add);
            inSheet.Add(DamageStatKeys.DamageTakenMult, -0.25f, StatStackMode.Add);
            inSheet.Add(DamageStatKeys.DamageTakenMult, 1.2f, StatStackMode.Multiply);

            float potion = pipeline.Resolve(Attacker(100f), withPotion,
                DamageRequest.Attack(damageTakenBonus: -0.25f), default).Amount;
            float sheet = pipeline.Resolve(Attacker(100f), inSheet, DamageRequest.Attack(), default).Amount;

            Assert.That(potion, Is.EqualTo(sheet).Within(0.001f), "(1 − 0.05 − 0.25) × 1.2 = 0.84 로 같아야 한다.");
            Assert.That(potion, Is.EqualTo(84f).Within(0.001f));
        }

        [Test]
        public void 받는_피해_감소는_방어력을_빼기_전에_합산에_들어간다()
        {
            // 공용 계산식 4단계(받는 피해) → 5단계(방어력). 27 × 0.75 − 7 = 13.25. 뒤에서 곱하면 (27 − 7) × 0.75 = 15.
            var target = new StatSheet(registry);
            target.Add(DamageStatKeys.Defense, 7f, StatStackMode.Add);

            float amount = pipeline.Resolve(Attacker(27f), target,
                DamageRequest.Attack(damageTakenBonus: -0.25f), default).Amount;

            Assert.That(amount, Is.EqualTo(13.25f).Within(0.001f));
        }

        [Test]
        public void 해석기는_주무기와_축복_피해_모두에_받는_피해_증감분을_싣는다()
        {
            bobSheet.Add(DamageStatKeys.DamageTakenMult, -0.05f, StatStackMode.Add);

            float weapon = resolver.ResolveDamage(Bob,
                new DamageInfo(Alice, WeaponType.Sword, 100f, damageTakenBonus: -0.25f));
            float blessing = resolver.ResolveDamage(Bob,
                new DamageInfo(Alice, WeaponType.Sword, 40f, CombatDamageSource.Blessing, damageTakenBonus: -0.25f));

            Assert.That(weapon, Is.EqualTo(70f).Within(0.001f));
            Assert.That(blessing, Is.EqualTo(28f).Within(0.001f), "40 × (1 − 0.05 − 0.25)");
        }

        [Test]
        public void 자기장_같은_고정_피해는_받는_피해_감소를_시트도_철벽도_받지_않는다()
        {
            // 고정 피해는 계산식 1~6단계를 통째로 건너뛴다 — 무적 말고는 어떤 피해 감소도 받지 않는다 (S-GD-S9KNJM).
            bobSheet.Add(DamageStatKeys.DamageTakenMult, -0.05f, StatStackMode.Add);

            float zone = resolver.ResolveDamage(Bob,
                new DamageInfo(default, WeaponType.None, 20f, CombatDamageSource.Zone, damageTakenBonus: -0.25f));

            Assert.That(zone, Is.EqualTo(20f).Within(0.001f));
        }

        // ── 패널티 몫 ──────────────────────────────────────────────────────

        [Test]
        public void 패널티_몫은_배율_스탯의_곱연산_기여만이다()
        {
            var sheet = new StatSheet(registry);
            sheet.Add(UpgradeStatKeys.AttackSpeed, 0.3f, StatStackMode.Add);
            sheet.Add(UpgradeStatKeys.AttackSpeed, 0.7f, StatStackMode.Multiply);

            Assert.That(sheet.Get(UpgradeStatKeys.AttackSpeed), Is.EqualTo(0.91f).Within(0.0001f), "(1 + 0.3) × 0.7");
            Assert.That(sheet.PenaltyOf(UpgradeStatKeys.AttackSpeed), Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(sheet.PenaltyOf(DamageStatKeys.Defense), Is.EqualTo(1f), "배율 스탯이 아니면 1");
            Assert.That(sheet.PenaltyOf("not-a-stat"), Is.EqualTo(1f));

            // 광란 +30% 를 Σ 안에 넣은 값 = 시트 값 + 0.3 × Π
            Assert.That(sheet.Get(UpgradeStatKeys.AttackSpeed) + 0.3f * sheet.PenaltyOf(UpgradeStatKeys.AttackSpeed),
                Is.EqualTo((1f + 0.3f + 0.3f) * 0.7f).Within(0.0001f));
        }

        [Test]
        public void 해석기는_전투원의_패널티_몫을_준다()
        {
            bobSheet.Add(UpgradeStatKeys.AttackSpeed, 0.8f, StatStackMode.Multiply);

            Assert.That(resolver.PenaltyFor(Bob, UpgradeStatKeys.AttackSpeed), Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(resolver.PenaltyFor(Alice, UpgradeStatKeys.AttackSpeed), Is.EqualTo(1f));
            Assert.That(resolver.PenaltyFor(new CombatantId(99), UpgradeStatKeys.AttackSpeed), Is.EqualTo(1f), "시트 없음");
        }
    }
}
