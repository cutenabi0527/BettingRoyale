using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Preparation;

namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 룰렛 요청 하나를 <b>끝까지 판정하는 유일한 자리</b> (S-GD-49WM7S, S-GD-7VKZA5).
    ///
    /// <para>
    /// <c>ShopRequestGate</c> 와 같은 모양이다 — 어댑터는 결과를 받아 <b>그대로 발행만</b> 하고
    /// 규칙을 다시 구현하지 않는다 (D-069, D-079).
    /// </para>
    ///
    /// <para>
    /// <b>역할 분담</b>: <see cref="RouletteTable"/> 은 <b>규칙</b>만 안다(누가 무엇에 얼마를
    /// 걸었고 언제 도는가). <b>골드는 한 푼도 만지지 않는다.</b> 지갑을 만지는 것은 이 게이트뿐이다 —
    /// 나누지 않으면 규칙과 경제를 따로 검증할 수 없다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 정산은 환불과 상금을 나눠서 넣는다.</b> 돌려받는 돈에는 <b>내가 낸 돈이 섞여 있다</b> —
    /// 그 부분은 번 것이 아니므로 <c>Wallet.TotalInflow</c>(최종 순위 동점 처리 5단계,
    /// S-GD-MNFBV7)에 넣으면 안 된다. 그래서 <c>min(회수, 건 돈)</c> 은
    /// <see cref="MatchEconomyState.Refund"/>, 그 초과분만 <see cref="MatchEconomyState.Earn"/> 이다.
    /// <b>합치면 잔액은 맞고 순위만 조용히 틀린다.</b>
    /// </para>
    ///
    /// <para>
    /// ⚠️ <c>playerId</c> 는 <b>반드시 RPC 입력 권한에서 꺼낸 값</b>이어야 한다.
    /// 페이로드에서 온 값을 넘기면 규약 §5 가 없앤 "남의 골드를 거는" 경로가 되살아난다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class RouletteRequestGate
    {
        private readonly MatchEconomyState economy;
        private readonly EconomyRequestLedger ledger;
        private readonly RouletteTable table;

        /// <summary>
        /// ⚠️ <b>구체 <c>PreparationAccessGate</c> 가 아니라 정책 경계로 받는다.</b> 온라인 Host 는
        /// 좌표로 재는 게이트를 넣지만, 네트워크 없는 준비 경로(Local Play · 96_TrainingLab)는
        /// 세션이 직접 접근 권한을 답한다. 구체 타입으로 묶어 두면 그 경로들이 룰렛을 아예 조립할 수
        /// 없어서 <b>"룰렛은 온라인에만 있다" 는 갈라진 계약</b>이 생긴다 — 상점·강화는 이미
        /// <see cref="IPreparationAccessPolicy"/> 로 받고 있었다.
        /// </summary>
        private readonly IPreparationAccessPolicy access;

        /// <param name="ledger">
        /// <b>축복·상점과 같은 원장을 쓴다.</b> 순번은 명령 종류와 무관한 하나짜리 카운터라
        /// (<c>EconomyRequestTicket.Sequence</c> 주석) 룰렛만 따로 원장을 두면 순번이 갈린다.
        /// </param>
        /// <param name="access">
        /// 룰렛 구역 안에 있는지 판정한다 (S-GD-66GTWA, 반경 5m). <b>Host 가 다시 재는 자리다</b> —
        /// 화면이 "가까이 있다" 고 보낸 값을 믿으면 맵 반대편에서 걸 수 있다.
        /// </param>
        public RouletteRequestGate(
            MatchEconomyState economy,
            EconomyRequestLedger ledger,
            RouletteTable table,
            IPreparationAccessPolicy access)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.table = table ?? throw new ArgumentNullException(nameof(table));
            this.access = access ?? throw new ArgumentNullException(nameof(access));
        }

        /// <summary>판정에 쓰인 바퀴. 테스트와 어댑터가 상태를 들여다볼 때 쓴다.</summary>
        public RouletteTable Table => table;

        // ── 화면에 보낼 것 ───────────────────────────────────────────────

        /// <summary>
        /// 바퀴 생김새. <b>준비 단계가 열릴 때 한 번만</b> 발행한다 — 매치 중에 바뀌지 않는다.
        /// </summary>
        public RouletteWheelAnnounced BuildWheel()
        {
            var wheel = table.Wheel;
            var numbers = new List<RouletteNumberView>(wheel.Numbers.Count);

            foreach (var number in wheel.Numbers)
            {
                numbers.Add(new RouletteNumberView(number, wheel.SlotsFor(number), number + 1));
            }

            var layout = new int[wheel.SlotCount];

            for (int i = 0; i < layout.Length; i++)
            {
                layout[i] = wheel.Layout[i];
            }

            return new RouletteWheelAnnounced(layout, numbers.ToArray(), table.Config.MinBet);
        }

        /// <summary>
        /// 바퀴의 현재 상태.
        ///
        /// <b>여기의 <c>CanBet</c> 과 <see cref="HandleBet"/> 의 판정은 같은 조건을 본다.</b>
        /// 어긋나면 "밝게 켜져 있는데 눌리지 않는 버튼" 이 생긴다.
        /// </summary>
        public RouletteStateChanged BuildState(int playerId, bool isMine)
        {
            bool participant = IsParticipant(playerId);
            var bets = participant ? BuildBets(playerId) : Array.Empty<RouletteBetView>();
            int staked = participant ? table.StakedBy(playerId) : 0;

            bool canBet = participant
                && economy.Phase == MatchEconomyPhase.Preparation
                && table.State == RouletteState.Betting
                && access.CanUse(playerId, PreparationFacility.Roulette)
                && economy.GetGold(playerId) >= table.Config.MinBet;

            return new RouletteStateChanged(
                playerId,
                isMine,
                table.State,
                table.Cycle,
                table.RemainingTicks,
                table.WinningSlot,
                table.WinningNumber,
                bets,
                staked,
                table.BettorCount,
                canBet);
        }

        // ── 요청 ────────────────────────────────────────────────────────

        /// <summary>
        /// 숫자 하나에 건다. <b>같은 요청을 두 번 보내도 골드는 한 번만 나가고 베팅도 한 번만 걸린다.</b>
        ///
        /// <para>
        /// <b>가진 것보다 많이 걸면 가진 만큼으로 깎인다</b> — 거절이 아니다. 그래서
        /// <paramref name="amount"/> 와 결과의 <c>BetPlaced.Amount</c> 가 다를 수 있다.
        /// </para>
        /// </summary>
        public RouletteRequestOutcome HandleBet(
            int playerId, int number, int amount, EconomyRequestTicket ticket, bool isMine)
        {
            if (!IsParticipant(playerId))
            {
                return default;
            }

            // ⚠️ 원장은 판정보다 먼저 소비한다. 거절된 요청도 "처리했다" 로 기록된다 —
            //    이유는 EconomyRequestVerdict.Duplicate 주석에 있다.
            var verdict = ledger.Accept(playerId, ticket);

            if (verdict != EconomyRequestVerdict.Fresh)
            {
                // 재전송·지난 요청 — 거절이 아니라 "현재 확정 상태" 를 돌려준다.
                return Snapshot(verdict, playerId, isMine, delta: 0);
            }

            // 처음 보는 요청인데 단계가 닫혀 있다 — 재전송과 달리 **알려야 한다**.
            if (economy.Phase != MatchEconomyPhase.Preparation)
            {
                return Reject(
                    EconomyRequestVerdict.PreparationClosed,
                    playerId, isMine, number, RouletteBetRejectReason.NotPreparation);
            }

            // 룰렛 구역 안에 서 있는가 — Host 가 직접 잰다 (S-GD-66GTWA).
            if (!access.CanUse(playerId, PreparationFacility.Roulette))
            {
                return Reject(verdict, playerId, isMine, number, RouletteBetRejectReason.NotInRange);
            }

            // 규칙 판정은 바퀴가 한다. **여기서는 아무것도 바꾸지 않는다** —
            // 실제로 걸 금액(깎인 뒤의 값)만 받아 온다.
            var inspect = table.InspectBet(number, amount, economy.GetGold(playerId), out int accepted);

            if (inspect != RouletteBetResult.Success)
            {
                return Reject(verdict, playerId, isMine, number, Translate(inspect));
            }

            // 골드를 먼저 뺀다. InspectBet 이 잔액 안으로 깎아 줬으므로 실패할 수 없다.
            if (!economy.TrySpend(playerId, accepted))
            {
                // 여기 오면 **InspectBet 과 이 줄 사이에 잔액이 줄었다**는 뜻이다.
                // 이 게이트는 그 사이에 아무것도 부르지 않으므로 같은 스레드에서는 일어날 수 없다.
                throw new InvalidOperationException(
                    $"베팅 차감이 실패했습니다. player={playerId} 숫자={number} 금액={accepted} " +
                    $"잔액={economy.GetGold(playerId)}. 판정과 차감 사이에 경제를 만진 곳이 있습니다.");
            }

            try
            {
                table.RecordBet(playerId, number, accepted);
            }
            catch
            {
                // ⚠️ **골드는 이미 빠졌다.** 장부에 못 적었으면 그 돈은 어디에도 없다.
                //    실제로 도달하려면 같은 숫자의 누적이 int 를 넘어야 하는데, 골드 자체가
                //    int 로 묶여 있어(Wallet.Add 가 checked) 일어날 수 없다. 그래도 되돌리는 이유는
                //    **"골드는 빠졌는데 베팅은 안 걸린" 상태로는 절대 나가지 않게** 하기 위해서다.
                //    Refund 를 쓰는 것이 중요하다 — Earn 이면 TotalInflow 가 오염된다.
                economy.Refund(playerId, accepted);
                throw;
            }

            var snapshot = Snapshot(verdict, playerId, isMine, delta: -accepted);

            return new RouletteRequestOutcome(
                snapshot.Verdict,
                hasEvents: true,
                gold: snapshot.Gold,
                state: snapshot.State,
                hasBetPlaced: true,
                betPlaced: new RouletteBetPlaced(
                    playerId, isMine, number, accepted, table.StakedBy(playerId)),
                hasRejection: false,
                rejection: default);
        }

        /// <summary>
        /// 이번 판에 건 것을 전부 물린다. <b>전액 돌려받는다</b> (S-GD-7VKZA5 베팅 규칙 6).
        ///
        /// <para>
        /// 돌려주는 돈은 <b>정확히 낸 돈</b>이므로 <see cref="MatchEconomyState.Refund"/> 다.
        /// <c>Earn</c> 이면 안 건 것과 걸었다 무른 것의 순위 근거가 달라진다.
        /// </para>
        /// </summary>
        public RouletteWithdrawOutcome HandleWithdraw(
            int playerId, EconomyRequestTicket ticket, bool isMine)
        {
            if (!IsParticipant(playerId))
            {
                return default;
            }

            var verdict = ledger.Accept(playerId, ticket);

            if (verdict != EconomyRequestVerdict.Fresh)
            {
                return WithdrawSnapshot(verdict, playerId, isMine, delta: 0, GoldChangeReason.Refunded);
            }

            if (economy.Phase != MatchEconomyPhase.Preparation)
            {
                return RejectWithdraw(
                    EconomyRequestVerdict.PreparationClosed,
                    playerId, isMine, RouletteWithdrawRejectReason.NotPreparation);
            }

            // ⚠️ 구역 밖이어도 물리기는 막지 않는다. 물리기는 **돈을 돌려받는 쪽**이라
            //    막아서 지킬 것이 없고, 오히려 창이 닫히는 순간과 겹치면 돈이 묶인다.

            if (table.State != RouletteState.Betting)
            {
                return RejectWithdraw(
                    verdict, playerId, isMine, RouletteWithdrawRejectReason.NotBetting);
            }

            int refund = table.WithdrawBets(playerId);

            if (refund <= 0)
            {
                return RejectWithdraw(
                    verdict, playerId, isMine, RouletteWithdrawRejectReason.NothingToWithdraw);
            }

            // ⚠️ Refund 이지 Earn 이 아니다. 돌려받은 돈은 번 것이 아니므로 TotalInflow 에
            //    들어가면 안 된다 — 넣으면 잔액은 맞고 최종 순위만 조용히 틀린다.
            economy.Refund(playerId, refund);

            return WithdrawSnapshot(
                verdict, playerId, isMine, delta: refund, GoldChangeReason.Refunded);
        }

        // ── 진행 ────────────────────────────────────────────────────────

        /// <summary>
        /// 바퀴를 한 틱 돌리고 <b>정산까지 지갑에 반영</b>한다. <b>Host 만 부른다.</b>
        /// </summary>
        /// <param name="preparationRemainingTicks">
        /// 준비 시간이 얼마나 남았는가. 이 값이 마감 리드타임 이하면 새 판을 열지 않는다.
        /// </param>
        /// <param name="random">회전 결과를 뽑는다. Host 의 것이어야 모든 피어가 같은 값을 본다.</param>
        /// <param name="viewerId">지금 화면을 보는 사람. 상태 이벤트를 이 사람 기준으로 만든다.</param>
        public RouletteTickOutcome Tick(
            int preparationRemainingTicks, DeterministicRandom random, int viewerId, bool isMine)
        {
            var before = table.State;
            var settlements = table.Tick(preparationRemainingTicks, random);

            return Apply(settlements, before, viewerId, isMine);
        }

        /// <summary>
        /// <b>새 준비 단계를 연다.</b> 라운드마다 <see cref="MatchEconomyState.BeginPreparation"/> 옆에서 부른다.
        ///
        /// <para>
        /// <b>⚠️ 빠뜨리면 1라운드에만 룰렛이 돌고 2·3라운드는 조용히 안 열린다.</b>
        /// 준비 단계가 끝나면 바퀴가 <c>Closed</c> 가 되고 <b>스스로는 되살아나지 못한다.</b>
        /// </para>
        /// </summary>
        public RouletteStateChanged BeginPreparation(int viewerId, bool isMine)
        {
            table.BeginPreparation();
            return BuildState(viewerId, isMine);
        }

        /// <summary>
        /// 준비 단계가 끝났다. 진행 중이던 베팅을 전부 돌려준다 —
        /// <b>돌지 않은 판에 돈을 먹지 않는다.</b>
        /// </summary>
        public RouletteTickOutcome Close(int viewerId, bool isMine)
        {
            var before = table.State;
            var settlements = table.Close();

            return Apply(settlements, before, viewerId, isMine);
        }

        // ── 안쪽 ────────────────────────────────────────────────────────

        /// <summary>
        /// 정산 결과를 지갑에 넣고 발행할 이벤트를 만든다.
        ///
        /// <para>
        /// <b>환불분과 상금분을 나눈다.</b> 회수액에는 내가 낸 돈이 섞여 있는데, 그 부분은
        /// 번 것이 아니다 — 이유는 클래스 주석에 있다.
        /// </para>
        /// </summary>
        private RouletteTickOutcome Apply(
            RouletteSettlement[] settlements, RouletteState before, int viewerId, bool isMine)
        {
            // 환불이면 바퀴가 돌지 않았다 — 그때 적중 숫자는 0 이고 칸은 -1 이다.
            // Payout == Staked 로 판정하면 안 된다: 여러 숫자에 나눠 걸어 본전인 판도 같은 값이다.
            bool refundRound = table.WinningNumber == 0;

            var settled = new RouletteSettled[settlements.Length];
            var golds = new GoldChanged[settlements.Length];

            for (int i = 0; i < settlements.Length; i++)
            {
                var s = settlements[i];

                int refundPart = s.Payout < s.Staked ? s.Payout : s.Staked;
                int earnPart = s.Payout - refundPart;

                if (refundPart > 0)
                {
                    economy.Refund(s.PlayerId, refundPart);
                }

                if (earnPart > 0)
                {
                    economy.Earn(s.PlayerId, earnPart);
                }

                // 룰렛 환급 축복 — 잃은 금액의 일부를 돌려받는다. 판이 무효(환불)면 잃은 것이 없다.
                int lossRefund = refundRound ? 0 : economy.RefundRouletteLoss(s.PlayerId, s.Loss);

                bool mine = isMine && s.PlayerId == viewerId;

                settled[i] = new RouletteSettled(
                    s.PlayerId,
                    mine,
                    table.Cycle,
                    refundRound ? -1 : table.WinningSlot,
                    table.WinningNumber,
                    s.Staked,
                    s.StakedOnWinner,
                    s.Payout + lossRefund,
                    s.Net + lossRefund,
                    refundRound ? 0 : s.Loss,
                    refundRound);

                golds[i] = economy.BuildGoldChanged(
                    s.PlayerId,
                    mine,
                    s.Payout + lossRefund,
                    earnPart > 0 ? GoldChangeReason.Earned : GoldChangeReason.Refunded);
            }

            bool changed = before != table.State || settlements.Length > 0;

            return new RouletteTickOutcome(
                changed,
                changed ? BuildState(viewerId, isMine) : default,
                settled,
                golds);
        }

        private RouletteBetView[] BuildBets(int playerId)
        {
            var wheel = table.Wheel;
            var bets = new List<RouletteBetView>(wheel.Numbers.Count);

            foreach (var number in wheel.Numbers)
            {
                int amount = table.StakedOn(playerId, number);

                if (amount > 0)
                {
                    bets.Add(new RouletteBetView(number, amount));
                }
            }

            return bets.Count == 0 ? Array.Empty<RouletteBetView>() : bets.ToArray();
        }

        /// <summary>거절·재전송에도 현재 상태를 실어 보낸다 — 화면이 이벤트 하나로 다시 그려지게.</summary>
        private RouletteRequestOutcome Snapshot(
            EconomyRequestVerdict verdict, int playerId, bool isMine, int delta) =>
            new RouletteRequestOutcome(
                verdict,
                hasEvents: true,
                gold: economy.BuildGoldChanged(playerId, isMine, delta, GoldChangeReason.Spent),
                state: BuildState(playerId, isMine),
                hasBetPlaced: false,
                betPlaced: default,
                hasRejection: false,
                rejection: default);

        private RouletteWithdrawOutcome WithdrawSnapshot(
            EconomyRequestVerdict verdict,
            int playerId,
            bool isMine,
            int delta,
            GoldChangeReason reason) =>
            new RouletteWithdrawOutcome(
                verdict,
                hasEvents: true,
                gold: economy.BuildGoldChanged(playerId, isMine, delta, reason),
                state: BuildState(playerId, isMine),
                hasRejection: false,
                rejection: default);

        private RouletteWithdrawOutcome RejectWithdraw(
            EconomyRequestVerdict verdict,
            int playerId,
            bool isMine,
            RouletteWithdrawRejectReason reason)
        {
            var snapshot = WithdrawSnapshot(
                verdict, playerId, isMine, delta: 0, GoldChangeReason.Refunded);

            return new RouletteWithdrawOutcome(
                snapshot.Verdict,
                hasEvents: true,
                gold: snapshot.Gold,
                state: snapshot.State,
                hasRejection: true,
                rejection: new RouletteWithdrawRejected(playerId, isMine, reason));
        }

        private RouletteRequestOutcome Reject(
            EconomyRequestVerdict verdict,
            int playerId,
            bool isMine,
            int number,
            RouletteBetRejectReason reason)
        {
            var snapshot = Snapshot(verdict, playerId, isMine, delta: 0);

            return new RouletteRequestOutcome(
                snapshot.Verdict,
                hasEvents: true,
                gold: snapshot.Gold,
                state: snapshot.State,
                hasBetPlaced: false,
                betPlaced: default,
                hasRejection: true,
                rejection: new RouletteBetRejected(playerId, isMine, number, reason));
        }

        /// <summary>바퀴의 판정을 화면이 읽을 수 있는 사유로 옮긴다.</summary>
        private static RouletteBetRejectReason Translate(RouletteBetResult result)
        {
            switch (result)
            {
                case RouletteBetResult.NotBetting:
                    return RouletteBetRejectReason.NotBetting;

                case RouletteBetResult.NoSuchNumber:
                    return RouletteBetRejectReason.NoSuchNumber;

                case RouletteBetResult.BelowMinimum:
                    return RouletteBetRejectReason.BelowMinimum;

                case RouletteBetResult.NotEnoughGold:
                    return RouletteBetRejectReason.NotEnoughGold;

                default:
                    // Success 는 여기 오지 않는다. 새 값이 생기면 컴파일러가 잡아 주지 않으므로
                    // "지금은 걸 수 없다" 로 둔다 — 사용자가 다시 눌러 볼 수 있는 쪽이 덜 해롭다.
                    return RouletteBetRejectReason.NotBetting;
            }
        }

        private bool IsParticipant(int playerId)
        {
            var ids = economy.PlayerIds;

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == playerId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
