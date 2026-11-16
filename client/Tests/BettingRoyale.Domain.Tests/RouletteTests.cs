using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Roulette;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 룰렛 (Jira S15P21A606-84, S-GD-49WM7S · S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것 셋.</b> 어떤 숫자도 무한 증식하지 않는다는 것,
    /// 돌지 않은 판의 돈은 돌려준다는 것, 환불이 최종 순위를 오염시키지 않는다는 것.
    /// </para>
    /// </summary>
    [TestFixture]
    public class RouletteTests
    {
        private const int Alice = 1;
        private const int Bob = 2;
        private const int Ticks = 10;   // 검증용 짧은 틱 — 확정값은 데이터로 들어온다

        private static RouletteConfig Config() =>
            new RouletteConfig(betTicks: Ticks, spinTicks: Ticks, resultTicks: 2, minBet: 10, openMarginTicks: 5);

        private static RouletteTable Table() =>
            new RouletteTable(PlaceholderRouletteWheel.Create(), Config());

        /// <summary>마감이 한참 남은 상태. 판이 정상으로 돈다.</summary>
        private const int PlentyOfTime = 10_000;

        // ── 바퀴 ────────────────────────────────────────────────────────

        [Test]
        public void 확정된_배치는_25칸이고_칸_수가_스펙과_같다()
        {
            var wheel = PlaceholderRouletteWheel.Create();

            Assert.That(wheel.SlotCount, Is.EqualTo(25));

            // S-GD-49WM7S 의 표. 손으로 센 것을 믿지 않는다.
            Assert.That(wheel.SlotsFor(1), Is.EqualTo(12));
            Assert.That(wheel.SlotsFor(3), Is.EqualTo(6));
            Assert.That(wheel.SlotsFor(5), Is.EqualTo(4));
            Assert.That(wheel.SlotsFor(10), Is.EqualTo(2));
            Assert.That(wheel.SlotsFor(20), Is.EqualTo(1));

            Assert.That(wheel.SlotsFor(1) + wheel.SlotsFor(3) + wheel.SlotsFor(5)
                + wheel.SlotsFor(10) + wheel.SlotsFor(20), Is.EqualTo(25));
        }

        /// <summary>
        /// <b>배치 순서가 3D 에셋과 같아야 한다</b> (S-GD-49WM7S).
        ///
        /// <para>
        /// 아래 배열은 <c>Art/Models/Roulette/RusticRoulette_Slots.json</c> 의
        /// <c>displayValue</c> 를 상단 0° 부터 시계 방향으로 옮겨 적은 것이다. 적중 칸 번호가
        /// 곧 모델의 회전 각도(<c>칸 번호 × 360/25</c>)라서, <b>어긋나면 화면이 멈춘 자리와
        /// 정산 결과가 다른 숫자를 가리킨다.</b> 컴파일러도 다른 테스트도 못 잡는 종류다.
        /// </para>
        /// </summary>
        [Test]
        public void 배치_순서가_3D_에셋의_슬롯_순서와_같다()
        {
            var expected = new[]
            {
                1, 3, 1, 5, 1, 3, 1, 10, 1, 3, 1, 5, 1,
                3, 1, 20, 1, 3, 1, 5, 1, 3, 1, 10, 5,
            };

            var wheel = PlaceholderRouletteWheel.Create();

            Assert.That(wheel.Layout, Is.EqualTo(expected),
                "RusticRoulette_Slots.json 의 순서와 어긋났다. 회전이 멈춘 칸과 정산 숫자가 달라진다");
        }

        /// <summary>
        /// <b>25칸에서는 배분이 유일하다</b> (S-GD-49WM7S).
        ///
        /// <para>
        /// 각 숫자가 쓸 수 있는 칸 수의 상한은 <c>전체 칸 수 / (숫자+1)</c> 인데, 25칸에서는
        /// 그 상한들의 합이 <b>정확히 25</b> 다. 남는 칸이 0 이므로 모든 숫자가 상한을 그대로
        /// 쓸 수밖에 없고, 다른 배분은 존재하지 않는다.
        /// </para>
        ///
        /// <para>
        /// 이전 46칸은 상한 합이 47 이라 <b>한 칸을 임의로 깎아야 했다</b>. 이 테스트는
        /// 칸 수를 바꿀 때 그 임의성이 다시 들어오는 것을 잡는다.
        /// </para>
        /// </summary>
        [Test]
        public void 확정된_칸_수에서는_배분에_임의로_고를_여지가_없다()
        {
            var wheel = PlaceholderRouletteWheel.Create();

            int capacity = 0;

            foreach (var number in wheel.Numbers)
            {
                capacity += wheel.SlotCount / (number + 1);
            }

            Assert.That(capacity, Is.EqualTo(wheel.SlotCount),
                $"상한 합 {capacity} 가 칸 수 {wheel.SlotCount} 와 달라 배분에 임의 선택이 생긴다");
        }

        [Test]
        public void 어떤_숫자도_기대_회수율_100퍼센트를_넘지_않는다()
        {
            var wheel = PlaceholderRouletteWheel.Create();

            foreach (var number in wheel.Numbers)
            {
                long expected = (long)wheel.SlotsFor(number) * (number + 1);

                Assert.That(expected, Is.LessThanOrEqualTo(wheel.SlotCount),
                    $"숫자 {number} 에만 걸어도 골드가 무한히 늘어난다");
            }
        }

        [Test]
        public void 무한_증식하는_배치는_만들_수_없다()
        {
            // 확정 배치는 당연히 통과한다.
            Assert.DoesNotThrow(() => PlaceholderRouletteWheel.Create());

            // 12칸에서 모든 숫자가 기대값 정확히 100% — 경계는 허용한다.
            //   1: 6칸 × 2 = 12    3: 3칸 × 4 = 12
            //   5: 2칸 × 6 = 12   11: 1칸 × 12 = 12
            Assert.DoesNotThrow(() => new RouletteWheel(
                new[] { 1, 1, 1, 1, 1, 1, 3, 3, 3, 5, 5, 11 }));

            // 4칸 중 3칸이 숫자 1 → 3 × 2 = 6 > 4. 여기에만 걸면 계속 딴다.
            var error = Assert.Throws<ArgumentException>(() => new RouletteWheel(new[] { 1, 1, 1, 3 }));
            Assert.That(error.Message, Does.Contain("무한히"));
        }

        [Test]
        public void 회수액은_건_돈의_숫자플러스1_배다()
        {
            Assert.That(RouletteWheel.PayoutFor(1, 100), Is.EqualTo(200), "1 은 ×2");
            Assert.That(RouletteWheel.PayoutFor(20, 100), Is.EqualTo(2_100), "20 은 ×21");
        }

        /// <summary>
        /// <b>S-GD-49WM7S 의 배당표를 숫자 다섯 개 전부 확인한다.</b>
        /// 두 개만 보면 가운데 세 개가 조용히 틀려도 통과한다.
        /// </summary>
        [TestCase(1, 2)]
        [TestCase(3, 4)]
        [TestCase(5, 6)]
        [TestCase(10, 11)]
        [TestCase(20, 21)]
        public void 배당표의_다섯_숫자가_전부_스펙과_같다(int number, int multiplier)
        {
            Assert.That(RouletteWheel.PayoutFor(number, 100), Is.EqualTo(100 * multiplier));
        }

        /// <summary>
        /// <b>숫자별 기대 회수율이 스펙의 표와 정확히 같다</b> (S-GD-49WM7S).
        ///
        /// <para>
        /// "100% 를 넘지 않는다" 만 보면 <b>집이 너무 많이 먹는 쪽으로 틀어져도 안 잡힌다</b> —
        /// 칸 수를 하나만 줄여도 그 검사는 통과하고 플레이어만 손해를 본다. 표 자체를 고정한다.
        /// </para>
        ///
        /// <para>
        /// 부동소수로 재지 않는다 — <c>칸 수 × (숫자+1)</c> 을 <b>정수</b>로 비교한다
        /// (25칸 기준 24 = 96%, 22 = 88%, 21 = 84%).
        /// </para>
        /// </summary>
        [TestCase(1, 12, 24)]
        [TestCase(3, 6, 24)]
        [TestCase(5, 4, 24)]
        [TestCase(10, 2, 22)]
        [TestCase(20, 1, 21)]
        public void 숫자별_기대_회수율이_스펙_표와_같다(int number, int slots, int expectedReturn)
        {
            var wheel = PlaceholderRouletteWheel.Create();

            Assert.That(wheel.SlotsFor(number), Is.EqualTo(slots));
            Assert.That(wheel.SlotsFor(number) * (number + 1), Is.EqualTo(expectedReturn),
                $"숫자 {number} 의 기대 회수가 25칸 중 {expectedReturn} 이 아니다");
        }

        /// <summary>
        /// <b>적중 칸과 적중 숫자는 같은 배치에서 나온 짝이다.</b>
        ///
        /// <para>
        /// 화면은 <b>칸 번호</b>로 바퀴를 멈추고 정산은 <b>숫자</b>로 돈을 준다. 둘이 다른 표에서
        /// 오면 <b>화면이 멈춘 자리와 받은 돈이 다른 숫자를 가리킨다</b> — 플레이어에게는 정산이
        /// 틀린 것으로 보이고, 잔액은 맞아서 로그에도 안 남는다.
        /// </para>
        ///
        /// <para><b>25칸이 전부 나오는지</b>도 함께 센다. 안 나오는 칸이 있으면 확률이 표와 다르다.</para>
        /// </summary>
        [Test]
        public void 적중_칸과_적중_숫자는_항상_짝이_맞고_모든_칸이_나온다()
        {
            var wheel = PlaceholderRouletteWheel.Create();
            var table = new RouletteTable(wheel, Config());
            var random = new DeterministicRandom(20260915);
            var seenSlots = new HashSet<int>();

            for (int i = 0; i < 40_000 && seenSlots.Count < wheel.SlotCount; i++)
            {
                table.Tick(PlentyOfTime, random);

                if (table.State != RouletteState.Spinning || table.WinningSlot < 0)
                {
                    continue;
                }

                Assert.That(table.WinningSlot, Is.InRange(0, wheel.SlotCount - 1));
                Assert.That(table.WinningNumber, Is.EqualTo(wheel.NumberAt(table.WinningSlot)),
                    $"칸 {table.WinningSlot} 의 숫자가 배치와 다르다");

                seenSlots.Add(table.WinningSlot);
            }

            Assert.That(seenSlots.Count, Is.EqualTo(wheel.SlotCount),
                "25칸 전부가 실제로 나와야 한다 — 안 나오는 칸이 있으면 그 숫자의 확률이 표와 다르다");
        }

        // ── 상태 흐름 ───────────────────────────────────────────────────

        [Test]
        public void 베팅에서_회전을_거쳐_결과로_간다()
        {
            var table = Table();
            var random = new DeterministicRandom(1234);

            Assert.That(table.State, Is.EqualTo(RouletteState.Idle));

            table.Tick(PlentyOfTime, random);
            Assert.That(table.State, Is.EqualTo(RouletteState.Betting));
            Assert.That(table.Cycle, Is.EqualTo(1));

            RunTicks(table, random, Ticks);
            Assert.That(table.State, Is.EqualTo(RouletteState.Spinning));
            Assert.That(table.WinningSlot, Is.InRange(0, 24), "회전이 시작될 때 결과가 정해진다");
            Assert.That(table.WinningNumber, Is.GreaterThan(0));

            RunTicks(table, random, Ticks);
            Assert.That(table.State, Is.EqualTo(RouletteState.Result));

            RunTicks(table, random, 2);
            Assert.That(table.State, Is.EqualTo(RouletteState.Idle));

            table.Tick(PlentyOfTime, random);
            Assert.That(table.Cycle, Is.EqualTo(2), "준비 시간이 남으면 다음 판이 열린다");
        }

        [Test]
        public void 회전_중에는_걸_수_없다()
        {
            var table = Table();
            var random = new DeterministicRandom(1);
            table.Tick(PlentyOfTime, random);
            RunTicks(table, random, Ticks);

            Assert.That(table.State, Is.EqualTo(RouletteState.Spinning));
            Assert.That(table.InspectBet(1, 100, gold: 1_000, out _),
                Is.EqualTo(RouletteBetResult.NotBetting));
        }

        // ── 베팅 ────────────────────────────────────────────────────────

        [Test]
        public void 여러_숫자에_걸_수_있고_같은_숫자는_더해진다()
        {
            var table = OpenBetting(out var random);

            Assert.That(table.InspectBet(1, 100, 1_000, out int a), Is.EqualTo(RouletteBetResult.Success));
            table.RecordBet(Alice, 1, a);
            Assert.That(table.InspectBet(20, 50, 900, out int b), Is.EqualTo(RouletteBetResult.Success));
            table.RecordBet(Alice, 20, b);
            Assert.That(table.InspectBet(1, 30, 850, out int c), Is.EqualTo(RouletteBetResult.Success));
            table.RecordBet(Alice, 1, c);

            Assert.That(table.StakedOn(Alice, 1), Is.EqualTo(130), "같은 숫자는 누적된다");
            Assert.That(table.StakedOn(Alice, 20), Is.EqualTo(50));
            Assert.That(table.StakedBy(Alice), Is.EqualTo(180));
        }

        [Test]
        public void 가진_것보다_많이_걸면_가진_만큼으로_깎인다()
        {
            var table = OpenBetting(out _);

            var result = table.InspectBet(1, requestedAmount: 5_000, gold: 700, out int accepted);

            Assert.That(result, Is.EqualTo(RouletteBetResult.Success),
                "'전부 걸기' 를 눌렀는데 거절당하는 것이 더 이상하다");
            Assert.That(accepted, Is.EqualTo(700));
        }

        [Test]
        public void 가진_돈이_최소_베팅에도_못_미치면_거절한다()
        {
            var table = OpenBetting(out _);

            Assert.That(table.InspectBet(1, 100, gold: 9, out int accepted),
                Is.EqualTo(RouletteBetResult.NotEnoughGold));
            Assert.That(accepted, Is.EqualTo(0));
        }

        [TestCase(9, RouletteBetResult.BelowMinimum, TestName = "최소보다_적게_걸면_거절")]
        [TestCase(10, RouletteBetResult.Success, TestName = "최소와_같으면_된다")]
        public void 최소_베팅_경계(int amount, RouletteBetResult expected)
        {
            var table = OpenBetting(out _);

            Assert.That(table.InspectBet(1, amount, gold: 1_000, out _), Is.EqualTo(expected));
        }

        [Test]
        public void 바퀴에_없는_숫자에는_걸_수_없다()
        {
            var table = OpenBetting(out _);

            Assert.That(table.InspectBet(7, 100, 1_000, out _), Is.EqualTo(RouletteBetResult.NoSuchNumber));
        }

        [Test]
        public void 베팅_중에는_전부_물릴_수_있다()
        {
            var table = OpenBetting(out _);
            table.RecordBet(Alice, 1, 100);
            table.RecordBet(Alice, 20, 50);

            Assert.That(table.WithdrawBets(Alice), Is.EqualTo(150));
            Assert.That(table.StakedBy(Alice), Is.EqualTo(0));
        }

        [Test]
        public void 회전이_시작되면_물릴_수_없다()
        {
            var table = OpenBetting(out var random);
            table.RecordBet(Alice, 1, 100);
            RunTicks(table, random, Ticks);

            Assert.That(table.State, Is.EqualTo(RouletteState.Spinning));
            Assert.That(table.WithdrawBets(Alice), Is.EqualTo(0), "이미 돌기 시작한 판은 못 물린다");
        }

        // ── 정산 ────────────────────────────────────────────────────────

        [Test]
        public void 적중한_숫자에_건_것만_회수하고_나머지는_잃는다()
        {
            var table = Table();
            var random = new DeterministicRandom(4242);
            table.Tick(PlentyOfTime, random);

            // 모든 숫자에 100 씩 건다 — 무엇이 나와도 하나는 맞는다.
            foreach (var number in table.Wheel.Numbers)
            {
                table.RecordBet(Alice, number, 100);
            }

            int staked = table.StakedBy(Alice);
            RunTicks(table, random, Ticks);          // 회전 시작
            int hit = table.WinningNumber;
            var settlements = RunUntilSettled(table, random);

            Assert.That(settlements.Length, Is.EqualTo(1));
            var s = settlements[0];

            Assert.That(s.PlayerId, Is.EqualTo(Alice));
            Assert.That(s.Staked, Is.EqualTo(staked));
            Assert.That(s.StakedOnWinner, Is.EqualTo(100));
            Assert.That(s.Payout, Is.EqualTo(100 * (hit + 1)));
            Assert.That(s.Net, Is.EqualTo(s.Payout - staked));
        }

        [Test]
        public void 전부_틀리면_건_돈을_모두_잃는다()
        {
            var table = Table();
            var random = new DeterministicRandom(999);
            table.Tick(PlentyOfTime, random);
            RunTicks(table, random, Ticks);
            int hit = table.WinningNumber;

            // 적중 숫자가 아닌 것 하나를 고른다.
            int miss = 0;
            foreach (var number in table.Wheel.Numbers)
            {
                if (number != hit) { miss = number; break; }
            }

            // 회전 중이라 못 걸므로 다시 열어 같은 결과를 재현하는 대신,
            // 베팅 시점에 걸고 같은 판을 끝까지 돌린다.
            var replay = Table();
            var replayRandom = new DeterministicRandom(999);
            replay.Tick(PlentyOfTime, replayRandom);
            replay.RecordBet(Bob, miss, 200);
            RunTicks(replay, replayRandom, Ticks);
            Assert.That(replay.WinningNumber, Is.EqualTo(hit), "같은 시드는 같은 결과를 준다");

            var s = RunUntilSettled(replay, replayRandom)[0];

            Assert.That(s.StakedOnWinner, Is.EqualTo(0));
            Assert.That(s.Payout, Is.EqualTo(0));
            Assert.That(s.Net, Is.EqualTo(-200));
            Assert.That(s.Loss, Is.EqualTo(200), "축복 ON_ROULETTE_LOSS 가 이 값을 쓴다");
        }

        [Test]
        public void 딴_판은_손실이_아니다()
        {
            var settlement = new RouletteSettlement(Alice, staked: 100, stakedOnWinner: 100, payout: 2_100);

            Assert.That(settlement.Net, Is.EqualTo(2_000));
            Assert.That(settlement.Loss, Is.EqualTo(0));
        }

        [Test]
        public void 안_건_사람은_정산에_나오지_않는다()
        {
            var table = Table();
            var random = new DeterministicRandom(7);
            table.Tick(PlentyOfTime, random);
            table.RecordBet(Alice, 1, 100);
            RunTicks(table, random, Ticks);

            var settlements = RunUntilSettled(table, random);

            Assert.That(settlements.Length, Is.EqualTo(1));
            Assert.That(settlements[0].PlayerId, Is.EqualTo(Alice));
        }

        // ── 마감과 환불 ─────────────────────────────────────────────────

        [Test]
        public void 돌지_않은_판의_돈은_돌려준다()
        {
            var table = OpenBetting(out var random);
            table.RecordBet(Alice, 1, 300);
            table.RecordBet(Bob, 20, 150);

            // 준비 단계가 끝났다. 시간이 아니라 **바깥에서 끝났다고 알려주는** 경로다 —
            // 진행 중인 판을 시간으로 취소하지 않는 것이 기획 확정이다 (2026-09-10).
            var refunds = table.Close();

            Assert.That(table.State, Is.EqualTo(RouletteState.Closed));
            Assert.That(refunds.Length, Is.EqualTo(2));

            foreach (var r in refunds)
            {
                Assert.That(r.Payout, Is.EqualTo(r.Staked), "낸 만큼 그대로 돌려준다");
                Assert.That(r.Net, Is.EqualTo(0));
                Assert.That(r.Loss, Is.EqualTo(0), "환불은 손실이 아니라 축복 트리거가 안 걸린다");
            }
        }

        [Test]
        public void 마감_시간에는_새_판을_열지_않는다()
        {
            var table = Table();
            var random = new DeterministicRandom(3);

            table.Tick(preparationRemainingTicks: 5, random);

            Assert.That(table.State, Is.EqualTo(RouletteState.Closed));
            Assert.That(table.Cycle, Is.EqualTo(0), "판이 아예 안 열렸다");
        }

        [Test]
        public void 이미_돌기_시작한_판은_마감_시간이어도_끝까지_돈다()
        {
            var table = OpenBetting(out var random);
            table.RecordBet(Alice, 1, 100);
            RunTicks(table, random, Ticks);
            Assert.That(table.State, Is.EqualTo(RouletteState.Spinning));

            // 회전 중에 마감 시간이 됐다.
            for (int i = 0; i < Ticks; i++)
            {
                var settled = table.Tick(preparationRemainingTicks: 1, random);

                if (settled.Length > 0)
                {
                    Assert.That(settled[0].PlayerId, Is.EqualTo(Alice));
                    Assert.That(table.WinningNumber, Is.GreaterThan(0), "결과가 나왔다");
                    return;
                }
            }

            Assert.Fail("회전이 끝나지 않았다 — 마감 때문에 결과 없이 끊겼다면 건 돈이 증발한다");
        }

        // ── 지갑 오염 ───────────────────────────────────────────────────

        [Test]
        public void 환불은_모은_골드_누계를_올리지_않는다()
        {
            var wallet = new Wallet(1_000);
            int inflowBefore = wallet.TotalInflow;

            Assert.That(wallet.TrySpend(300), Is.True);
            wallet.Refund(300);

            Assert.That(wallet.Gold, Is.EqualTo(1_000), "잔액은 되돌아온다");
            Assert.That(wallet.TotalInflow, Is.EqualTo(inflowBefore),
                "환불을 번 돈으로 세면 실제로 벌지 않은 골드가 최종 순위 근거에 섞인다 (S-GD-MNFBV7)");
        }

        [Test]
        public void 당첨금은_모은_골드_누계를_올린다()
        {
            var wallet = new Wallet(1_000);
            int inflowBefore = wallet.TotalInflow;

            wallet.Earn(500);

            Assert.That(wallet.TotalInflow, Is.EqualTo(inflowBefore + 500),
                "실제로 딴 것은 번 돈이다");
        }

        // ── 같은 시드는 같은 결과 ───────────────────────────────────────

        [Test]
        public void 같은_시드는_같은_판을_만든다()
        {
            var first = SpinNumbers(seed: 20260908, rounds: 6);
            var second = SpinNumbers(seed: 20260908, rounds: 6);

            Assert.That(second, Is.EqualTo(first),
                "Host 와 클라이언트가 같은 결과를 봐야 한다");
        }

        // ── 도우미 ──────────────────────────────────────────────────────

        private static RouletteTable OpenBetting(out DeterministicRandom random)
        {
            var table = Table();
            random = new DeterministicRandom(2026);
            table.Tick(PlentyOfTime, random);
            return table;
        }

        private static void RunTicks(RouletteTable table, DeterministicRandom random, int count)
        {
            for (int i = 0; i < count; i++)
            {
                table.Tick(PlentyOfTime, random);
            }
        }

        private static RouletteSettlement[] RunUntilSettled(RouletteTable table, DeterministicRandom random)
        {
            for (int i = 0; i < 1_000; i++)
            {
                var settled = table.Tick(PlentyOfTime, random);

                if (settled.Length > 0)
                {
                    return settled;
                }
            }

            Assert.Fail("정산이 일어나지 않았다");
            return Array.Empty<RouletteSettlement>();
        }

        private static List<int> SpinNumbers(int seed, int rounds)
        {
            var table = Table();
            var random = new DeterministicRandom(seed);
            var numbers = new List<int>();

            while (numbers.Count < rounds)
            {
                table.Tick(PlentyOfTime, random);

                if (table.State == RouletteState.Spinning && !numbers.Contains(-table.Cycle))
                {
                    if (numbers.Count < table.Cycle)
                    {
                        numbers.Add(table.WinningNumber);
                    }
                }
            }

            return numbers;
        }
    }
}
