using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// <b>방에 하나뿐인 룰렛</b> (S-GD-7VKZA5). 상태와 베팅 장부를 가진다.
    ///
    /// <para>
    /// 사람마다 각자 돌리지 않는다 — 한 판의 결과를 방 전원이 함께 본다.
    /// 각자 하는 것은 베팅뿐이다.
    /// </para>
    ///
    /// <code>
    /// Idle ──▶ Betting ──▶ Spinning ──▶ Result ──┐
    ///           ▲                                 │
    ///           └─────────────────────────────────┘  준비 시간이 남아 있으면 반복
    ///
    /// 남은 시간 ≤ 한 판 + 여유 ──▶ Closed
    /// </code>
    ///
    /// <para>
    /// <b>⚠️ 이 클래스는 지갑을 만지지 않는다.</b> 골드 차감·환불·지급은 게이트가 한다 —
    /// 여기서 만지면 규칙과 경제가 한 덩어리가 되어 따로 검증할 수 없다.
    /// 대신 <b>얼마를 움직여야 하는지</b>를 정확히 돌려준다.
    /// </para>
    ///
    /// <para>
    /// <b>Host 만 돌린다.</b> 결과 난수는 <see cref="DeterministicRandom"/> 이 뽑고
    /// 클라이언트는 복제된 상태를 받는다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class RouletteTable
    {
        /// <summary>한 사람이 이번 판에 건 것. 숫자 → 금액.</summary>
        private readonly Dictionary<int, Dictionary<int, int>> betsByPlayer =
            new Dictionary<int, Dictionary<int, int>>();

        private readonly RouletteWheel wheel;
        private readonly RouletteConfig config;

        private int remainingTicks;

        /// <summary>
        /// 이번 준비 단계가 시작된 뒤 흐른 틱.
        /// <b>첫 판을 늦추는 데만 쓴다</b> — 어댑터가 넘기는 값이 "남은 시간" 뿐이라
        /// "얼마나 지났나" 는 여기서 센다. 그래야 <see cref="Tick"/> 의 인자가 안 바뀐다.
        /// </summary>
        private int elapsedTicks;

        /// <summary>이번 준비 단계에서 판을 한 번이라도 열었는가. 첫 판 지연을 첫 판에만 걸기 위해서다.</summary>
        private bool openedThisPreparation;

        public RouletteTable(RouletteWheel wheel, RouletteConfig config)
        {
            this.wheel = wheel ?? throw new ArgumentNullException(nameof(wheel));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public RouletteState State { get; private set; } = RouletteState.Idle;

        /// <summary>판 번호. 베팅이 열릴 때마다 1 오른다. 화면이 "지난 판" 과 구분하는 데 쓴다.</summary>
        public int Cycle { get; private set; }

        /// <summary>지금 상태가 끝나기까지 남은 틱. <see cref="RouletteState.Idle"/>·<c>Closed</c> 에서는 0.</summary>
        public int RemainingTicks => remainingTicks;

        /// <summary>적중 칸. 회전이 시작될 때 정해진다. 그 전에는 -1.</summary>
        public int WinningSlot { get; private set; } = -1;

        /// <summary>적중 숫자. 회전이 시작될 때 정해진다. 그 전에는 0.</summary>
        public int WinningNumber { get; private set; }

        public RouletteWheel Wheel => wheel;

        public RouletteConfig Config => config;

        /// <summary>지금 이 사람이 이 판에 건 돈 전부.</summary>
        public int StakedBy(int playerId)
        {
            if (!betsByPlayer.TryGetValue(playerId, out var bets))
            {
                return 0;
            }

            int total = 0;

            foreach (var amount in bets.Values)
            {
                total = checked(total + amount);
            }

            return total;
        }

        /// <summary>이 사람이 그 숫자에 건 금액.</summary>
        public int StakedOn(int playerId, int number) =>
            betsByPlayer.TryGetValue(playerId, out var bets) && bets.TryGetValue(number, out int amount)
                ? amount
                : 0;

        /// <summary>이번 판에 건 사람 수. 화면이 "판돈이 모였는지" 를 보이는 데 쓴다.</summary>
        public int BettorCount
        {
            get
            {
                int count = 0;

                foreach (var bets in betsByPlayer.Values)
                {
                    if (bets.Count > 0)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // ── 베팅 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 걸 수 있는지 살펴보고 <b>실제로 걸 금액</b>을 알려준다. <b>상태를 바꾸지 않는다.</b>
        ///
        /// <para>
        /// <b>가진 것보다 많이 걸면 가진 만큼으로 깎아준다.</b> "전부 걸기" 를 눌렀을 때
        /// 거절당하는 것이 더 이상하다. 그래서 <paramref name="acceptedAmount"/> 가
        /// 요청한 값과 다를 수 있고, <b>부르는 쪽은 그 값으로 차감해야 한다.</b>
        /// </para>
        /// </summary>
        public RouletteBetResult InspectBet(int number, int requestedAmount, int gold, out int acceptedAmount)
        {
            acceptedAmount = 0;

            if (State != RouletteState.Betting)
            {
                return RouletteBetResult.NotBetting;
            }

            if (!wheel.Accepts(number))
            {
                return RouletteBetResult.NoSuchNumber;
            }

            if (requestedAmount < config.MinBet)
            {
                return RouletteBetResult.BelowMinimum;
            }

            int amount = requestedAmount > gold ? gold : requestedAmount;

            if (amount < config.MinBet)
            {
                // 가진 돈이 최소 베팅에도 못 미친다. 깎아서도 걸 수 없다.
                return RouletteBetResult.NotEnoughGold;
            }

            acceptedAmount = amount;
            return RouletteBetResult.Success;
        }

        /// <summary>
        /// 장부에 적는다. <b>골드는 부르는 쪽이 이미 뺐어야 한다.</b>
        ///
        /// <para>
        /// <see cref="InspectBet"/> 이 <c>Success</c> 를 준 금액만 넣는다 — 그러지 않으면
        /// 장부와 지갑이 어긋난다. 같은 숫자에 또 걸면 더해진다.
        /// </para>
        /// </summary>
        public void RecordBet(int playerId, int number, int amount)
        {
            if (State != RouletteState.Betting)
            {
                throw new InvalidOperationException(
                    $"베팅을 받지 않는 상태입니다: {State}. InspectBet 을 먼저 확인하세요.");
            }

            if (amount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "베팅 금액은 1 이상이어야 합니다.");
            }

            if (!betsByPlayer.TryGetValue(playerId, out var bets))
            {
                bets = new Dictionary<int, int>();
                betsByPlayer[playerId] = bets;
            }

            bets.TryGetValue(number, out int previous);
            bets[number] = checked(previous + amount);
        }

        /// <summary>
        /// 이 사람의 베팅을 전부 물린다. <b>돌려줄 금액</b>을 알려준다 —
        /// 실제 환불은 부르는 쪽이 한다.
        ///
        /// <para><see cref="RouletteState.Betting"/> 이 아니면 0 이고 아무것도 바꾸지 않는다.</para>
        /// </summary>
        public int WithdrawBets(int playerId)
        {
            if (State != RouletteState.Betting)
            {
                return 0;
            }

            int refund = StakedBy(playerId);
            betsByPlayer.Remove(playerId);
            return refund;
        }

        // ── 진행 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 한 틱 진행한다. <b>Host 만 부른다.</b>
        /// </summary>
        /// <param name="preparationRemainingTicks">
        /// 준비 시간이 얼마나 남았는가. 이 값이 <see cref="RouletteConfig.MinTicksToOpen"/>
        /// (한 판 + 여유) <b>이하면 새 판을 열지 않는다</b> — 룰렛이 돌다가 전투 라운드로
        /// 넘어가는 것을 막는다 (기획 확정 2026-09-10).
        /// </param>
        /// <param name="random">회전 결과를 뽑는다. Host 의 것이어야 모든 피어가 같은 값을 본다.</param>
        /// <returns>
        /// 이 틱에 정산이 일어났으면 그 결과, 아니면 빈 배열.
        /// <b>환불이 일어난 경우도 여기로 나온다</b> — <c>Payout == Staked</c> 인 항목이다.
        /// </returns>
        public RouletteSettlement[] Tick(int preparationRemainingTicks, DeterministicRandom random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (State == RouletteState.Closed)
            {
                return Array.Empty<RouletteSettlement>();
            }

            elapsedTicks++;

            if (remainingTicks > 0)
            {
                remainingTicks--;
            }

            switch (State)
            {
                case RouletteState.Idle:
                    // **한 판을 다 돌고도 여유가 남을 때만 연다** (기획 확정 2026-09-10).
                    //
                    // 남은 시간이 `한 판 + 여유` 이하면 열지 않는다 — 룰렛이 돌다가 전투 라운드로
                    // 넘어가는 것을 막는다. 싸이클을 바꾸면 판정선도 자동으로 따라간다.
                    if (preparationRemainingTicks <= config.MinTicksToOpen)
                    {
                        return CloseAndRefund();
                    }

                    // 첫 판은 늦춘다 — 그 구간은 축복 고르는 시간이다 (기획 확정 2026-09-10).
                    // **판 번호(Cycle)로 판정하지 않는다** — 그 값은 매치 내내 이어지므로
                    // 2라운드부터는 0 이 아니어서 지연이 통째로 사라진다.
                    if (!openedThisPreparation && elapsedTicks < config.StartDelayTicks)
                    {
                        return Array.Empty<RouletteSettlement>();
                    }

                    OpenBetting();
                    return Array.Empty<RouletteSettlement>();

                case RouletteState.Betting:
                    // ⚠️ **시간이 됐다고 진행 중인 판을 취소하지 않는다.**
                    //
                    // 여는 조건이 이미 완주를 보장하므로 여기서 또 시간을 보면 두 조건이 서로 싸운다 —
                    // 예전에는 31초에 열린 판이 20초 지점에서 취소돼서 **화면에는 베팅창이 떴는데
                    // 아무도 돈을 못 거는 유령 판**이 됐다.
                    //
                    // 준비 단계가 밖에서 일찍 끝나는 경우는 Close() 가 받는다.
                    if (remainingTicks <= 0)
                    {
                        StartSpin(random);
                    }

                    return Array.Empty<RouletteSettlement>();

                case RouletteState.Spinning:
                    // ⚠️ 회전은 마감 시간이어도 끝까지 돈다. 결과가 이미 정해졌기 때문이다.
                    return remainingTicks <= 0 ? SettleSpin() : Array.Empty<RouletteSettlement>();

                case RouletteState.Result:
                    if (remainingTicks <= 0)
                    {
                        State = RouletteState.Idle;
                    }

                    return Array.Empty<RouletteSettlement>();

                default:
                    return Array.Empty<RouletteSettlement>();
            }
        }

        /// <summary>
        /// 준비 단계가 끝났다. 진행 중이던 베팅을 전부 돌려준다.
        /// <b>돌지 않은 판에 돈을 먹지 않는다.</b>
        /// </summary>
        public RouletteSettlement[] Close() => CloseAndRefund();

        /// <summary>
        /// <b>새 준비 단계를 연다.</b> 라운드마다 부른다.
        ///
        /// <para>
        /// <b>⚠️ 이게 없으면 1라운드에만 룰렛이 돌고 2·3라운드는 조용히 안 열린다.</b>
        /// 준비 단계가 끝나면 <see cref="RouletteState.Closed"/> 가 되는데,
        /// <see cref="Tick"/> 은 그 상태에서 곧바로 되돌아가므로 <b>스스로는 절대 되살아나지 못한다.</b>
        /// 아무 오류도 로그도 없이 룰렛이 사라지는 종류다.
        /// </para>
        ///
        /// <para>
        /// <b>판 번호는 이어진다.</b> 매치 안에서 계속 오르는 값이라 화면이 "지난 판" 과 구분할 수 있다.
        /// </para>
        /// </summary>
        public void BeginPreparation()
        {
            State = RouletteState.Idle;
            remainingTicks = 0;
            elapsedTicks = 0;
            openedThisPreparation = false;
            WinningSlot = -1;
            WinningNumber = 0;

            // 지난 준비 단계의 베팅이 남아 있으면 안 된다. 정상 흐름에서는 Close 가 이미 비웠지만,
            // 여기서 한 번 더 비우는 편이 안전하다 — 남으면 남의 돈이 새 판에 걸린 채로 시작한다.
            betsByPlayer.Clear();
        }

        private void OpenBetting()
        {
            openedThisPreparation = true;
            Cycle++;
            State = RouletteState.Betting;
            remainingTicks = config.BetTicks;
            WinningSlot = -1;
            WinningNumber = 0;
            betsByPlayer.Clear();
        }

        private void StartSpin(DeterministicRandom random)
        {
            State = RouletteState.Spinning;
            remainingTicks = config.SpinTicks;

            // 칸을 균일하게 뽑는다 — 칸 배치가 곧 확률표다 (S-GD-49WM7S).
            WinningSlot = random.Next(wheel.SlotCount);
            WinningNumber = wheel.NumberAt(WinningSlot);
        }

        private RouletteSettlement[] SettleSpin()
        {
            var settlements = new List<RouletteSettlement>(betsByPlayer.Count);

            foreach (var pair in betsByPlayer)
            {
                int staked = StakedBy(pair.Key);

                if (staked <= 0)
                {
                    continue;
                }

                int onWinner = StakedOn(pair.Key, WinningNumber);
                int payout = onWinner > 0 ? RouletteWheel.PayoutFor(WinningNumber, onWinner) : 0;

                settlements.Add(new RouletteSettlement(pair.Key, staked, onWinner, payout));
            }

            betsByPlayer.Clear();
            State = RouletteState.Result;
            remainingTicks = config.ResultTicks;

            return settlements.ToArray();
        }

        private RouletteSettlement[] CloseAndRefund()
        {
            var refunds = new List<RouletteSettlement>(betsByPlayer.Count);

            foreach (var playerId in betsByPlayer.Keys)
            {
                int staked = StakedBy(playerId);

                if (staked > 0)
                {
                    // 낸 만큼 그대로 돌려준다. 손익 0 이라 축복 손실 트리거도 안 걸린다.
                    refunds.Add(new RouletteSettlement(playerId, staked, 0, staked));
                }
            }

            betsByPlayer.Clear();
            State = RouletteState.Closed;
            remainingTicks = 0;
            WinningSlot = -1;
            WinningNumber = 0;

            return refunds.ToArray();
        }

        public override string ToString() =>
            $"룰렛 {State} · 판 {Cycle} · 남은 {remainingTicks}t" +
            (WinningNumber > 0 ? $" · 적중 {WinningNumber}" : string.Empty);
    }
}
