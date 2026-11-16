using System;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Roulette;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// Host 가 보낸 룰렛 스냅샷. <b>클라이언트가 결과를 미리 계산하지 않는다</b> —
    /// <see cref="TrainingSessionView"/> 의 다른 칸과 같은 규칙이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 값은 전부 제품 게이트(<see cref="RouletteRequestGate.BuildState"/>)가 만든 것을 옮긴 것이다.
    /// 훈련 전용 판정을 여기서 만들지 않는다.
    /// </para>
    /// <para>
    /// <b>바퀴 배치는 싣지 않는다.</b> 매치 중 바뀌지 않는 25칸 배열을 매 스냅샷마다 보내면
    /// 회선을 채운다 — 제품이 <c>RouletteWheelAnnounced</c> 를 따로 둔 것과 같은 이유다.
    /// 훈련 화면은 숫자·배당만 필요하므로 <see cref="Numbers"/>·<see cref="Payouts"/> 만 나른다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public sealed class TrainingRouletteView
    {
        /// <summary>바퀴가 없는 상태. <b>절대 <c>null</c> 을 쓰지 않는다.</b></summary>
        public static readonly TrainingRouletteView Empty = new(
            RouletteState.Idle, 0, 0, 0, 0, 0, 0, false, 0,
            Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());

        /// <summary>훈련 스냅샷이 실을 수 있는 최대 숫자 종류. 확정 배치는 5종이다.</summary>
        public const int MaxNumbers = 16;

        public TrainingRouletteView(
            RouletteState state,
            int cycle,
            int remainingTicks,
            int winningNumber,
            int myStakeTotal,
            int bettorCount,
            int minBet,
            bool canBet,
            int lastNet,
            int[] numbers,
            int[] payouts,
            int[] myStakes)
        {
            State = state;
            Cycle = cycle;
            RemainingTicks = remainingTicks;
            WinningNumber = winningNumber;
            MyStakeTotal = myStakeTotal;
            BettorCount = bettorCount;
            MinBet = minBet;
            CanBet = canBet;
            LastNet = lastNet;
            Numbers = numbers ?? Array.Empty<int>();
            Payouts = payouts ?? Array.Empty<int>();
            MyStakes = myStakes ?? Array.Empty<int>();
        }

        public RouletteState State { get; }
        public int Cycle { get; }
        public int RemainingTicks { get; }

        /// <summary>
        /// 적중 숫자. <b><see cref="RouletteState.Result"/> 에서만 0 이 아니다</b> —
        /// 회전 중에 밝히면 바퀴가 돌기 시작하는 순간 정답이 뜬다.
        /// </summary>
        public int WinningNumber { get; }

        public int MyStakeTotal { get; }
        public int BettorCount { get; }
        public int MinBet { get; }

        /// <summary>진열의 "걸 수 있음". 게이트의 판정을 그대로 옮긴 값이다.</summary>
        public bool CanBet { get; }

        /// <summary>마지막으로 끝난 판의 순손익. 화면이 "따고 잃음" 한 줄을 그리는 데 쓴다.</summary>
        public int LastNet { get; }

        /// <summary>걸 수 있는 숫자.</summary>
        public int[] Numbers { get; }

        /// <summary>같은 순서의 총 회수 배수(숫자 + 1).</summary>
        public int[] Payouts { get; }

        /// <summary>같은 순서의 내 베팅 누계. 안 걸었으면 0이다.</summary>
        public int[] MyStakes { get; }

        /// <summary>제품 게이트가 만든 상태를 훈련 스냅샷으로 옮긴다.</summary>
        public static TrainingRouletteView Capture(
            in RouletteStateChanged state, in RouletteWheelAnnounced wheel, int lastNet)
        {
            int count = Math.Min(wheel.Numbers.Length, MaxNumbers);
            var numbers = new int[count];
            var payouts = new int[count];
            var stakes = new int[count];

            for (int i = 0; i < count; i++)
            {
                numbers[i] = wheel.Numbers[i].Number;
                payouts[i] = wheel.Numbers[i].PayoutMultiplier;
                stakes[i] = FindStake(state.MyBets, numbers[i]);
            }

            return new TrainingRouletteView(
                state.State,
                state.Cycle,
                state.RemainingTicks,
                // 회전 중에는 계약상 결과가 실려 있지만 화면에는 아직 밝히지 않는다.
                state.State == RouletteState.Result ? state.WinningNumber : 0,
                state.MyStakeTotal,
                state.BettorCount,
                wheel.MinBet,
                state.CanBet,
                lastNet,
                numbers,
                payouts,
                stakes);
        }

        private static int FindStake(RouletteBetView[] bets, int number)
        {
            if (bets == null) return 0;

            for (int i = 0; i < bets.Length; i++)
            {
                if (bets[i].Number == number) return bets[i].Amount;
            }

            return 0;
        }
    }
}
