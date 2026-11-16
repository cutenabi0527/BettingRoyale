using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복 스탯 계산 (기획 「0909 축복 시스템 기획」 §1·§11).
    ///
    /// <para>
    /// <b>기대값은 전부 기획서 §11 검증 케이스에서 그대로 가져왔다.</b> 제가 계산해서 적은 값이
    /// 아니라 <b>기획이 "구현 후 이 값이 나와야 한다" 고 적어 둔 값</b>이다. 번호를 함께 남긴다.
    /// </para>
    ///
    /// <para>
    /// <b>이 파일이 지키는 것은 하나다 — 공속 패널티가 상쇄되지 않는다.</b> 합연산으로 두면
    /// <c>쇠뇌(−30%) + 속사(+20%) = −10%</c> 가 되어 패널티가 사라지는데, <b>숫자는 그럴듯하게
    /// 나오므로 아무도 눈치채지 못한다.</b>
    /// </para>
    /// </summary>
    [TestFixture]
    public class BlessingStatTests
    {
        // ── 검증용 스탯 정의 ─────────────────────────────────────────────
        //
        // ⚠️ 확정 수치가 아니다. 기획서 §10 전역 상수와 §1.2 표에서 이 테스트에 필요한 것만
        //    옮겨 적은 것이고, 실제 값은 stats.json / constants.json 으로 들어온다 (D-041).

        private const string AttackPower = "attack-power";
        private const string AttackSpeed = "attack-speed";
        private const string MoveSpeed = "move-speed";
        private const string CritChance = "crit-chance";
        private const string OpenTime = "open-time";
        private const string AttackArc = "attack-arc";
        private const string DamageTakenFixed = "damage-taken-fixed";

        private static StatRegistry Registry() => new StatRegistry(new List<StatDefinition>
        {
            new StatDefinition(AttackPower, "공격력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(AttackSpeed, "공격 속도", 1f, StatKind.Multiplier),
            new StatDefinition(MoveSpeed, "이동 속도", 5f, StatKind.Multiplier),
            new StatDefinition(CritChance, "치명타 확률", 0.05f, StatKind.Flat, min: 0f, max: 1f),
            new StatDefinition(OpenTime, "개봉 시간", 5f, StatKind.Multiplier, min: 0.5f),
            new StatDefinition(AttackArc, "공격 폭", 45f, StatKind.Max),
            new StatDefinition(DamageTakenFixed, "받는 피해 고정", float.PositiveInfinity, StatKind.Min),
        });

        private StatSheet sheet;

        [SetUp]
        public void SetUp() => sheet = new StatSheet(Registry());

        // ── §11 검증 케이스 ─────────────────────────────────────────────

        /// <summary>§11-9 이속: 5.0 × (1 + 0.05 + 0.10 + 0.20) = 6.75 m/s</summary>
        [Test]
        public void 케이스9_이동속도는_증감분을_더해_곱한다()
        {
            sheet.Add(MoveSpeed, 0.05f, StatStackMode.Add, "light-step-t1");
            sheet.Add(MoveSpeed, 0.10f, StatStackMode.Add, "light-step-t2");
            sheet.Add(MoveSpeed, 0.20f, StatStackMode.Add, "light-step-t3");

            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(6.75f).Within(0.0001f));
        }

        /// <summary>
        /// §11-5 공속: 재빠른공격 t1+t2(+100%) + 잔상(×0.72) = 1.0 × 2.0 × 0.72 = <b>1.44</b>회/초
        ///
        /// <para>합연산이었다면 1.0 × (1 + 1.0 − 0.28) = 1.72 로 <b>패널티가 절반 넘게 상쇄된다.</b></para>
        /// </summary>
        [Test]
        public void 케이스5_공속_패널티는_보너스에_상쇄되지_않는다()
        {
            sheet.Add(AttackSpeed, 0.50f, StatStackMode.Add, "swift-attack-t1");
            sheet.Add(AttackSpeed, 0.50f, StatStackMode.Add, "swift-attack-t2");
            sheet.Add(AttackSpeed, 0.72f, StatStackMode.Multiply, "afterimage-t3");

            Assert.That(sheet.Get(AttackSpeed), Is.EqualTo(1.44f).Within(0.0001f),
                "곱연산이 아니면 패널티가 보너스에 먹힌다 — 숫자는 그럴듯해서 아무도 눈치채지 못한다");
        }

        /// <summary>§11-6 공속: 속사 3종(+170%) + 쇠뇌(×0.70) = 1.0 × 2.70 × 0.70 = <b>1.89</b>회/초</summary>
        [Test]
        public void 케이스6_보너스를_아무리_쌓아도_비율_손해는_유지된다()
        {
            sheet.Add(AttackSpeed, 0.40f, StatStackMode.Add, "rapid-fire-t1");
            sheet.Add(AttackSpeed, 0.60f, StatStackMode.Add, "rapid-fire-t2");
            sheet.Add(AttackSpeed, 0.70f, StatStackMode.Add, "rapid-fire-t3");
            sheet.Add(AttackSpeed, 0.70f, StatStackMode.Multiply, "crossbow-t1");

            Assert.That(sheet.Get(AttackSpeed), Is.EqualTo(1.89f).Within(0.0001f));
        }

        /// <summary>§11-7 치명타 확률: 5% + 20 + 50 + 75 = 150% → <b>100%</b> (초과분 버림)</summary>
        [Test]
        public void 케이스7_치명타_확률은_100퍼센트에서_잘린다()
        {
            sheet.Add(CritChance, 0.20f, StatStackMode.Add, "crit-t1");
            sheet.Add(CritChance, 0.50f, StatStackMode.Add, "crit-t2");
            sheet.Add(CritChance, 0.75f, StatStackMode.Add, "crit-t3");

            Assert.That(sheet.Get(CritChance), Is.EqualTo(1f).Within(0.0001f),
                "상한이 없으면 확률 판정이 150% 를 받아 늘 성공한다");
        }

        /// <summary>§11-8 개봉 시간: 5.0 × (1 − 0.2 − 0.3 − 0.4) = <b>0.5초</b> — 하한과 정확히 같다</summary>
        [Test]
        public void 케이스8_개봉_시간은_감소분을_음수로_더한다()
        {
            sheet.Add(OpenTime, -0.20f, StatStackMode.Add, "quick-open-t1");
            sheet.Add(OpenTime, -0.30f, StatStackMode.Add, "quick-open-t2");
            sheet.Add(OpenTime, -0.40f, StatStackMode.Add, "quick-open-t3");

            Assert.That(sheet.Get(OpenTime), Is.EqualTo(0.5f).Within(0.0001f),
                "3종 중첩이 하한(MIN_OPEN_TIME 0.5초)에 정확히 닿는다");
        }

        /// <summary>§11-10 공격 폭: max(45, 120) = <b>120도</b> — 더하지 않는다</summary>
        [Test]
        public void 케이스10_공격_폭은_가장_큰_것_하나만_산다()
        {
            sheet.Add(AttackArc, 120f, StatStackMode.Max, "wide-swing-t3");

            Assert.That(sheet.Get(AttackArc), Is.EqualTo(120f),
                "더하면 165도가 된다 — MAX 는 합연산이 아니다");
        }

        [Test]
        public void MIN_스택은_낮은_쪽이_이긴다()
        {
            // §2.5 — fixed 1 과 fixed 2 를 함께 들면 낮은 쪽인 1 이 이긴다.
            // 스택을 정하지 않으면 획득 순서에 따라 결과가 달라진다.
            sheet.Add(DamageTakenFixed, 2f, StatStackMode.Min, "some-blessing");
            sheet.Add(DamageTakenFixed, 1f, StatStackMode.Min, "nine-lives-t3");

            Assert.That(sheet.Get(DamageTakenFixed), Is.EqualTo(1f));
        }

        [Test]
        public void 기여가_없으면_기본값_그대로다()
        {
            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(5f));
            Assert.That(sheet.Get(AttackSpeed), Is.EqualTo(1f));
            Assert.That(sheet.Get(AttackArc), Is.EqualTo(45f));
            Assert.That(sheet.Get(DamageTakenFixed), Is.EqualTo(float.PositiveInfinity),
                "받는 피해 고정은 기본이 '없음' 이다");
        }

        // ── 무기별 기본값 주입 ───────────────────────────────────────────

        [Test]
        public void 무기가_정하는_기본값은_밖에서_주입한다()
        {
            // 공격력 기본값은 무기가 정한다 (검 25 / 활 15). stats 시트에 고정으로 적을 수 없다.
            sheet.SetBase(AttackPower, 25f);
            sheet.Add(AttackPower, 5f, StatStackMode.Add, "sharpen-t1");

            Assert.That(sheet.Get(AttackPower), Is.EqualTo(30f));
        }

        // ── 데이터 오류를 조용히 넘기지 않는다 ──────────────────────────

        [Test]
        public void 정의에_없는_스탯은_조용히_무시되지_않고_터진다()
        {
            // 오타 하나로 그 축복만 아무 효과가 없어지는데, 아무 신호가 없어 "약한가 보다" 로 넘어간다.
            Assert.Throws<KeyNotFoundException>(() => sheet.Add("move-speeed", 0.2f, StatStackMode.Add));
            Assert.Throws<KeyNotFoundException>(() => sheet.Get("no-such-stat"));
        }

        [Test]
        public void 계산_방식과_맞지_않는_스택은_터진다()
        {
            // FLAT 스탯에 곱연산 기여를 붙이면 계산식에 넣을 자리가 없어 값이 조용히 사라진다.
            var error = Assert.Throws<ArgumentException>(
                () => sheet.Add(CritChance, 0.7f, StatStackMode.Multiply, "crossbow-t1"));

            Assert.That(error.Message, Does.Contain("crossbow-t1"), "어느 축복이 잘못됐는지 알려줘야 한다");
            Assert.That(error.Message, Does.Contain("MULTIPLIER"));

            Assert.Throws<ArgumentException>(() => sheet.Add(MoveSpeed, 120f, StatStackMode.Max));
            Assert.Throws<ArgumentException>(() => sheet.Add(MoveSpeed, 1f, StatStackMode.Min));
        }

        [Test]
        public void 스탯_id_가_중복되면_레지스트리를_만들_수_없다()
        {
            // 어느 쪽이 이기는지가 로드 순서에 달리면 값이 이상할 때 원인을 찾을 수 없다.
            Assert.Throws<ArgumentException>(() => new StatRegistry(new[]
            {
                new StatDefinition(MoveSpeed, "이동 속도", 5f, StatKind.Multiplier),
                new StatDefinition(MoveSpeed, "이동 속도(중복)", 7f, StatKind.Multiplier),
            }));
        }

        [Test]
        public void 스탯_id_는_대소문자를_구분한다()
        {
            // BlessingDefinition 의 동등성이 Ordinal 이라 축복 id 도 같은 규칙을 따른다.
            Assert.Throws<KeyNotFoundException>(() => sheet.Get("Move-Speed"));
        }

        // ── 라운드 초기화 ───────────────────────────────────────────────

        [Test]
        public void 축복별로_기여를_걷어낼_수_있다()
        {
            // 라운드 시작마다 일시 효과만 풀린다 (S-GD-AN554W). 영구 효과는 남는다.
            sheet.Add(MoveSpeed, 0.30f, StatStackMode.Add, "ouch-t1");     // 피격 시 2초 버프
            sheet.Add(MoveSpeed, 0.10f, StatStackMode.Add, "light-step-t2"); // 영구

            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(7f).Within(0.0001f));

            sheet.RemoveFrom("ouch-t1");

            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(5.5f).Within(0.0001f),
                "일시 효과만 풀리고 영구 효과는 남는다");
        }
    }
}
