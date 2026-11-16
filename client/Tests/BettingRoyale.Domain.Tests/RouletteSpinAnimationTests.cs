using System;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Roulette;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 회전판 표현 (Jira S15P21A606-140, S-GD-49WM7S · S-CLI-B2TGQ8).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것 둘.</b>
    /// </para>
    ///
    /// <list type="number">
    /// <item><b>화면이 멈춘 자리와 정산 결과가 같은 칸이다.</b> 도착 각도를 눈으로 "그럴듯하다"
    /// 고 보면 부호 하나가 뒤집혀도 통과하고, 그때 바퀴는 <b>거울상 칸</b>에 멈춘다.
    /// 그래서 각도를 다시 칸으로 되돌려 확인한다.</item>
    /// <item><b>곡선을 바꿔도 도착 칸은 안 바뀐다.</b> 가속·감속과 바퀴 수는 확정 스펙이 없어
    /// 앞으로 손댈 값이라, 그 값이 결과에 새어 나가면 안 된다.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class RouletteSpinAnimationTests
    {
        private const int Slots = 25;
        private const float Accel = RouletteSpinAnimation.DefaultAccelerationFraction;
        private const int Decel = RouletteSpinAnimation.DefaultDecelerationExponent;

        // ── 각도 규약 ───────────────────────────────────────────────────

        [Test]
        public void 확정된_25칸에서_한_칸은_14점4도다()
        {
            Assert.That(RouletteSpinAnimation.SlotAngleDegrees(Slots), Is.EqualTo(14.4f).Within(0.0001f),
                "S-GD-49WM7S 의 25칸 · RusticRoulette_Slots.json 의 칸당 14.4°");
        }

        /// <summary>
        /// <b>포인터는 상단 0° 에 고정돼 있다</b> (S-CLI-B2TGQ8). 그래서 0번 칸은 안 돌려도 당첨이다.
        /// </summary>
        [Test]
        public void 첫_칸은_돌리지_않아도_포인터_아래에_있다()
        {
            Assert.That(RouletteSpinAnimation.RestAngleDegrees(0, Slots), Is.EqualTo(0f).Within(0.0001f));
        }

        /// <summary>
        /// <b>칸 번호는 상단에서 시계 방향으로 증가한다</b>(S-GD-49WM7S). 그 칸을 포인터로
        /// 데려오려면 <b>반시계</b>로 같은 각도만큼 돌려야 한다 — 부호가 뒤집히면 거울상 칸에 멈춘다.
        /// </summary>
        [TestCase(1, 345.6f)]
        [TestCase(7, 259.2f)]
        [TestCase(15, 144.0f)]
        [TestCase(24, 14.4f)]
        public void 적중_칸은_반시계로_칸번호만큼_돌려야_포인터에_온다(int slot, float expected)
        {
            Assert.That(RouletteSpinAnimation.RestAngleDegrees(slot, Slots),
                Is.EqualTo(expected).Within(0.0005f));
        }

        [Test]
        public void 모든_칸에서_각도와_칸이_왕복한다()
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                float angle = RouletteSpinAnimation.RestAngleDegrees(slot, Slots);

                Assert.That(RouletteSpinAnimation.SlotUnderPointer(angle, Slots), Is.EqualTo(slot),
                    $"칸 {slot} 의 정지 각도 {angle}° 가 다른 칸을 가리킨다");
            }
        }

        /// <summary>
        /// <b>한 칸 안에서는 흔들려도 같은 칸을 가리킨다.</b> 부동소수 오차나 연출용 미세 오프셋이
        /// 칸을 넘기면 화면과 정산이 갈린다.
        /// </summary>
        [Test]
        public void 반_칸_안쪽_오차로는_칸이_바뀌지_않는다()
        {
            float half = RouletteSpinAnimation.SlotAngleDegrees(Slots) / 2f;

            for (int slot = 0; slot < Slots; slot++)
            {
                float angle = RouletteSpinAnimation.RestAngleDegrees(slot, Slots);

                Assert.That(RouletteSpinAnimation.SlotUnderPointer(angle + (half * 0.9f), Slots),
                    Is.EqualTo(slot));
                Assert.That(RouletteSpinAnimation.SlotUnderPointer(angle - (half * 0.9f), Slots),
                    Is.EqualTo(slot));
            }
        }

        [Test]
        public void 범위_밖_칸은_조용히_넘어가지_않고_던진다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RouletteSpinAnimation.RestAngleDegrees(Slots, Slots));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RouletteSpinAnimation.RestAngleDegrees(-1, Slots));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RouletteSpinAnimation.SlotAngleDegrees(0));
        }

        // ── 도착점 ──────────────────────────────────────────────────────

        /// <summary>
        /// <b>회전이 끝나면 적중 칸이 포인터 아래에 있다.</b> 어디서 출발했든, 몇 바퀴를 돌든.
        /// </summary>
        [Test]
        public void 어디서_출발해도_적중_칸에서_멈춘다()
        {
            foreach (float start in new[] { 0f, 14.4f, 123.4f, 359.9f, 720f, -37f })
            {
                for (int slot = 0; slot < Slots; slot++)
                {
                    for (int turns = 0; turns <= 9; turns += 3)
                    {
                        float end = RouletteSpinAnimation.AngleDegreesAt(
                            1f, start, slot, Slots, turns, Accel, Decel);

                        Assert.That(RouletteSpinAnimation.SlotUnderPointer(end, Slots), Is.EqualTo(slot),
                            $"출발 {start}° · 칸 {slot} · {turns}바퀴 에서 다른 칸에 멈췄다");
                    }
                }
            }
        }

        /// <summary>
        /// <b>곡선과 바퀴 수는 도착점을 바꾸지 않는다.</b> 확정 스펙이 없는 값이라 앞으로 손댈
        /// 텐데, 그때 멈추는 칸까지 따라 움직이면 정산과 어긋난다.
        /// </summary>
        [Test]
        public void 곡선과_바퀴_수를_바꿔도_멈추는_칸은_같다()
        {
            const int Slot = 17;

            foreach (int turns in new[] { 0, 1, 5, 12 })
            {
                foreach (float accel in new[] { 0f, 0.05f, 0.3f, 0.95f })
                {
                    foreach (int decel in new[] { 1, 2, 4, 8 })
                    {
                        float end = RouletteSpinAnimation.AngleDegreesAt(
                            1f, startAngleDegrees: 91f, Slot, Slots, turns, accel, decel);

                        Assert.That(RouletteSpinAnimation.SlotUnderPointer(end, Slots), Is.EqualTo(Slot));
                    }
                }
            }
        }

        /// <summary>
        /// 도메인이 뽑은 적중 칸을 그대로 받아 <b>같은 칸에서 멈추는지</b> 끝에서 끝까지 확인한다.
        /// 표현이 결과를 다시 뽑거나 오프셋을 더하면 여기서 깨진다.
        /// </summary>
        [Test]
        public void 도메인이_뽑은_칸과_화면이_멈추는_칸이_같다()
        {
            var config = new RouletteConfig(
                betTicks: 4, spinTicks: 4, resultTicks: 2, minBet: 10, openMarginTicks: 2);
            var wheel = PlaceholderRouletteWheel.Create();
            var table = new RouletteTable(wheel, config);
            var random = new DeterministicRandom(20260915);

            int spins = 0;

            for (int i = 0; i < 400 && spins < 12; i++)
            {
                table.Tick(10_000, random);

                if (table.State != RouletteState.Spinning || table.RemainingTicks != config.SpinTicks)
                {
                    continue;
                }

                spins++;

                float end = RouletteSpinAnimation.AngleDegreesAt(
                    1f, startAngleDegrees: 0f, table.WinningSlot, wheel.SlotCount, turns: 7, Accel, Decel);
                int stopped = RouletteSpinAnimation.SlotUnderPointer(end, wheel.SlotCount);

                Assert.That(stopped, Is.EqualTo(table.WinningSlot));
                Assert.That(wheel.NumberAt(stopped), Is.EqualTo(table.WinningNumber),
                    "화면이 멈춘 칸의 숫자가 정산 숫자와 달라지면 플레이어는 정산이 틀렸다고 본다");
            }

            Assert.That(spins, Is.EqualTo(12), "회전을 12번 돌려 확인하려 했다");
        }

        // ── 곡선 ────────────────────────────────────────────────────────

        [Test]
        public void 곡선은_0에서_시작해_1에서_끝난다()
        {
            Assert.That(RouletteSpinAnimation.Ease(0f, Accel, Decel), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(RouletteSpinAnimation.Ease(1f, Accel, Decel), Is.EqualTo(1f).Within(1e-5f));

            // 범위 밖 진행도는 잘라서 쓴다 — 프레임이 튀어 1 을 넘어도 바퀴가 지나쳐 가면 안 된다.
            Assert.That(RouletteSpinAnimation.Ease(-3f, Accel, Decel), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(RouletteSpinAnimation.Ease(4f, Accel, Decel), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(RouletteSpinAnimation.Ease(float.NaN, Accel, Decel), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void 곡선은_되돌아가지_않는다()
        {
            float previous = -1f;

            for (int i = 0; i <= 1_000; i++)
            {
                float value = RouletteSpinAnimation.Ease(i / 1_000f, Accel, Decel);

                Assert.That(value, Is.GreaterThanOrEqualTo(previous),
                    $"진행도 {i / 1_000f} 에서 바퀴가 뒤로 돌았다");
                previous = value;
            }
        }

        /// <summary>
        /// <b>가속에서 감속으로 넘어가는 자리에서 속도가 이어진다.</b> 안 이어지면 바퀴가
        /// 그 순간 눈에 띄게 덜컥한다 — 테스트 없이는 "연출이 좀 이상한데" 로만 남는다.
        /// </summary>
        [Test]
        public void 가속과_감속의_이음매에서_속도가_이어진다()
        {
            const float Step = 1e-4f;

            float before = (RouletteSpinAnimation.Ease(Accel, Accel, Decel)
                - RouletteSpinAnimation.Ease(Accel - Step, Accel, Decel)) / Step;
            float after = (RouletteSpinAnimation.Ease(Accel + Step, Accel, Decel)
                - RouletteSpinAnimation.Ease(Accel, Accel, Decel)) / Step;

            Assert.That(after, Is.EqualTo(before).Within(0.02f),
                $"이음매 속도가 {before} → {after} 로 튀었다");
        }

        /// <summary>
        /// <b>끝에서 속도가 0 으로 들어온다.</b> 그래야 적중 칸에 미끄러지듯 멈춘다 —
        /// 속도가 남은 채 끝나면 각도는 맞아도 <b>뚝 끊긴 것처럼 보인다.</b>
        /// </summary>
        [Test]
        public void 마지막에는_속도가_0_으로_수렴한다()
        {
            const float Step = 1e-3f;

            float mid = (RouletteSpinAnimation.Ease(0.5f + Step, Accel, Decel)
                - RouletteSpinAnimation.Ease(0.5f, Accel, Decel)) / Step;
            float tail = (RouletteSpinAnimation.Ease(1f, Accel, Decel)
                - RouletteSpinAnimation.Ease(1f - Step, Accel, Decel)) / Step;

            Assert.That(tail, Is.LessThan(mid * 0.1f), $"끝 속도 {tail} 가 중간 속도 {mid} 에 비해 안 줄었다");
            Assert.That(tail, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void 앞부분에서_거리를_더_많이_먹는다()
        {
            float firstQuarter = RouletteSpinAnimation.Ease(0.25f, Accel, Decel);

            Assert.That(firstQuarter, Is.GreaterThan(0.25f),
                "룰렛은 빠르게 돌다 천천히 멈춘다 — 앞 1/4 에서 거리의 1/4 보다 많이 가야 한다");
        }

        // ── 바퀴 수 ─────────────────────────────────────────────────────

        [Test]
        public void 바퀴_수는_회전_시간에_비례하고_최소_한_바퀴다()
        {
            Assert.That(RouletteSpinAnimation.TurnsFor(8f, 0.9f), Is.EqualTo(7));
            Assert.That(RouletteSpinAnimation.TurnsFor(4f, 0.9f), Is.EqualTo(4));

            // 늦게 들어와 남은 시간이 거의 없어도 "회전" 으로 보여야 한다.
            Assert.That(RouletteSpinAnimation.TurnsFor(0.2f, 0.9f), Is.EqualTo(1));
            Assert.That(RouletteSpinAnimation.TurnsFor(0f, 0.9f), Is.EqualTo(1));
            Assert.That(RouletteSpinAnimation.TurnsFor(8f, 0f), Is.EqualTo(1));
        }

        /// <summary>
        /// 확정 회전 시간 8초(S-GD-DP2DVC)를 공용 틱 레이트로 옮겨도 같은 초가 나온다.
        /// <b>여기가 어긋나면 화면은 계속 도는데 판정은 이미 끝나 있다.</b>
        /// </summary>
        [Test]
        public void 회전_8초가_공용_틱_레이트로_왕복한다()
        {
            var config = PlaceholderRouletteConfig.Create(RouletteTiming.TicksPerSecond);

            Assert.That(RouletteTiming.ToSeconds(config.SpinTicks), Is.EqualTo(8f).Within(1e-4f));
            Assert.That(RouletteTiming.ToSeconds(config.BetTicks), Is.EqualTo(10f).Within(1e-4f));
            Assert.That(RouletteTiming.ToSeconds(config.ResultTicks), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(RouletteTiming.ToTicks(8f), Is.EqualTo(config.SpinTicks));
        }
    }
}
