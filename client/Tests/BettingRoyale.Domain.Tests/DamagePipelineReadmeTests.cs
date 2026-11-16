using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using NUnit.Framework;
using CombatWeapon = BettingRoyale.Data.Combat.WeaponType;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 기획 엑셀 README "피해 계산 순서" 가 새로 못 박은 것들 (S-GD-PA3E9T).
    /// </summary>
    /// <remarks>
    /// 3-B 잭팟 · 배율은 (무기 + 축복) 공격력 전체에 · 고정 피해는 상한도 건너뜀 · 표식 사격은 고정 피해 ·
    /// 고정 피해엔 축복 감소가 없고 보호막만 · DOT·ZONE 은 피격 트리거를 돌리지 않음.
    /// </remarks>
    [TestFixture]
    public sealed class DamagePipelineReadmeTests
    {
        private static StatRegistry Registry() => new StatRegistry(new List<StatDefinition>
        {
            new StatDefinition(DamageStatKeys.AttackPower, "공격력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.DamageBonus, "피해 증가", 0f, StatKind.Flat),
            new StatDefinition(DamageStatKeys.CritDamage, "치명타 배율", 2f, StatKind.Flat),
            new StatDefinition(DamageStatKeys.JackpotMultiplier, "잭팟 배율", 1f, StatKind.Flat),
            new StatDefinition(DamageStatKeys.ArmorPen, "방어력 관통", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.Lifesteal, "흡혈", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.Defense, "방어력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.DamageTakenMult, "받는 피해", 1f, StatKind.Multiplier),
            new StatDefinition(DamageStatKeys.DamageTakenFixed, "받는 피해 고정", float.PositiveInfinity, StatKind.Min),
            new StatDefinition(DamageStatKeys.DamageTakenCap, "받는 피해 상한", float.PositiveInfinity, StatKind.Min),
        });

        private StatRegistry registry;
        private DamagePipeline pipeline;
        private StatSheet attacker;
        private StatSheet target;

        [SetUp]
        public void SetUp()
        {
            registry = Registry();
            pipeline = new DamagePipeline(0f);
            attacker = new StatSheet(registry);
            target = new StatSheet(registry);
            attacker.SetBase(DamageStatKeys.AttackPower, 25f);
            attacker.Add(DamageStatKeys.AttackPower, 5f, StatStackMode.Add, "combat-instinct-t1");
        }

        // ── 3-B 잭팟 ────────────────────────────────────────────────────

        [Test]
        public void 잭팟은_공격력_전체에_곱해지고_치명타와도_곱해진다()
        {
            attacker.Add(DamageStatKeys.JackpotMultiplier, 1.5f, StatStackMode.Add, "jackpot-stat");

            float plain = pipeline.Resolve(attacker, target, DamageRequest.Attack()).Amount;
            float jackpot = pipeline.Resolve(attacker, target, DamageRequest.Attack(isJackpot: true)).Amount;
            float both = pipeline.Resolve(attacker, target, DamageRequest.Attack(isCritical: true, isJackpot: true)).Amount;

            Assert.That(plain, Is.EqualTo(30f));
            Assert.That(jackpot, Is.EqualTo(30f * 2.5f), "(25 + 5) × 2.5 — 축복 공격력도 곱해진다");
            Assert.That(both, Is.EqualTo(30f * 2f * 2.5f), "치명타 × 잭팟");
        }

        [Test]
        public void 한_대의_배율은_축복으로_더한_공격력에도_곱해진다()
        {
            // 예전에는 무기 기본값(25)에만 곱해 25×2.5+5 = 67.5 가 나왔다.
            float damage = pipeline.Resolve(attacker, target, DamageRequest.Attack(damageMultiplier: 2.5f)).Amount;

            Assert.That(damage, Is.EqualTo(75f));
        }

        [Test]
        public void 배율은_방어력보다_먼저다()
        {
            target.Add(DamageStatKeys.Defense, 10f, StatStackMode.Add, "armor");

            float damage = pipeline.Resolve(attacker, target, DamageRequest.Attack(damageMultiplier: 2f)).Amount;

            Assert.That(damage, Is.EqualTo(30f * 2f - 10f));
        }

        [Test]
        public void 잭팟_능력치가_없는_계산표에서는_잭팟이_아무것도_곱하지_않는다()
        {
            var bare = new StatRegistry(new List<StatDefinition>
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
            var a = new StatSheet(bare);
            a.SetBase(DamageStatKeys.AttackPower, 25f);

            Assert.That(pipeline.Resolve(a, new StatSheet(bare), DamageRequest.Attack(isJackpot: true)).Amount,
                Is.EqualTo(25f));
        }

        // ── 고정 피해 ───────────────────────────────────────────────────

        [TestCase(DamageSource.Zone)]
        [TestCase(DamageSource.Dot)]
        [TestCase(DamageSource.Blessing)]
        public void 고정_피해는_피해_상한에도_걸리지_않는다(DamageSource source)
        {
            target.Add(DamageStatKeys.DamageTakenCap, 5f, StatStackMode.Min, "cap");

            DamageResult result = pipeline.Resolve(attacker, target, DamageRequest.Fixed(source, 20f));

            Assert.That(result.Amount, Is.EqualTo(20f));
            Assert.That(result.WasCapped, Is.False);
        }

        [Test]
        public void 보통_공격은_여전히_상한에_걸린다()
        {
            target.Add(DamageStatKeys.DamageTakenCap, 5f, StatStackMode.Min, "cap");

            Assert.That(pipeline.Resolve(attacker, target, DamageRequest.Attack()).Amount, Is.EqualTo(5f));
        }

        [TestCase(DamageSource.Dot, false)]
        [TestCase(DamageSource.Zone, false)]
        [TestCase(DamageSource.Blessing, true)]
        public void 피격_트리거는_DOT와_ZONE만_돌리지_않는다(DamageSource source, bool fires)
        {
            Assert.That(pipeline.Resolve(attacker, target, DamageRequest.Fixed(source, 3f)).FiresOnDamaged,
                Is.EqualTo(fires));
        }

        // ── 해석기 — 표식 사격은 고정 피해 ─────────────────────────────

        private sealed class ById : ICombatantPlayerMap
        {
            public bool TryGetPlayerId(CombatantId id, out int player)
            {
                player = id.Value;
                return true;
            }
        }

        [Test]
        public void 고정이라고_적힌_타격은_방어력과_받는_피해_감소를_무시한다()
        {
            var resolver = new BlessingCombatResolver(pipeline, new ById(), registry);
            var defender = new StatSheet(registry);
            defender.Add(DamageStatKeys.Defense, 100f, StatStackMode.Add, "armor");
            defender.Add(DamageStatKeys.DamageTakenMult, -0.5f, StatStackMode.Add, "tough");
            resolver.SetSheet(2, defender);

            float markShot = resolver.ResolveDamage(new CombatantId(2), new DamageInfo(
                new CombatantId(1), CombatWeapon.Bow, 10f, CombatDamageSource.Blessing, isFixed: true));
            float burst = resolver.ResolveDamage(new CombatantId(2), new DamageInfo(
                new CombatantId(1), CombatWeapon.Bow, 10f, CombatDamageSource.Blessing));

            Assert.That(markShot, Is.EqualTo(10f), "표식 사격 — 고정 피해");
            Assert.That(burst, Is.EqualTo(0f), "고정이 아닌 축복 피해는 방어력에 깎인다");
        }

        [Test]
        public void 해석기는_한_대의_배율과_잭팟을_파이프라인에_넘긴다()
        {
            var resolver = new BlessingCombatResolver(pipeline, new ById(), registry);
            attacker.Add(DamageStatKeys.JackpotMultiplier, 1f, StatStackMode.Add, "jackpot-stat");
            resolver.SetSheet(1, attacker);
            resolver.SetSheet(2, target);

            float damage = resolver.ResolveDamage(new CombatantId(2), new DamageInfo(
                new CombatantId(1), CombatWeapon.Sword, 25f, damageMultiplier: 1.5f, isJackpot: true));

            Assert.That(damage, Is.EqualTo(30f * 1.5f * 2f));
        }

        [Test]
        public void 프로틴_공격력은_무기_강화_공격력과_똑같이_계산된다()
        {
            // S-GD-7BA93J — 프로틴 쉐이크 +3 은 강화로 얻은 공격력 +3 과 같은 자리(1단계)에 더한다.
            attacker.Add(DamageStatKeys.JackpotMultiplier, 1.5f, StatStackMode.Add, "jackpot-stat");
            attacker.Add(DamageStatKeys.DamageBonus, .2f, StatStackMode.Add, "bonus");
            var upgraded = new StatSheet(registry);
            upgraded.SetBase(DamageStatKeys.AttackPower, 25f);
            upgraded.Add(DamageStatKeys.AttackPower, 5f, StatStackMode.Add, "combat-instinct-t1");
            upgraded.Add(DamageStatKeys.AttackPower, 3f, StatStackMode.Add, "weapon-upgrade");
            upgraded.Add(DamageStatKeys.JackpotMultiplier, 1.5f, StatStackMode.Add, "jackpot-stat");
            upgraded.Add(DamageStatKeys.DamageBonus, .2f, StatStackMode.Add, "bonus");

            foreach (var (critical, jackpot) in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                float protein = pipeline.Resolve(attacker, target,
                    DamageRequest.Attack(isCritical: critical, isJackpot: jackpot, attackPowerBonus: 3f)).Amount;
                float upgrade = pipeline.Resolve(upgraded, target,
                    DamageRequest.Attack(isCritical: critical, isJackpot: jackpot)).Amount;
                Assert.That(protein, Is.EqualTo(upgrade).Within(1e-4f), $"치명타 {critical} · 잭팟 {jackpot}");
            }

            Assert.That(pipeline.Resolve(attacker, target,
                    DamageRequest.Attack(isJackpot: true, attackPowerBonus: 3f)).Amount,
                Is.EqualTo((25f + 5f + 3f) * 1.2f * 2.5f).Within(1e-4f), "+3 도 잭팟 배율을 받는다");
        }

        [Test]
        public void 프로틴_공격력은_강화_공격력처럼_축복_피해에는_붙지_않는다()
        {
            var resolver = new BlessingCombatResolver(pipeline, new ById(), registry);
            resolver.SetSheet(1, attacker);
            resolver.SetSheet(2, target);

            float plain = resolver.ResolveDamage(new CombatantId(2), new DamageInfo(
                new CombatantId(1), CombatWeapon.Sword, 10f, CombatDamageSource.Blessing));
            float protein = resolver.ResolveDamage(new CombatantId(2), new DamageInfo(
                new CombatantId(1), CombatWeapon.Sword, 10f, CombatDamageSource.Blessing, attackPowerBonus: 3f));

            Assert.That(plain, Is.EqualTo(10f), "시트 공격력(강화·축복 +5)은 축복 피해에 안 들어간다");
            Assert.That(protein, Is.EqualTo(plain), "프로틴 +3 도 마찬가지다");
        }

        [Test]
        public void 기본값으로_만든_피해_정보의_배율은_1이다()
        {
            Assert.That(default(DamageInfo).DamageMultiplier, Is.EqualTo(1f));
            Assert.That(new DamageInfo(new CombatantId(1), CombatWeapon.Sword, 5f).DamageMultiplier, Is.EqualTo(1f));
        }

        // ── 축복 상태 — 고정 피해엔 감소 없음, 보호막은 깎임, 피격 트리거 ─────

        private static BlessingCombatState StateWithEffect(string effect)
        {
            BlessingMvpDefinition entry = BlessingMvpCatalog.All.First(x => x.Effect == effect);
            var state = new BlessingCombatState(new[] { entry.Definition });
            state.Tick(1f, 0f, combatActive: true, moving: false);
            return state;
        }

        [Test]
        public void 고정_피해에는_축복의_받는_피해_감소가_걸리지_않는다()
        {
            BlessingCombatState state = StateWithEffect("house-edge");

            // 감소율은 계산 4단계에 더해 들어간다(보통 피해만). 고정 피해는 그 단계를 지나지 않고, 뒤에서도 곱하지 않는다.
            Assert.That(state.IncomingDamageTakenBonus(2f, true), Is.LessThan(0f), "보통 피해는 줄어든다");
            float fixedHit = state.BeforeDamage(2f, 20f, 100f, 100f, false, float.PositiveInfinity,
                fixedDamage: true, firesOnDamaged: false);
            Assert.That(fixedHit, Is.EqualTo(20f), "고정 피해는 그대로");

            float fixedThroughPipeline = pipeline.Resolve(attacker, target,
                DamageRequest.Fixed(DamageSource.Zone, 20f)).Amount;
            Assert.That(fixedThroughPipeline, Is.EqualTo(20f), "고정 피해 요청에는 받는 피해 증감이 실리지 않는다");
        }

        [Test]
        public void 축복의_받는_피해_감소는_4단계에서_시트_감소와_더해지고_방어력보다_먼저다()
        {
            // 공격력 30 · 시트 받는 피해 −20%(세트 등) · 축복 −10%(하우스 엣지 등) · 방어력 4.
            // 합연산: 30 × (1 − 0.2 − 0.1) − 4 = 17. 예전(뒤에서 곱하기): (30 × 0.8 − 4) × 0.9 = 18.
            target.Add(DamageStatKeys.DamageTakenMult, -0.2f, StatStackMode.Add, "set");
            target.Add(DamageStatKeys.Defense, 4f, StatStackMode.Add, "armor");

            float damage = pipeline.Resolve(attacker, target, DamageRequest.Attack(damageTakenBonus: -0.1f)).Amount;
            float blessing = pipeline.Resolve(attacker, target, DamageRequest.Blessing(damageTakenBonus: -0.1f)).Amount;

            Assert.That(damage, Is.EqualTo(17f).Within(1e-4f));
            Assert.That(blessing, Is.EqualTo(17f).Within(1e-4f), "축복 피해도 같은 자리에서 더한다");
        }

        [Test]
        public void 받는_피해_감소는_패널티_곱연산_몫만_곱해_더한다()
        {
            // 시트: 기본 1 × (1 − 0.2) × 패널티 0.5 = 0.4. 축복 −0.1 은 Σ 에 들어가야 하므로 1 × (1 − 0.2 − 0.1) × 0.5 = 0.35.
            target.Add(DamageStatKeys.DamageTakenMult, -0.2f, StatStackMode.Add, "set");
            target.Add(DamageStatKeys.DamageTakenMult, 0.5f, StatStackMode.Multiply, "penalty");

            float damage = pipeline.Resolve(attacker, target, DamageRequest.Attack(damageTakenBonus: -0.1f)).Amount;

            Assert.That(damage, Is.EqualTo(30f * 0.35f).Within(1e-4f));
        }

        [Test]
        public void 보호막은_고정_피해도_체력보다_먼저_받는다()
        {
            BlessingMvpDefinition entry = BlessingMvpCatalog.All.First(x => x.Effect == "safe-asset");
            var state = new BlessingCombatState(new[] { entry.Definition });
            float idle = entry.Number("idle_seconds");
            float shield = entry.Number("shield");
            state.Tick(idle, idle, true, false);
            state.BeginAttack(idle, WeaponTag.Sword);
            Assert.That(state.Shield, Is.EqualTo(shield).Within(1e-4f));

            float through = state.BeforeDamage(idle, shield + 3f, 100f, 100f, false, float.PositiveInfinity,
                fixedDamage: true, firesOnDamaged: false);

            Assert.That(through, Is.EqualTo(3f).Within(1e-4f), "보호막이 먼저 깎이고 남은 것만 체력으로 간다");
            Assert.That(state.Shield, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void 자기장은_가드레일을_켜지_않는다()
        {
            BlessingCombatState state = StateWithEffect("guard-rail");

            state.BeforeDamage(2f, 5f, 100f, 100f, false, float.PositiveInfinity, fixedDamage: true, firesOnDamaged: false);

            Assert.That(state.GuardRailReady, Is.True, "피격 트리거가 돌지 않았다");

            state.BeforeDamage(3f, 5f, 95f, 100f, true, 1f, fixedDamage: false, firesOnDamaged: true);
            Assert.That(state.GuardRailReady, Is.False, "직접 맞으면 켜진다");
        }
    }
}
