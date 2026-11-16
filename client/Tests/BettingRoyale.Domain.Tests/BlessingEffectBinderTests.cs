using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 엑셀 <c>effects</c> 한 줄이 실제 효과가 되기까지 (기획 「0909 축복 시스템 기획」 §1·§3·§8).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것 셋.</b> ① 조건부 기여가 스탯 시트에 스며들지 않는다 —
    /// 스며들면 언더독·저격수가 <b>상시 켜진다</b>. ② 같은 계열의 여러 등급이 중첩된다.
    /// ③ 스탯 이름 오타가 조용히 "행동 파라미터" 로 빠지지 않는다.
    /// </para>
    ///
    /// <para>
    /// 아래 행들은 <b>실제 엑셀 구조 그대로</b>다 —
    /// <c>blessing_id | param_key | value | stack | condition</c>.
    /// 값은 검증용이고 확정 수치가 아니다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class BlessingEffectBinderTests
    {
        private const string MoveSpeed = "move-speed";
        private const string AttackSpeed = "attack-speed";
        private const string DamageBonus = "damage-bonus";
        private const string ChestOpenTime = "chest-open-time";
        private const string DamageTakenFixed = "damage-taken-fixed";

        private static StatRegistry Registry() => new StatRegistry(new List<StatDefinition>
        {
            new StatDefinition(MoveSpeed, "이동 속도", 5f, StatKind.Multiplier),
            new StatDefinition(AttackSpeed, "공격 속도", 1f, StatKind.Multiplier),
            new StatDefinition(DamageBonus, "피해 증가", 0f, StatKind.Flat),
            new StatDefinition(ChestOpenTime, "개봉 시간", 5f, StatKind.Multiplier, min: 0.5f),
            new StatDefinition(DamageTakenFixed, "받는 피해 고정", float.PositiveInfinity, StatKind.Min),
        });

        private static BlessingEffectBinder Binder(params BlessingEffectRow[] rows) =>
            new BlessingEffectBinder(Registry(), new BlessingEffectCatalog(rows));

        private static StatSheet Sheet() => new StatSheet(Registry());

        // ── 스탯 기여 ───────────────────────────────────────────────────

        [Test]
        public void 스탯_행은_시트에_바로_얹힌다()
        {
            var binder = Binder(
                new BlessingEffectRow("light-step-t1", MoveSpeed, "0.05", "ADD"),
                new BlessingEffectRow("light-step-t2", MoveSpeed, "0.10", "ADD"));

            var sheet = Sheet();
            var result = binder.Apply(sheet, new[] { "light-step-t1", "light-step-t2" });

            Assert.That(result.AppliedStatCount, Is.EqualTo(2));
            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(5.75f).Within(0.0001f));
        }

        [Test]
        public void 같은_계열의_여러_등급은_그냥_중첩된다()
        {
            // §1.5 — 상속이 아니다. 별도 처리 없이 둘 다 얹힌다.
            var binder = Binder(
                new BlessingEffectRow("quick-open-t1", ChestOpenTime, "-0.20", "ADD"),
                new BlessingEffectRow("quick-open-t2", ChestOpenTime, "-0.30", "ADD"),
                new BlessingEffectRow("quick-open-t3", ChestOpenTime, "-0.40", "ADD"));

            var sheet = Sheet();
            binder.Apply(sheet, new[] { "quick-open-t1", "quick-open-t2", "quick-open-t3" });

            Assert.That(sheet.Get(ChestOpenTime), Is.EqualTo(0.5f).Within(0.0001f),
                "3종 중첩이 하한에 정확히 닿는다 (§11-8)");
        }

        [Test]
        public void 곱연산_스택이_그대로_전달된다()
        {
            var binder = Binder(
                new BlessingEffectRow("rapid-fire-t1", AttackSpeed, "1.00", "ADD"),
                new BlessingEffectRow("crossbow-t1", AttackSpeed, "0.70", "MUL"));

            var sheet = Sheet();
            binder.Apply(sheet, new[] { "rapid-fire-t1", "crossbow-t1" });

            Assert.That(sheet.Get(AttackSpeed), Is.EqualTo(1.40f).Within(0.0001f),
                "MUL 을 ADD 로 읽으면 1.7 이 되어 패널티가 상쇄된다");
        }

        [Test]
        public void 스택이_비어_있으면_합연산이다()
        {
            // §1.3 — "공속을 제외한 모든 스탯 = 합연산".
            var binder = Binder(new BlessingEffectRow("power-t1", DamageBonus, "0.30"));

            var sheet = Sheet();
            binder.Apply(sheet, new[] { "power-t1" });

            Assert.That(sheet.Get(DamageBonus), Is.EqualTo(0.30f).Within(0.0001f));
        }

        // ── 조건부 (§3.3) ───────────────────────────────────────────────

        [Test]
        public void 조건부_기여는_시트에_들어가지_않는다()
        {
            var binder = Binder(
                new BlessingEffectRow("sniper-t1", DamageBonus, "0.15", "ADD", "DISTANCE_GTE(15)"),
                new BlessingEffectRow("power-t1", DamageBonus, "0.10", "ADD"));

            var sheet = Sheet();
            var result = binder.Apply(sheet, new[] { "sniper-t1", "power-t1" });

            Assert.That(sheet.Get(DamageBonus), Is.EqualTo(0.10f).Within(0.0001f),
                "조건부가 시트에 스며들면 저격수가 거리와 무관하게 상시 켜진다");

            Assert.That(result.Conditional.Count, Is.EqualTo(1));
            Assert.That(result.Conditional[0].Condition, Is.EqualTo("DISTANCE_GTE(15)"));
            Assert.That(result.AppliedStatCount, Is.EqualTo(1));
        }

        [Test]
        public void 복합_조건도_문자열_그대로_보존된다()
        {
            // 실제 데이터에 있는 값이다. 열거형으로 만들었으면 여기서 막혔다.
            const string Compound = "DISTANCE_LTE(10) AND MOVING_TOWARD_TARGET";

            var binder = Binder(new BlessingEffectRow("rush-t2", MoveSpeed, "0.25", "ADD", Compound));
            var result = binder.Apply(Sheet(), new[] { "rush-t2" });

            Assert.That(result.Conditional[0].Condition, Is.EqualTo(Compound));
        }

        // ── 행동 파라미터 ───────────────────────────────────────────────

        [Test]
        public void 스탯이_아닌_키는_행동_파라미터로_빠진다()
        {
            var binder = Binder(
                new BlessingEffectRow("one-hit-t3", "trigger", "ON_DAMAGED"),
                new BlessingEffectRow("one-hit-t3", "invuln_duration", "1.0"),
                new BlessingEffectRow("one-hit-t3", "invuln_kind", "ATTACKABLE"));

            var result = binder.Apply(Sheet(), new[] { "one-hit-t3" });

            Assert.That(result.AppliedStatCount, Is.Zero);
            Assert.That(result.Behavioral.Count, Is.EqualTo(3));
            Assert.That(result.TryGetBehavioral("one-hit-t3", "invuln_kind", out var kind), Is.True);
            Assert.That(kind.Value, Is.EqualTo("ATTACKABLE"));
        }

        [Test]
        public void 면역_소스는_합집합으로_모인다()
        {
            var binder = Binder(
                new BlessingEffectRow("nine-lives-t3", "immune_sources", "DOT"),
                new BlessingEffectRow("future-t2", "immune_sources", "ZONE"));

            var result = binder.Apply(Sheet(), new[] { "nine-lives-t3", "future-t2" });
            var immune = result.BuildImmuneSources();

            Assert.That(immune.Contains(DamageSource.Dot), Is.True);
            Assert.That(immune.Contains(DamageSource.Zone), Is.True);
            Assert.That(immune.Contains(DamageSource.Direct), Is.False);
        }

        [Test]
        public void 면역_소스에_모르는_값이_오면_터진다()
        {
            var binder = Binder(new BlessingEffectRow("broken-t1", "immune_sources", "MAGIC"));
            var result = binder.Apply(Sheet(), new[] { "broken-t1" });

            Assert.Throws<FormatException>(() => result.BuildImmuneSources());
        }

        /// <summary>
        /// <b>9개의 목숨이 한 축복으로 끝까지 이어지는지</b> — 계수 → 시트 → 피해 계산.
        /// §11-11·13-B 를 계수 행에서 출발해 재현한다.
        /// </summary>
        [Test]
        public void 계수_한_줄이_실제_피해_결과까지_이어진다()
        {
            var registry = Registry();
            var catalog = new BlessingEffectCatalog(new[]
            {
                new BlessingEffectRow("nine-lives-t3", DamageTakenFixed, "1", "MIN"),
                new BlessingEffectRow("nine-lives-t3", "immune_sources", "DOT"),
                new BlessingEffectRow("nine-lives-t3", "max_hp_override", "9"),
            });

            var target = new StatSheet(registry);
            var result = new BlessingEffectBinder(registry, catalog).Apply(target, new[] { "nine-lives-t3" });

            var pipeline = new DamagePipeline(minDamageFloor: 0f);

            // §11-13-B 자기장 0.9 → 덮어쓰기라 1 로 올라간다
            var zone = pipeline.Resolve(
                null, target, DamageRequest.Fixed(DamageSource.Zone, 0.9f), result.BuildImmuneSources());
            Assert.That(zone.Amount, Is.EqualTo(1f));

            // §11-12 독화살 도트 → 면역
            var dot = pipeline.Resolve(
                null, target, DamageRequest.Fixed(DamageSource.Dot, 3f), result.BuildImmuneSources());
            Assert.That(dot.Amount, Is.EqualTo(0f));
            Assert.That(dot.WasImmune, Is.True);

            // max_hp_override 는 스탯이 아니라 행동 파라미터로 남는다 — 체력 시스템이 읽는다
            Assert.That(result.TryGetBehavioral("nine-lives-t3", "max_hp_override", out var hp), Is.True);
            Assert.That(hp.TryGetNumber(out float hpValue), Is.True);
            Assert.That(hpValue, Is.EqualTo(9f));
        }

        // ── 데이터 오류를 조용히 넘기지 않는다 ──────────────────────────

        [Test]
        public void 스탯_이름_오타는_행동_파라미터로_빠지지_않고_터진다()
        {
            // '-' 가 들어간 키는 스탯 규칙이다. 정의에 없으면 오타이거나 stats 시트 누락이다.
            // 이 검사가 없으면 그 축복만 아무 효과 없이 조용히 넘어간다.
            var binder = Binder(new BlessingEffectRow("light-step-t1", "move-speeed", "0.05", "ADD"));

            var error = Assert.Throws<KeyNotFoundException>(() => binder.Apply(Sheet(), new[] { "light-step-t1" }));

            Assert.That(error.Message, Does.Contain("light-step-t1"));
            Assert.That(error.Message, Does.Contain("move-speeed"));
        }

        [Test]
        public void 스탯_기여인데_숫자가_아니면_터진다()
        {
            var binder = Binder(new BlessingEffectRow("broken-t1", MoveSpeed, "TRUE", "ADD"));

            Assert.Throws<FormatException>(() => binder.Apply(Sheet(), new[] { "broken-t1" }));
        }

        [Test]
        public void 모르는_스택_값은_합연산으로_넘기지_않고_터진다()
        {
            var binder = Binder(new BlessingEffectRow("broken-t1", AttackSpeed, "0.7", "MULTIPLY"));

            Assert.Throws<FormatException>(() => binder.Apply(Sheet(), new[] { "broken-t1" }));
        }

        [Test]
        public void 계수가_없는_축복도_정상이다()
        {
            // 시스템 대기 중이거나 순수 표기용 축복이 실제로 있다.
            var binder = Binder(new BlessingEffectRow("light-step-t1", MoveSpeed, "0.05", "ADD"));

            var result = binder.Apply(Sheet(), new[] { "not-in-catalog-t1" });

            Assert.That(result.AppliedStatCount, Is.Zero);
            Assert.That(result.Behavioral, Is.Empty);
            Assert.That(result.Conditional, Is.Empty);
        }

        [Test]
        public void 소수점은_문화권과_무관하게_읽는다()
        {
            // 데이터는 항상 '.' 소수점이다. 한국어 윈도우에서도 같아야 한다.
            var row = new BlessingEffectRow("x-t1", MoveSpeed, "0.05", "ADD");

            Assert.That(row.TryGetNumber(out float value), Is.True);
            Assert.That(value, Is.EqualTo(0.05f).Within(0.00001f));
        }

        [Test]
        public void 카탈로그는_축복별로_행을_모은다()
        {
            var catalog = new BlessingEffectCatalog(new[]
            {
                new BlessingEffectRow("a-t1", MoveSpeed, "0.1", "ADD"),
                new BlessingEffectRow("a-t1", AttackSpeed, "0.2", "ADD"),
                new BlessingEffectRow("b-t1", MoveSpeed, "0.3", "ADD"),
            });

            Assert.That(catalog.RowCount, Is.EqualTo(3));
            Assert.That(catalog.BlessingCount, Is.EqualTo(2));
            Assert.That(catalog.For("a-t1").Count, Is.EqualTo(2));
            Assert.That(catalog.For("없는-축복"), Is.Empty, "null 이 아니라 빈 목록이다");
        }

        // ── 방식 칸이 빈 행 — 엑셀은 이 칸이 없다 ──────────────────────────

        private static StatRegistry KindRegistry() => new StatRegistry(new[]
        {
            new StatDefinition("attack-arc", "공격 폭", 45f, StatKind.Max),
            new StatDefinition("damage-taken-cap", "받는 피해 상한", float.PositiveInfinity, StatKind.Min),
            new StatDefinition("crit-chance", "치명타 확률", 0.05f, StatKind.Flat),
        });

        /// <summary>
        /// 최댓값 능력치에 방식 없이 들어온 기여는 최댓값으로 얹는다. 합연산으로 넣으면 계산표가 무시해서
        /// 넓은 베기(공격 폭 120)가 아무 일도 안 했다.
        /// </summary>
        [Test]
        public void 방식이_빈_행은_최댓값_능력치에_최댓값으로_얹힌다()
        {
            var registry = KindRegistry();
            var binder = new BlessingEffectBinder(registry, new BlessingEffectCatalog(new[]
            {
                new BlessingEffectRow("wide-swing-t3", "attack-arc", "120"),
            }));
            var sheet = new StatSheet(registry);

            binder.Apply(sheet, new[] { "wide-swing-t3" });

            Assert.That(sheet.Get("attack-arc"), Is.EqualTo(120f));
        }

        [Test]
        public void 방식이_빈_행은_최솟값_능력치에_최솟값으로_얹힌다()
        {
            var registry = KindRegistry();
            var binder = new BlessingEffectBinder(registry, new BlessingEffectCatalog(new[]
            {
                new BlessingEffectRow("cap-t1", "damage-taken-cap", "25"),
            }));
            var sheet = new StatSheet(registry);

            binder.Apply(sheet, new[] { "cap-t1" });

            Assert.That(sheet.Get("damage-taken-cap"), Is.EqualTo(25f));
        }

        [Test]
        public void 방식이_빈_행은_그_밖의_능력치에_합연산으로_얹힌다()
        {
            // 기획 §1.3 — 공속 디버프를 빼면 전부 합연산.
            var registry = KindRegistry();
            var binder = new BlessingEffectBinder(registry, new BlessingEffectCatalog(new[]
            {
                new BlessingEffectRow("critical-t1", "crit-chance", "0.25"),
                new BlessingEffectRow("critical-t2", "crit-chance", "0.50"),
            }));
            var sheet = new StatSheet(registry);

            binder.Apply(sheet, new[] { "critical-t1", "critical-t2" });

            Assert.That(sheet.Get("crit-chance"), Is.EqualTo(0.80f).Within(1e-5f), "0.05 + 0.25 + 0.50");
        }

        /// <summary>
        /// 방식을 <b>잘못</b> 적은 행은 드러나야 한다. 능력치 방식으로 슬쩍 바꿔 주면 데이터 실수가 묻힌다.
        /// </summary>
        [Test]
        public void 최댓값_능력치에_최솟값_방식을_적으면_드러난다()
        {
            var registry = KindRegistry();
            var binder = new BlessingEffectBinder(registry, new BlessingEffectCatalog(new[]
            {
                new BlessingEffectRow("typo-t1", "attack-arc", "120", "MIN"),
            }));

            Assert.Throws<ArgumentException>(() => binder.Apply(new StatSheet(registry), new[] { "typo-t1" }));
        }

        [Test]
        public void 방식을_적어_둔_행은_그대로_따른다()
        {
            var registry = new StatRegistry(new[] { new StatDefinition(MoveSpeed, "이동 속도", 1f, StatKind.Multiplier) });
            var binder = new BlessingEffectBinder(registry, new BlessingEffectCatalog(new[]
            {
                new BlessingEffectRow("slow-t1", MoveSpeed, "0.5", "MUL"),
            }));
            var sheet = new StatSheet(registry);

            binder.Apply(sheet, new[] { "slow-t1" });

            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(0.5f).Within(1e-5f));
        }
    }
}
