using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 피해 계산 파이프라인 (기획 「0909 축복 시스템 기획」 §2·§11).
    ///
    /// <para>
    /// <b>기대값은 전부 기획서 §11 검증 케이스다.</b> 제가 계산한 값이 아니라 기획이
    /// "구현 후 이 값이 나와야 한다" 고 못박아 둔 값이고, 테스트 이름에 번호를 남긴다.
    /// </para>
    ///
    /// <para>
    /// <b>이 파일이 지키는 것 둘.</b> ① 방어력 차감이 <b>항상 마지막</b>이라는 것 —
    /// 순서를 바꾸면 치명타가 통째로 사라진다. ② 도트·자기장이 피격 트리거를 돌리지 않는다는 것 —
    /// 돌면 무적 계열이 <b>영구 무적</b>이 된다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DamagePipelineTests
    {
        // ⚠️ 확정 수치가 아니다. 기획서 §10 전역 상수와 §1.2 표에서 필요한 것만 옮겨 적었고,
        //    실제 값은 stats.json / constants.json 으로 들어온다 (D-041).
        private const float MinDamageFloor = 0f;

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
        private DamagePipeline pipeline;
        private StatSheet attacker;
        private StatSheet target;

        [SetUp]
        public void SetUp()
        {
            registry = Registry();
            pipeline = new DamagePipeline(MinDamageFloor);
            attacker = new StatSheet(registry);
            target = new StatSheet(registry);
        }

        private DamageResult Hit(bool crit = false) =>
            pipeline.Resolve(attacker, target, DamageRequest.Attack(crit));

        // ── §11 검증 케이스 ─────────────────────────────────────────────

        /// <summary>§11-1 공격력 30, 피해 증가 +30%, 상대 방어력 20 / 관통 0 → <b>19</b></summary>
        [Test]
        public void 케이스1_피해_증가는_곱하고_방어력은_뺀다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 30f);
            attacker.Add(DamageStatKeys.DamageBonus, 0.30f, StatStackMode.Add, "power-t1");
            target.Add(DamageStatKeys.Defense, 20f, StatStackMode.Add, "armor-t2");

            Assert.That(Hit().Amount, Is.EqualTo(19f).Within(0.0001f));
        }

        /// <summary>§11-2 검 25, 비치명타, 상대 방어력 30 → <b>0</b> (block)</summary>
        [Test]
        public void 케이스2_방어력이_공격력보다_높으면_막힌다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 25f);
            target.Add(DamageStatKeys.Defense, 30f, StatStackMode.Add, "armor-t3");

            var result = Hit();

            Assert.That(result.Amount, Is.EqualTo(0f), "음수 피해가 회복이 되면 안 된다");
            Assert.That(result.WasBlocked, Is.True, "화면이 block 을 띄운다 — 의도된 상성이다");
        }

        /// <summary>
        /// 위 케이스를 <b>제품이 쓰는 하한</b>으로 다시 본다 — 방어력이 더 높아도 1 은 들어간다 (S-GD-56NVEJ).
        /// </summary>
        /// <remarks>
        /// 위 케이스2 는 <b>계산식 자체</b>(하한 0)를 보는 것이고 그대로 둔다. 제품 수치는 여기서 고정한다 —
        /// 둘을 한 테스트에 합치면 "식이 틀린 것" 과 "하한이 바뀐 것" 을 구분할 수 없다.
        /// </remarks>
        [Test]
        public void 제품_하한은_방어력이_더_높아도_최소_1을_남긴다()
        {
            var product = new DamagePipeline(DamagePipeline.DefaultMinDamageFloor);
            attacker.SetBase(DamageStatKeys.AttackPower, 25f);
            target.Add(DamageStatKeys.Defense, 30f, StatStackMode.Add, "armor-t3");

            var result = product.Resolve(attacker, target, DamageRequest.Attack(isCritical: false));

            Assert.That(DamagePipeline.DefaultMinDamageFloor, Is.EqualTo(1f),
                "제품 하한이 바뀌면 이 계약부터 고친다.");
            Assert.That(result.Amount, Is.EqualTo(1f).Within(0.0001f),
                "방어력이 아무리 높아도 계속 맞으면 언젠가 쓰러진다.");
            Assert.That(result.WasBlocked, Is.False, "완전 차단은 무적·면역뿐이다.");
        }

        /// <summary>
        /// 하한은 <b>면역보다 뒤</b>다. 앞에 두면 9개의 목숨이 독에 1 씩 맞아 "독 면역" 이 무너진다 (S-GD-56NVEJ).
        /// </summary>
        [Test]
        public void 제품_하한은_소스_면역을_뚫지_못한다()
        {
            var product = new DamagePipeline(DamagePipeline.DefaultMinDamageFloor);

            var result = product.Resolve(attacker, target,
                DamageRequest.Fixed(DamageSource.Dot, 10f), DamageSourceSet.Of(DamageSource.Dot));

            Assert.That(result.WasImmune, Is.True);
            Assert.That(result.Amount, Is.EqualTo(0f), "면역은 하한을 뚫지 않는다.");
        }

        /// <summary>
        /// 받는 피해 고정(9개의 목숨 1)은 덮어쓴 값이 하한과 같아 결과가 그대로 1 이다 (S-GD-56NVEJ).
        /// </summary>
        [Test]
        public void 제품_하한은_받는_피해_고정을_바꾸지_않는다()
        {
            var product = new DamagePipeline(DamagePipeline.DefaultMinDamageFloor);
            attacker.SetBase(DamageStatKeys.AttackPower, 999f);
            target.Add(DamageStatKeys.DamageTakenFixed, 1f, StatStackMode.Min, "nine-lives-t3");

            var result = product.Resolve(attacker, target, DamageRequest.Attack(isCritical: false));

            Assert.That(result.WasOverridden, Is.True);
            Assert.That(result.Amount, Is.EqualTo(1f).Within(0.0001f));
        }

        /// <summary>
        /// §11-3 검 25, 치명타(2.2배), 상대 방어력 30 → <b>25</b>
        ///
        /// <para>
        /// <b>순서를 지키는지 확인하는 케이스다.</b> 방어력을 먼저 빼면 (25−30)→0 이 되어
        /// 치명타가 통째로 사라진다. 고정 차감은 항상 마지막이다.
        /// </para>
        /// </summary>
        [Test]
        public void 케이스3_방어력_차감은_치명타_뒤에_온다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 25f);
            attacker.Add(DamageStatKeys.CritDamage, 0.20f, StatStackMode.Add, "crit-dmg-t1"); // 2.0 + 0.2
            target.Add(DamageStatKeys.Defense, 30f, StatStackMode.Add, "armor-t3");

            Assert.That(Hit(crit: true).Amount, Is.EqualTo(25f).Within(0.0001f),
                "순서를 바꾸면 0 이 된다 — 치명타 축복이 통째로 죽는다");
        }

        /// <summary>§11-4 활 15 + 보드킨 3종 flat 18 = 33, 피해 +10%, 상대 방어 30 / 관통 30 → <b>36.3</b></summary>
        [Test]
        public void 케이스4_관통이_방어력과_같으면_방어력이_0이_된다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 15f);
            attacker.Add(DamageStatKeys.AttackPower, 18f, StatStackMode.Add, "bodkin-t1t2t3");
            attacker.Add(DamageStatKeys.DamageBonus, 0.10f, StatStackMode.Add, "power-t1");
            attacker.Add(DamageStatKeys.ArmorPen, 30f, StatStackMode.Add, "bodkin-pen");
            target.Add(DamageStatKeys.Defense, 30f, StatStackMode.Add, "armor-t3");

            Assert.That(Hit().Amount, Is.EqualTo(36.3f).Within(0.0001f));
        }

        [Test]
        public void 관통_초과분은_추가_피해가_되지_않는다()
        {
            // 카운터 전용이다 (§2.1). 넘치는 만큼 피해가 늘면 방어력 0 인 상대에게 관통이 공격력이 된다.
            attacker.SetBase(DamageStatKeys.AttackPower, 25f);
            attacker.Add(DamageStatKeys.ArmorPen, 100f, StatStackMode.Add, "bodkin-pen");
            target.Add(DamageStatKeys.Defense, 10f, StatStackMode.Add, "armor-t1");

            Assert.That(Hit().Amount, Is.EqualTo(25f).Within(0.0001f), "25 그대로여야 한다");
        }

        // ── 9개의 목숨 (§2.6) ───────────────────────────────────────────

        /// <summary>§11-11 9개의 목숨 + 방어력 3종, 검 25 피격 → <b>1</b></summary>
        [Test]
        public void 케이스11_받는_피해_고정은_방어력_계산을_통째로_덮어쓴다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 25f);
            target.Add(DamageStatKeys.Defense, 30f, StatStackMode.Add, "armor-t3");
            target.Add(DamageStatKeys.DamageTakenFixed, 1f, StatStackMode.Min, "nine-lives-t3");

            var result = Hit();

            Assert.That(result.Amount, Is.EqualTo(1f));
            Assert.That(result.WasOverridden, Is.True);
        }

        /// <summary>§11-12 9개의 목숨 + 독화살 도트 → <b>0</b> (면역)</summary>
        [Test]
        public void 케이스12_면역_소스는_고정_피해보다_먼저_판정된다()
        {
            target.Add(DamageStatKeys.DamageTakenFixed, 1f, StatStackMode.Min, "nine-lives-t3");

            var result = pipeline.Resolve(
                attacker, target, DamageRequest.Fixed(DamageSource.Dot, 3f),
                DamageSourceSet.Of(DamageSource.Dot));

            Assert.That(result.Amount, Is.EqualTo(0f));
            Assert.That(result.WasImmune, Is.True);
            Assert.That(result.WasOverridden, Is.False, "면역이 먼저다 — 덮어쓰기까지 가지 않는다");
        }

        /// <summary>§11-13 9개의 목숨 + 자기장 틱 20 → <b>1</b></summary>
        [Test]
        public void 케이스13_자기장_큰_틱도_1로_덮인다()
        {
            target.Add(DamageStatKeys.DamageTakenFixed, 1f, StatStackMode.Min, "nine-lives-t3");

            var result = pipeline.Resolve(attacker, target, DamageRequest.Fixed(DamageSource.Zone, 20f));

            Assert.That(result.Amount, Is.EqualTo(1f));
        }

        /// <summary>
        /// §11-13-B 9개의 목숨 + 자기장 틱 0.9 (체력 비례) → <b>1</b>
        ///
        /// <para>
        /// <b>덮어쓰기라 위아래 모두 고정이다.</b> 상한이었다면 0.9 가 그대로 남아,
        /// 자기장 계산 방식이 바뀔 때마다 이 축복에 예외 처리를 만들어야 한다.
        /// </para>
        /// </summary>
        [Test]
        public void 케이스13B_자기장_작은_틱은_1로_올라간다()
        {
            target.Add(DamageStatKeys.DamageTakenFixed, 1f, StatStackMode.Min, "nine-lives-t3");

            var result = pipeline.Resolve(attacker, target, DamageRequest.Fixed(DamageSource.Zone, 0.9f));

            Assert.That(result.Amount, Is.EqualTo(1f), "상한이 아니라 덮어쓰기다 — 작은 값도 1 이 된다");
        }

        // ── 피격 트리거 (§2.4) ──────────────────────────────────────────

        /// <summary>§11-16 독화살 도트 틱 → <c>ON_DAMAGED</c> <b>발동 안 함</b></summary>
        [Test]
        public void 케이스16_도트는_피격_트리거를_돌리지_않는다()
        {
            var result = pipeline.Resolve(attacker, target, DamageRequest.Fixed(DamageSource.Dot, 2f));

            Assert.That(result.FiresOnDamaged, Is.False,
                "돌면 '1초에 한 대만!' 의 무적이 계속 갱신되어 영구 무적이 된다");
        }

        /// <summary>§11-17 자기장 틱 → <c>ON_DAMAGED</c> <b>발동 안 함</b></summary>
        [Test]
        public void 케이스17_자기장은_피격_트리거를_돌리지_않는다()
        {
            var result = pipeline.Resolve(attacker, target, DamageRequest.Fixed(DamageSource.Zone, 20f));

            Assert.That(result.FiresOnDamaged, Is.False);
        }

        [Test]
        public void 직접_공격은_막혀도_피격_트리거가_돈다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 5f);
            target.Add(DamageStatKeys.Defense, 30f, StatStackMode.Add, "armor-t3");

            var result = Hit();

            Assert.That(result.Amount, Is.EqualTo(0f));
            Assert.That(result.FiresOnDamaged, Is.True, "피해가 0 이어도 맞은 것은 맞다");
        }

        // ── 고정 피해와 경감 (§2.3) ─────────────────────────────────────

        [Test]
        public void 고정_피해는_방어력과_받는_피해_감소를_전부_무시한다()
        {
            target.Add(DamageStatKeys.Defense, 100f, StatStackMode.Add, "armor-t3");
            target.Add(DamageStatKeys.DamageTakenMult, -0.50f, StatStackMode.Add, "tough-t2");

            var result = pipeline.Resolve(attacker, target, DamageRequest.Fixed(DamageSource.Dot, 2f));

            Assert.That(result.Amount, Is.EqualTo(2f), "고정 피해는 1~6단계를 건너뛴다");
        }

        [Test]
        public void 받는_피해_감소는_음수_기여로_들어온다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 100f);
            target.Add(DamageStatKeys.DamageTakenMult, -0.10f, StatStackMode.Add, "tough-t1");
            target.Add(DamageStatKeys.DamageTakenMult, -0.20f, StatStackMode.Add, "tough-t2");

            // 1.0 × (1 − 0.10 − 0.20) = 0.70
            Assert.That(Hit().Amount, Is.EqualTo(70f).Within(0.0001f));
        }

        [Test]
        public void 받는_피해_상한은_위만_자른다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 100f);
            target.Add(DamageStatKeys.DamageTakenCap, 30f, StatStackMode.Min, "future-blessing");

            var result = Hit();

            Assert.That(result.Amount, Is.EqualTo(30f));
            Assert.That(result.WasCapped, Is.True);
            Assert.That(result.WasOverridden, Is.False, "상한과 덮어쓰기는 다른 것이다");
        }

        // ── 흡혈 (§2.1 7단계) ───────────────────────────────────────────

        [Test]
        public void 흡혈은_최종_피해_기준이다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 40f);
            attacker.Add(DamageStatKeys.Lifesteal, 0.15f, StatStackMode.Add, "bloodthirst-t2");
            target.Add(DamageStatKeys.Defense, 10f, StatStackMode.Add, "armor-t1");

            var result = Hit();

            Assert.That(result.Amount, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(result.Lifesteal, Is.EqualTo(4.5f).Within(0.0001f), "방어력 차감 전이 아니라 후 기준이다");
        }

        [Test]
        public void 막힌_대에서는_흡혈도_0이다()
        {
            attacker.SetBase(DamageStatKeys.AttackPower, 5f);
            attacker.Add(DamageStatKeys.Lifesteal, 0.50f, StatStackMode.Add, "bloodthirst-t2");
            target.Add(DamageStatKeys.Defense, 30f, StatStackMode.Add, "armor-t3");

            Assert.That(Hit().Lifesteal, Is.EqualTo(0f));
        }
    }
}
