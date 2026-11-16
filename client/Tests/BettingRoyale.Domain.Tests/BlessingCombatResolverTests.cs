using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복을 실제 피해에 먹이는 어댑터 (기획 「0909 축복 시스템 기획」 §2·§12-5).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것 셋.</b> ① 축복이 없는 대상(봇·더미)에서 전투가 멈추지 않는다.
    /// ② 디버그 빈사 토글이 계속 눕힌다. ③ 소스 열거형을 캐스팅으로 잇지 않는다 —
    /// 값이 어긋나 있어서 <b>자기장이 도트로 읽힌다.</b>
    /// </para>
    /// </summary>
    [TestFixture]
    public class BlessingCombatResolverTests
    {
        private const int AlicePlayer = 1;
        private const int BobPlayer = 2;

        private static readonly CombatantId AliceCombatant = new CombatantId(10_001);
        private static readonly CombatantId BobCombatant = new CombatantId(10_002);

        /// <summary>봇. 경제 플레이어가 아니라 오프셋 아래 id 를 쓴다.</summary>
        private static readonly CombatantId Bot = new CombatantId(7);

        /// <summary>자기장은 공격자가 없다.</summary>
        private static readonly CombatantId NoAttacker = default;

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

        private StatRegistry registry;
        private BlessingCombatResolver resolver;
        private FakePlayerMap map;

        [SetUp]
        public void SetUp()
        {
            registry = Registry();
            map = new FakePlayerMap();
            map.Bind(AliceCombatant, AlicePlayer);
            map.Bind(BobCombatant, BobPlayer);

            resolver = new BlessingCombatResolver(new DamagePipeline(minDamageFloor: 0f), map, registry);
        }

        private StatSheet Sheet() => new StatSheet(registry);

        private static DamageInfo Sword(CombatantId attacker, float amount) =>
            new DamageInfo(attacker, WeaponType.Sword, amount);

        private static DamageInfo Zone(float amount) =>
            new DamageInfo(NoAttacker, WeaponType.Sword, amount, CombatDamageSource.Zone);

        [TestCase(25f)]
        [TestCase(15f)]
        public void EmergencyExitReducesAttackPowerBeforeDamageBonusAndRestoresAtExpiry(float weaponDamage)
        {
            var sheet = Sheet();
            sheet.Add(DamageStatKeys.AttackPower, 5f, StatStackMode.Add);
            sheet.Add(DamageStatKeys.DamageBonus, .4f, StatStackMode.Add);
            resolver.SetSheet(AlicePlayer, sheet);
            resolver.SetBlessings(AlicePlayer, new[] { new BlessingDefinition("emergency-exit-t1", "비상구", WeaponTag.Common, BlessingTier.Silver) });
            var state = resolver.CombatStateFor(AliceCombatant);
            Assert.That(BlessingMvpCatalog.TryGet("emergency-exit-t1", out var behavior), Is.True);
            float penalty = behavior.Number("penalty");
            state.Tick(0f, 0f, true, false);
            state.AfterDamage(1f, 80f, 1f, 100f, true);
            var snapshot = resolver.CaptureDamageStats(AliceCombatant, weaponDamage, WeaponType.Sword);
            float attackPower = (weaponDamage + 5f) * (1f - penalty);
            Assert.That(snapshot.AttackPower, Is.EqualTo(attackPower).Within(.0001f), "공격력 감소는 공격력 행에 표시한다");
            Assert.That(snapshot.DamageBonus, Is.EqualTo(.4f).Within(.0001f), "피해 증가에는 공격력 패널티를 섞지 않는다");
            var attack = state.BeginAttack(1f, WeaponTag.Sword);
            Assert.That(attack.DamageBonus, Is.Zero);
            Assert.That(resolver.ResolveDamage(Bot, new DamageInfo(AliceCombatant, WeaponType.Sword, weaponDamage,
                damageBonus: attack.DamageBonus)), Is.EqualTo(attackPower * 1.4f).Within(.0001f));
            state.Tick(1f + behavior.Number("penalty_seconds"), 0f, true, false);
            Assert.That(resolver.CaptureDamageStats(AliceCombatant, weaponDamage).AttackPower,
                Is.EqualTo(weaponDamage + 5f).Within(.0001f));
        }

        // ── 통과시켜야 하는 것들 ────────────────────────────────────────

        [Test]
        public void 스탯_시트가_없는_대상은_원래_값_그대로다()
        {
            // 봇·훈련 더미·샌드박스 NPC. 예외를 던지면 전투가 멈추고, 0 을 주면 아무도 안 죽는다.
            Assert.That(resolver.ResolveDamage(Bot, Sword(AliceCombatant, 25f)), Is.EqualTo(25f));
        }

        [Test]
        public void 축복의_받는_피해_감소는_계산_4단계에_더해지고_고정_피해에는_실리지_않는다()
        {
            // 시트가 없는 대상(봇)이어도 빠지지 않는다. 20 × (1 − 0.25) = 15 (S-GD-SH74SC).
            Assert.That(resolver.ResolveDamage(Bot, Sword(AliceCombatant, 20f).WithDamageTakenBonus(-0.25f)),
                Is.EqualTo(15f).Within(1e-4f));
            Assert.That(resolver.ResolveDamage(Bot, Zone(20f).WithDamageTakenBonus(-0.25f)),
                Is.EqualTo(20f).Within(1e-4f), "자기장은 고정 피해라 감소를 받지 않는다");
        }

        [Test]
        public void 등록되지_않은_플레이어도_원래_값_그대로다()
        {
            // 매핑은 되지만 아직 시트를 안 넣은 상태. 준비 단계 진입 직후 한 프레임에서 날 수 있다.
            Assert.That(resolver.ResolveDamage(BobCombatant, Sword(AliceCombatant, 25f)), Is.EqualTo(25f));
        }

        [Test]
        public void 자해는_규칙을_타지_않는다()
        {
            // G키 디버그 빈사 토글이 "현재 체력만큼" 피해를 자기 자신에게 넣는다.
            // 방어력이 빠지면 안 눕고, 그러면 오늘 팀 플레이에서 빈사 검증을 못 한다.
            var bob = Sheet();
            bob.Add(DamageStatKeys.Defense, 30f, StatStackMode.Add, "armor-t3");
            resolver.SetSheet(BobPlayer, bob);

            Assert.That(resolver.ResolveDamage(BobCombatant, Sword(BobCombatant, 100f)), Is.EqualTo(100f));
        }

        [Test]
        public void 매치가_끝나면_비워서_지난_축복이_새지_않는다()
        {
            var bob = Sheet();
            bob.Add(DamageStatKeys.Defense, 10f, StatStackMode.Add, "armor-t1");
            resolver.SetSheet(BobPlayer, bob);

            Assert.That(resolver.ResolveDamage(BobCombatant, Sword(AliceCombatant, 25f)), Is.EqualTo(15f));

            resolver.Clear();

            Assert.That(resolver.TrackedPlayerCount, Is.Zero);
            Assert.That(resolver.ResolveDamage(BobCombatant, Sword(AliceCombatant, 25f)), Is.EqualTo(25f));
        }

        // ── 실제로 규칙이 먹는가 ────────────────────────────────────────

        [Test]
        public void 무기_피해량이_공격력_기본값이_된다()
        {
            // ⚠️ 들어온 숫자를 기본값으로 안 넣으면 검이 25 가 아니라 0 이 된다.
            resolver.SetSheet(AlicePlayer, Sheet());
            resolver.SetSheet(BobPlayer, Sheet());

            Assert.That(resolver.ResolveDamage(BobCombatant, Sword(AliceCombatant, 25f)), Is.EqualTo(25f));
        }

        [Test]
        public void 공격자_축복과_방어자_축복이_함께_적용된다()
        {
            var alice = Sheet();
            alice.Add(DamageStatKeys.DamageBonus, 0.30f, StatStackMode.Add, "power-t1");
            resolver.SetSheet(AlicePlayer, alice);

            var bob = Sheet();
            bob.Add(DamageStatKeys.Defense, 20f, StatStackMode.Add, "armor-t2");
            resolver.SetSheet(BobPlayer, bob);

            // §11-1 과 같은 계산: 30 × 1.3 − 20 = 19
            Assert.That(resolver.ResolveDamage(BobCombatant, Sword(AliceCombatant, 30f)), Is.EqualTo(19f).Within(0.0001f));
        }

        [Test]
        public void 자기장은_공격자가_없어도_돌고_경감을_우회한다()
        {
            var bob = Sheet();
            bob.Add(DamageStatKeys.Defense, 100f, StatStackMode.Add, "armor-t3");
            resolver.SetSheet(BobPlayer, bob);

            // 고정 피해라 방어력을 무시한다 (§2.3). 방어력이 먹으면 자기장이 무력해진다.
            Assert.That(resolver.ResolveDamage(BobCombatant, Zone(20f)), Is.EqualTo(20f));
        }

        [Test]
        public void 면역_소스는_등록한_대로_적용된다()
        {
            resolver.SetSheet(BobPlayer, Sheet(), DamageSourceSet.Of(DamageSource.Zone));

            Assert.That(resolver.ResolveDamage(BobCombatant, Zone(20f)), Is.EqualTo(0f));
        }

        // ── 소스 매핑 ───────────────────────────────────────────────────

        /// <summary>
        /// <b>두 열거형을 캐스팅으로 이으면 조용히 틀린다.</b>
        /// <c>CombatDamageSource.Zone</c> 은 1 이고 <c>DamageSource.Zone</c> 은 2 다 —
        /// 캐스팅하면 자기장이 <c>Dot</c>(1) 으로 읽힌다.
        /// </summary>
        [Test]
        public void 소스는_캐스팅이_아니라_매핑으로_옮긴다()
        {
            Assert.That(BlessingCombatResolver.Translate(CombatDamageSource.Weapon), Is.EqualTo(DamageSource.Direct));
            Assert.That(BlessingCombatResolver.Translate(CombatDamageSource.Zone), Is.EqualTo(DamageSource.Zone));

            // 캐스팅이었다면 이 단언이 실패한다.
            Assert.That((int)CombatDamageSource.Zone, Is.Not.EqualTo((int)DamageSource.Zone),
                "값이 같아지면 이 테스트의 의미가 사라진다 — 그때는 매핑을 지워도 되는지 다시 판단할 것");
        }

        private sealed class FakePlayerMap : ICombatantPlayerMap
        {
            private readonly Dictionary<int, int> byCombatant = new Dictionary<int, int>();

            public void Bind(CombatantId combatant, int playerId) => byCombatant[combatant.Value] = playerId;

            public bool TryGetPlayerId(CombatantId combatant, out int playerId) =>
                byCombatant.TryGetValue(combatant.Value, out playerId);
        }
    }

    /// <summary>
    /// 후보 풀 필터 (기획 §6.1·§6.2).
    ///
    /// <para>
    /// <b>기획서가 "조용히 틀리는 것" 으로 지목한 것을 잡는다</b> — 풀이 정확히 후보 수와 같으면
    /// 재추첨이 <b>골드만 먹고 같은 카드를 순서만 섞어</b> 돌려준다. 예외도 로그도 없다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class BlessingPoolRulesTests
    {
        private static BlessingEffectCatalog Catalog(params BlessingEffectRow[] rows) =>
            new BlessingEffectCatalog(rows);

        [Test]
        public void 최소_인원이_지정된_축복은_작은_매치에서_빠진다()
        {
            var rules = new BlessingPoolRules(Catalog(
                new BlessingEffectRow("gambler-t1", BlessingPoolRules.MinMatchSizeKey, "4")));

            Assert.That(rules.MinMatchSizeOf("gambler-t1"), Is.EqualTo(4));
            Assert.That(rules.Allows("gambler-t1", matchSize: 2), Is.False);
            Assert.That(rules.Allows("gambler-t1", matchSize: 4), Is.True);
            Assert.That(rules.Allows("gambler-t1", matchSize: 6), Is.True);
        }

        [Test]
        public void 지정이_없는_축복은_어떤_인원에서도_나온다()
        {
            var rules = new BlessingPoolRules(Catalog(
                new BlessingEffectRow("gambler-t1", BlessingPoolRules.MinMatchSizeKey, "4")));

            Assert.That(rules.MinMatchSizeOf("light-step-t1"), Is.Zero);
            Assert.That(rules.Allows("light-step-t1", matchSize: 2), Is.True);
        }

        [Test]
        public void 인원을_모르면_아무것도_거르지_않는다()
        {
            // matchSize 0 = "인원을 안 넘겼다". 기존 호출부의 동작이 그대로 유지돼야 한다.
            var rules = new BlessingPoolRules(Catalog(
                new BlessingEffectRow("gambler-t1", BlessingPoolRules.MinMatchSizeKey, "4")));

            Assert.That(rules.Allows("gambler-t1", matchSize: 0), Is.True);
        }

        [Test]
        public void 최소_인원이_정수가_아니면_조용히_넘기지_않고_터진다()
        {
            // 0 으로 넘기면 그 축복이 2인 매치에도 뜬다.
            Assert.Throws<System.FormatException>(() => new BlessingPoolRules(Catalog(
                new BlessingEffectRow("broken-t1", BlessingPoolRules.MinMatchSizeKey, "많이"))));
        }

        [Test]
        public void 규칙이_없으면_빈_규칙으로_쓴다()
        {
            Assert.That(BlessingPoolRules.Empty.RuleCount, Is.Zero);
            Assert.That(BlessingPoolRules.Empty.Allows("무엇이든", matchSize: 2), Is.True);
        }
    }
}
