using System;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 이월 경계 테스트 (S-GD-P5RFZM).
    ///
    /// Jira S15P21A606-21 이 테스트할 경계를 직접 지정했다 — <b>0 / 10,000 / 50,000 / 50,001·무제한</b>.
    /// 아래 케이스는 그 목록을 그대로 덮는다.
    /// </summary>
    [TestFixture]
    public class CarryOverLimitTests
    {
        // --------------------------------------------------------------- 경계: 0

        [Test]
        public void 한도가_0이면_남은_골드가_전부_소멸한다()
        {
            var limit = CarryOverLimit.FromSetting(0);

            var result = limit.Apply(7_500);

            Assert.That(result.CarriedGold, Is.EqualTo(0));
            Assert.That(result.ForfeitedGold, Is.EqualTo(7_500));
        }

        [Test]
        public void 한도가_0이고_남은_골드도_0이면_소멸액도_0이다()
        {
            var result = CarryOverLimit.FromSetting(0).Apply(0);

            Assert.That(result.CarriedGold, Is.EqualTo(0));
            Assert.That(result.ForfeitedGold, Is.EqualTo(0));
        }

        // ---------------------------------------------------------- 경계: 10,000 (기본값)

        [Test]
        public void 기본_한도는_10000G_다()
        {
            Assert.That(CarryOverLimit.Default.IsUnlimited, Is.False);
            Assert.That(CarryOverLimit.Default.Gold, Is.EqualTo(10_000));
            Assert.That(CarryOverLimit.DefaultGold, Is.EqualTo(10_000));
        }

        [Test]
        public void 남은_골드가_한도와_정확히_같으면_전부_이월된다()
        {
            var result = CarryOverLimit.FromSetting(10_000).Apply(10_000);

            Assert.That(result.CarriedGold, Is.EqualTo(10_000));
            Assert.That(result.ForfeitedGold, Is.EqualTo(0), "한도와 같은 값은 초과가 아니다");
        }

        [Test]
        public void 남은_골드가_한도보다_1G_많으면_1G_만_소멸한다()
        {
            var result = CarryOverLimit.FromSetting(10_000).Apply(10_001);

            Assert.That(result.CarriedGold, Is.EqualTo(10_000));
            Assert.That(result.ForfeitedGold, Is.EqualTo(1));
        }

        [Test]
        public void 남은_골드가_한도보다_적으면_그대로_이월된다()
        {
            var result = CarryOverLimit.FromSetting(10_000).Apply(3_200);

            Assert.That(result.CarriedGold, Is.EqualTo(3_200));
            Assert.That(result.ForfeitedGold, Is.EqualTo(0));
        }

        // ------------------------------------------------------ 경계: 50,000 (설정 상한)

        [Test]
        public void 설정값_50000은_무제한이_아니라_유한_한도다()
        {
            var limit = CarryOverLimit.FromSetting(50_000);

            Assert.That(limit.IsUnlimited, Is.False, "50,000 은 상한 그 자체이지 초과가 아니다");
            Assert.That(limit.Gold, Is.EqualTo(50_000));
        }

        [Test]
        public void 한도_50000에서_초과분은_소멸한다()
        {
            var result = CarryOverLimit.FromSetting(50_000).Apply(60_000);

            Assert.That(result.CarriedGold, Is.EqualTo(50_000));
            Assert.That(result.ForfeitedGold, Is.EqualTo(10_000));
        }

        // -------------------------------------------- 경계: 50,001 · 무제한 (무한 이월)

        [Test]
        public void 설정값_50001은_무한_이월로_해석된다()
        {
            var limit = CarryOverLimit.FromSetting(50_001);

            Assert.That(limit.IsUnlimited, Is.True,
                "50,001 은 잘못된 입력이 아니라 '무제한' 을 뜻하는 유효한 입력이다 (S-GD-P5RFZM)");
        }

        [Test]
        public void 무한_이월이면_남은_골드가_전부_넘어간다()
        {
            var result = CarryOverLimit.FromSetting(50_001).Apply(1_234_567);

            Assert.That(result.CarriedGold, Is.EqualTo(1_234_567));
            Assert.That(result.ForfeitedGold, Is.EqualTo(0));
        }

        [Test]
        public void 무제한_선택은_50001_입력과_같은_결과다()
        {
            Assert.That(CarryOverLimit.FromUnlimitedSelection(),
                Is.EqualTo(CarryOverLimit.FromSetting(50_001)));
        }

        [Test]
        public void 무한_한도에서_금액을_읽으면_예외다()
        {
            var limit = CarryOverLimit.Unlimited;

            Assert.That(() => _ = limit.Gold, Throws.InvalidOperationException,
                "조용히 0 을 돌려주면 '이월 없음' 과 구분되지 않는다");
        }

        // ------------------------------------------------------------- 범위 밖 입력

        [Test]
        public void 음수_설정은_0으로_붙는다()
        {
            // ⚠️ 스펙에 없는 동작이다. 설정 경로에서 예외를 던지지 않기 위한 선택 —
            //    확인 후 조정한다 (S-GD-P5RFZM 의 미정 항목).
            var limit = CarryOverLimit.FromSetting(-1);

            Assert.That(limit.IsUnlimited, Is.False);
            Assert.That(limit.Gold, Is.EqualTo(0));
        }

        [Test]
        public void 남은_골드가_음수면_예외다()
        {
            Assert.That(() => CarryOverLimit.Default.Apply(-1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // ----------------------------------------------------------------- 설정 모델

        [Test]
        public void 설정값을_바꿔도_원본_설정은_그대로다()
        {
            var original = EconomyConfig.Default;

            var changed = original.WithCarryOverLimitSetting(0);

            Assert.That(original.CarryOverLimit.Gold, Is.EqualTo(10_000), "설정은 불변이어야 한다");
            Assert.That(changed.CarryOverLimit.Gold, Is.EqualTo(0));
        }

        [Test]
        public void 라운드_지급_골드의_기본값은_1000G_다()
        {
            Assert.That(EconomyConfig.Default.RoundGrantGold, Is.EqualTo(1_000));
        }
    }
}
