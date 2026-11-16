using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 획득한 축복 → 스탯 시트 → 실제 피해까지의 조립 (기획 「0909 축복 시스템 기획」 §3).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것 셋.</b> ① 라운드가 넘어가도 일시 효과가 남지 않는다.
    /// ② 축복을 잃으면 그 효과도 사라진다. ③ 조건부·행동 파라미터가 스탯에 섞이지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>여기까지 오면 데이터만 오면 끝난다</b> — 축복 목록과 계수가 JSON 으로 들어오는 순간
    /// 코드 변경 없이 동작한다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class BlessingRuntimeTests
    {
        private const int AlicePlayer = 1;
        private static readonly CombatantId AliceCombatant = new CombatantId(10_001);
        private static readonly CombatantId BobCombatant = new CombatantId(10_002);
        private const int BobPlayer = 2;

        private const string MoveSpeed = "move-speed";

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
            new StatDefinition(MoveSpeed, "이동 속도", 5f, StatKind.Multiplier),
        });

        /// <summary>검증용 계수. 실제 값은 엑셀에서 온다.</summary>
        private static BlessingEffectCatalog Catalog() => new BlessingEffectCatalog(new[]
        {
            new BlessingEffectRow("armor-t1", DamageStatKeys.Defense, "10", "ADD"),
            new BlessingEffectRow("armor-t2", DamageStatKeys.Defense, "20", "ADD"),
            new BlessingEffectRow("light-step-t1", MoveSpeed, "0.05", "ADD"),
            new BlessingEffectRow("sniper-t1", DamageStatKeys.DamageBonus, "0.15", "ADD", "DISTANCE_GTE(15)"),
            new BlessingEffectRow("nine-lives-t3", DamageStatKeys.DamageTakenFixed, "1", "MIN"),
            new BlessingEffectRow("nine-lives-t3", "immune_sources", "DOT"),
            new BlessingEffectRow("nine-lives-t3", "max_hp_override", "9"),
        });

        private static BlessingDefinition Blessing(string id, BlessingTier tier = BlessingTier.Silver) =>
            new BlessingDefinition(id, id, WeaponTag.Common, tier);

        private StatRegistry registry;
        private BlessingCombatResolver resolver;
        private BlessingRuntime runtime;
        private FakePlayerMap map;

        [SetUp]
        public void SetUp()
        {
            registry = Registry();
            map = new FakePlayerMap();
            map.Bind(AliceCombatant, AlicePlayer);
            map.Bind(BobCombatant, BobPlayer);

            resolver = new BlessingCombatResolver(new DamagePipeline(minDamageFloor: 0f), map, registry);
            runtime = new BlessingRuntime(registry, Catalog(), resolver);
        }

        private float Sword(float amount) =>
            resolver.ResolveDamage(BobCombatant, new DamageInfo(AliceCombatant, WeaponType.Sword, amount));

        // ── 조립이 끝까지 이어지는가 ────────────────────────────────────

        [Test]
        public void 획득한_축복이_실제_피해까지_이어진다()
        {
            runtime.Rebuild(BobPlayer, new[] { Blessing("armor-t1") });

            Assert.That(Sword(25f), Is.EqualTo(15f), "방어력 10 이 빠져야 한다");
        }

        [Test]
        public void 같은_계열_두_등급은_중첩된다()
        {
            runtime.Rebuild(BobPlayer, new[] { Blessing("armor-t1"), Blessing("armor-t2", BlessingTier.Gold) });

            Assert.That(Sword(50f), Is.EqualTo(20f), "10 + 20 = 30 이 빠져야 한다");
        }

        [Test]
        public void 행동_파라미터도_함께_적용된다()
        {
            runtime.Rebuild(BobPlayer, new[] { Blessing("nine-lives-t3", BlessingTier.Platinum) });

            // damage-taken-fixed 가 덮어쓴다
            Assert.That(Sword(25f), Is.EqualTo(1f));

            // immune_sources 도 등록됐다
            var zone = resolver.ResolveDamage(
                BobCombatant, new DamageInfo(default, WeaponType.Sword, 3f, CombatDamageSource.Zone));
            Assert.That(zone, Is.EqualTo(1f), "자기장은 면역이 아니라 덮어쓰기 대상이다");

            // max_hp_override 는 스탯이 아니므로 행동 파라미터로 남는다
            var behavioral = runtime.BehavioralOf(BobPlayer);
            Assert.That(behavioral.Count, Is.EqualTo(2), "immune_sources + max_hp_override");
        }

        // ── 라운드·매치 경계 ────────────────────────────────────────────

        /// <summary>
        /// <b>매번 처음부터 다시 만든다.</b> 두 번 불러도 효과가 두 배가 되면 안 된다 —
        /// 라운드마다 부르는 구조라 그러면 3라운드에 방어력이 3배가 된다.
        /// </summary>
        [Test]
        public void 여러_번_불러도_효과가_누적되지_않는다()
        {
            var acquired = new[] { Blessing("armor-t1") };

            runtime.Rebuild(BobPlayer, acquired);
            runtime.Rebuild(BobPlayer, acquired);
            runtime.Rebuild(BobPlayer, acquired);

            Assert.That(Sword(25f), Is.EqualTo(15f), "세 번 불렀다고 방어력이 30 이 되면 안 된다");
        }

        [Test]
        public void 축복이_없으면_빈_시트로_덮어써서_지난_기여가_남지_않는다()
        {
            runtime.Rebuild(BobPlayer, new[] { Blessing("armor-t2", BlessingTier.Gold) });
            Assert.That(Sword(50f), Is.EqualTo(30f));

            // 새 매치라 획득 목록이 비었다.
            runtime.Rebuild(BobPlayer, new BlessingDefinition[0]);

            Assert.That(Sword(50f), Is.EqualTo(50f), "빈 목록으로 부르면 규칙이 사라져야 한다");
        }

        [Test]
        public void 매치가_끝나면_전부_비운다()
        {
            runtime.Rebuild(AlicePlayer, new[] { Blessing("light-step-t1") });
            runtime.Rebuild(BobPlayer, new[] { Blessing("armor-t1") });

            Assert.That(runtime.TrackedPlayerCount, Is.EqualTo(2));

            runtime.Clear();

            Assert.That(runtime.TrackedPlayerCount, Is.Zero);
            Assert.That(resolver.TrackedPlayerCount, Is.Zero, "해석기까지 함께 비워야 한다");
            Assert.That(Sword(25f), Is.EqualTo(25f));
        }

        [Test]
        public void 사람마다_따로_적용된다()
        {
            runtime.Rebuild(BobPlayer, new[] { Blessing("armor-t2", BlessingTier.Gold) });
            runtime.Rebuild(AlicePlayer, new BlessingDefinition[0]);

            Assert.That(Sword(50f), Is.EqualTo(30f), "밥만 방어력이 있다");
            Assert.That(
                resolver.ResolveDamage(AliceCombatant, new DamageInfo(BobCombatant, WeaponType.Sword, 50f)),
                Is.EqualTo(50f),
                "앨리스는 축복이 없다");
        }

        // ── 섞이면 안 되는 것들 ─────────────────────────────────────────

        [Test]
        public void 조건부_기여는_스탯에_섞이지_않고_따로_보관된다()
        {
            runtime.Rebuild(AlicePlayer, new[] { Blessing("sniper-t1") });
            runtime.Rebuild(BobPlayer, new BlessingDefinition[0]);

            // 저격수는 거리 조건이 있으므로 상시 켜지면 안 된다.
            Assert.That(Sword(100f), Is.EqualTo(100f), "조건을 평가하지 않은 채로 피해가 늘면 안 된다");

            var conditional = runtime.ConditionalOf(AlicePlayer);
            Assert.That(conditional.Count, Is.EqualTo(1));
            Assert.That(conditional[0].Condition, Is.EqualTo("DISTANCE_GTE(15)"));
        }

        [Test]
        public void 규칙이_없는_사람을_물어도_null_이_아니다()
        {
            Assert.That(runtime.Has(99), Is.False);
            Assert.That(runtime.ConditionalOf(99), Is.Not.Null);
            Assert.That(runtime.ConditionalOf(99), Is.Empty);
            Assert.That(runtime.BehavioralOf(99), Is.Not.Null);
            Assert.That(runtime.BehavioralOf(99), Is.Empty);
        }

        [Test]
        public void 계수가_없는_축복도_문제없이_지나간다()
        {
            // 시스템 대기 중이거나 순수 표기용 축복이 실제로 있다.
            var result = runtime.Rebuild(BobPlayer, new[] { Blessing("not-in-catalog-t1") });

            Assert.That(result.AppliedStatCount, Is.Zero);
            Assert.That(Sword(25f), Is.EqualTo(25f));
        }

        private sealed class FakePlayerMap : ICombatantPlayerMap
        {
            private readonly Dictionary<int, int> byCombatant = new Dictionary<int, int>();

            public void Bind(CombatantId combatant, int playerId) => byCombatant[combatant.Value] = playerId;

            public bool TryGetPlayerId(CombatantId combatant, out int playerId) =>
                byCombatant.TryGetValue(combatant.Value, out playerId);
        }
    }
}
