using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 소모품이 <b>공용 피해 계산식</b>에 들어가는 자리 — 투지 +20% · 프로틴 공격력 +3 · 방어력 +7
    /// (S-GD-DDXPX9 §2 · S-GD-RVVB4H §6 · S-GD-ZESTQH).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 파일이 지키는 것.</b> 프로틴 쉐이크의 +3·+7 은 시트를 거치지 않지만 <b>장신구와 같은 단위</b>여야 한다 —
    /// 그래서 "시트에 같은 값을 넣은 것과 결과가 같다" 를 직접 비교한다. 곱하기 전에 더해지는지, 관통이 합친 방어력에서
    /// 빠지는지도 여기서 잡힌다.
    /// </para>
    /// <para>
    /// 무적·받는 피해 감소·아이템 보호막은 계산식 <b>뒤</b>(체력 컴포넌트)에서 걸린다 — EditMode 계약 테스트가 본다.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ConsumableCombatDamageTests
    {
        private const int AlicePlayer = 1;
        private const int BobPlayer = 2;
        private static readonly CombatantId Alice = new CombatantId(10_001);
        private static readonly CombatantId Bob = new CombatantId(10_002);

        private static StatRegistry Registry() => new StatRegistry(new List<StatDefinition>
        {
            new StatDefinition(DamageStatKeys.AttackPower, "공격력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.DamageBonus, "피해 증가", 0f, StatKind.Flat),
            new StatDefinition(DamageStatKeys.CritDamage, "치명타 배율", 2f, StatKind.Flat),
            new StatDefinition(DamageStatKeys.ArmorPen, "방어력 관통", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.Lifesteal, "흡혈", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.Defense, "방어력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.DamageTakenMult, "받는 피해", 1f, StatKind.Multiplier),
            new StatDefinition(DamageStatKeys.DamageTakenFixed, "받는 피해 고정", float.PositiveInfinity, StatKind.Min),
            new StatDefinition(DamageStatKeys.DamageTakenCap, "받는 피해 상한", float.PositiveInfinity, StatKind.Min),
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

        [SetUp]
        public void SetUp()
        {
            registry = Registry();
            pipeline = new DamagePipeline(minDamageFloor: 0f);
            resolver = new BlessingCombatResolver(pipeline, new Map(), registry);
            resolver.SetSheet(AlicePlayer, new StatSheet(registry));
            resolver.SetSheet(BobPlayer, new StatSheet(registry));
        }

        private StatSheet Attacker(float attackPower)
        {
            var sheet = new StatSheet(registry);
            sheet.SetBase(DamageStatKeys.AttackPower, attackPower);
            return sheet;
        }

        // ── 계산식 ─────────────────────────────────────────────────────────

        [Test]
        public void 공격력_보너스는_피해_증가를_곱하기_전에_더한다()
        {
            // (25 + 3) × (1 + 0.2) = 33.6. 곱한 뒤 더하면 33 이 된다.
            DamageResult result = pipeline.Resolve(Attacker(25f), new StatSheet(registry),
                DamageRequest.Attack(damageBonus: 0.2f, attackPowerBonus: 3f));

            Assert.That(result.Amount, Is.EqualTo(33.6f).Within(0.0001f));
        }

        [Test]
        public void 방어력_보너스는_마지막에_시트_방어력과_함께_뺀다()
        {
            var target = new StatSheet(registry);
            target.Add(DamageStatKeys.Defense, ShopItemEffectCatalog.AmuletDefense, StatStackMode.Add, "amulet");

            DamageResult result = pipeline.Resolve(Attacker(25f), target,
                DamageRequest.Attack(defenseBonus: ShopItemEffectCatalog.ProteinShakeDefense));

            Assert.That(result.Amount,
                Is.EqualTo(25f - ShopItemEffectCatalog.AmuletDefense - ShopItemEffectCatalog.ProteinShakeDefense).Within(0.0001f),
                "수호의 부적(+5, S-GD-2YQQG4)과 쉐이크(+7)를 합쳐 뺀다.");
        }

        [Test]
        public void 관통은_합친_방어력에서_뺀다()
        {
            // 시트 방어력 0 + 소모품 7 − 관통 5 = 2. 소모품 방어력만 따로 빼면 관통이 먹지 않는다.
            StatSheet attacker = Attacker(25f);
            attacker.Add(DamageStatKeys.ArmorPen, 5f, StatStackMode.Add, "pen");

            DamageResult result = pipeline.Resolve(attacker, new StatSheet(registry),
                DamageRequest.Attack(defenseBonus: 7f));

            Assert.That(result.Amount, Is.EqualTo(23f).Within(0.0001f));
        }

        [Test]
        public void 프로틴_쉐이크는_힘의_반지와_같은_고정_방어력을_낀_것과_같은_피해가_난다()
        {
            // S-GD-DDXPX9 §2 — 쉐이크는 장신구와 같은 단위(고정 수치)다. 단위가 어긋나면 이 비교가 깨진다.
            // 부적은 +5 로 내려가고 쉐이크는 +7 을 유지하므로(S-GD-2YQQG4) 값은 쉐이크와 같은 고정 방어력으로 견준다.
            StatSheet ringAttacker = Attacker(15f);
            ringAttacker.Add(DamageStatKeys.AttackPower, ShopItemEffectCatalog.RingRangedAttackPower, StatStackMode.Add, "ring");
            var amuletTarget = new StatSheet(registry);
            amuletTarget.Add(DamageStatKeys.Defense, ShopItemEffectCatalog.ProteinShakeDefense, StatStackMode.Add, "fixed-defense");

            float withAccessories = pipeline.Resolve(ringAttacker, amuletTarget,
                DamageRequest.Attack(damageBonus: 0.1f, isCritical: true)).Amount;
            float withShake = pipeline.Resolve(Attacker(15f), new StatSheet(registry),
                DamageRequest.Attack(isCritical: true, damageBonus: 0.1f,
                    attackPowerBonus: ShopItemEffectCatalog.ProteinShakeAttackPower,
                    defenseBonus: ShopItemEffectCatalog.ProteinShakeDefense)).Amount;

            Assert.That(withShake, Is.EqualTo(withAccessories).Within(0.0001f));
        }

        // ── 해석기 — DamageInfo 에서 계산식으로 ─────────────────────────────────

        [Test]
        public void 주무기_명중은_투지와_프로틴을_모두_싣는다()
        {
            var info = new DamageInfo(Alice, WeaponType.Sword, 25f, CombatDamageSource.Weapon,
                damageBonus: ShopItemEffectCatalog.FuryPotionDamageBonus,
                attackPowerBonus: ShopItemEffectCatalog.ProteinShakeAttackPower)
                .WithDefenseBonus(ShopItemEffectCatalog.ProteinShakeDefense);

            float damage = resolver.ResolveDamage(Bob, info);

            Assert.That(damage, Is.EqualTo((25f + 3f) * 1.2f - 7f).Within(0.0001f));
        }

        [Test]
        public void 축복_피해는_투지와_맞는_쪽_방어력만_싣고_공격력은_싣지_않는다()
        {
            // 시트 공격력(힘의 반지)도 축복 피해에는 들어가지 않는다 — 같은 단위의 프로틴도 같다.
            var info = new DamageInfo(Alice, WeaponType.Sword, 10f, CombatDamageSource.Blessing,
                damageBonus: 0.2f, attackPowerBonus: 3f).WithDefenseBonus(7f);

            Assert.That(resolver.ResolveDamage(Bob, info), Is.EqualTo(10f * 1.2f - 7f).Within(0.0001f));
        }

        [Test]
        public void 자기장_같은_고정_피해는_소모품_방어력을_무시한다()
        {
            // 고정 피해는 계산식 1~6단계를 통째로 건너뛴다. 무적·보호막만 계산식 뒤에서 따로 건다 (S-GD-S9KNJM).
            var zone = new DamageInfo(default, WeaponType.None, 10f, CombatDamageSource.Zone).WithDefenseBonus(7f);

            Assert.That(resolver.ResolveDamage(Bob, zone), Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void 비유한_소모품_값은_0으로_본다()
        {
            var info = new DamageInfo(Alice, WeaponType.Sword, 25f, attackPowerBonus: float.NaN)
                .WithDefenseBonus(float.PositiveInfinity);

            Assert.That(resolver.ResolveDamage(Bob, info), Is.EqualTo(25f).Within(0.0001f));
        }

        [Test]
        public void 방어력_보너스만_바꾼_사본은_나머지를_그대로_둔다()
        {
            var info = new DamageInfo(Alice, WeaponType.Bow, 15f, CombatDamageSource.Weapon, 0.2f, 0.9f, true,
                damageMultiplier: 1.5f, isJackpot: true, attackPowerBonus: 3f);

            DamageInfo copy = info.WithDefenseBonus(7f);

            Assert.That(copy.Attacker, Is.EqualTo(info.Attacker));
            Assert.That(copy.Weapon, Is.EqualTo(info.Weapon));
            Assert.That(copy.Amount, Is.EqualTo(info.Amount));
            Assert.That(copy.Source, Is.EqualTo(info.Source));
            Assert.That(copy.DamageBonus, Is.EqualTo(info.DamageBonus));
            Assert.That(copy.DamagePenaltyMultiplier, Is.EqualTo(info.DamagePenaltyMultiplier));
            Assert.That(copy.IsCritical, Is.EqualTo(info.IsCritical));
            Assert.That(copy.AttackPowerBonus, Is.EqualTo(3f));
            Assert.That(copy.DamageMultiplier, Is.EqualTo(1.5f));
            Assert.That(copy.IsJackpot, Is.True);
            Assert.That(copy.DefenseBonus, Is.EqualTo(7f));
        }
    }
}
