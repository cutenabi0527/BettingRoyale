using System;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 베팅·물리기 요청 하나를 처리한 결과 (S-GD-7VKZA5). 어댑터는 <b>판단하지 않고 그대로 발행</b>한다.
    ///
    /// <code>
    /// var r = rouletteGate.HandleBet(playerId, number, amount, ticket, isMine);
    /// if (!r.HasEvents) return;
    /// if (r.HasRejection) EventBus.Publish(r.Rejection);
    /// if (r.HasBetPlaced) EventBus.Publish(r.BetPlaced);
    /// EventBus.Publish(r.Gold);
    /// EventBus.Publish(r.State);
    /// </code>
    ///
    /// <para>
    /// ⚠️ <b>이 타입은 발행하지 않는다.</b> <c>EventBus</c> 가 <c>UnityEngine</c> 을 참조하므로
    /// <c>Scripts/Data/</c> 에서는 부를 수 없다 (D-060). 이벤트를 <b>만들어 돌려주기만</b> 한다.
    /// </para>
    /// </summary>
    public readonly struct RouletteRequestOutcome
    {
        public readonly EconomyRequestVerdict Verdict;

        /// <summary><c>false</c> 면 참가자가 아니라서 아무것도 발행하지 않는다.</summary>
        public readonly bool HasEvents;

        /// <summary>
        /// 항상 발행한다. 실제로 걸지 않았으면 <c>Delta</c> 가 0 이다.
        /// <b>0 이어도 보내는 이유</b>: 이 이벤트 하나로 준비 화면의 잔액 줄이 다시 그려진다.
        /// </summary>
        public readonly GoldChanged Gold;

        /// <summary>항상 발행한다. 바퀴 상태 <b>전체</b>를 실으므로 재전송이 공짜다.</summary>
        public readonly RouletteStateChanged State;

        /// <summary>이번 요청으로 <b>실제로 돈이 걸렸을 때만</b> 있다. 재전송·거절·물리기에는 없다.</summary>
        public readonly bool HasBetPlaced;

        public readonly RouletteBetPlaced BetPlaced;

        /// <summary>
        /// 걸지 못했고 <b>그것이 사용자에게 알릴 만한 일일 때만</b> 참이다.
        /// 재전송은 거절이 아니다 — <see cref="RouletteBetRejected"/> 주석 참고.
        /// </summary>
        public readonly bool HasRejection;

        public readonly RouletteBetRejected Rejection;

        internal RouletteRequestOutcome(
            EconomyRequestVerdict verdict,
            bool hasEvents,
            GoldChanged gold,
            RouletteStateChanged state,
            bool hasBetPlaced,
            RouletteBetPlaced betPlaced,
            bool hasRejection,
            RouletteBetRejected rejection)
        {
            Verdict = verdict;
            HasEvents = hasEvents;
            Gold = gold;
            State = state;
            HasBetPlaced = hasBetPlaced;
            BetPlaced = betPlaced;
            HasRejection = hasRejection;
            Rejection = rejection;
        }
    }

    /// <summary>
    /// 물리기 요청 하나를 처리한 결과 (S-GD-7VKZA5).
    ///
    /// <code>
    /// var r = rouletteGate.HandleWithdraw(playerId, ticket, isMine);
    /// if (!r.HasEvents) return;
    /// if (r.HasRejection) EventBus.Publish(r.Rejection);
    /// EventBus.Publish(r.Gold);
    /// EventBus.Publish(r.State);
    /// </code>
    ///
    /// <para>
    /// <b>베팅과 결과 타입이 다른 이유</b>: 거절 사유의 종류가 다르다. 하나로 합치면
    /// 화면이 이 요청에서는 오지 않는 사유까지 다뤄야 한다.
    /// </para>
    /// </summary>
    public readonly struct RouletteWithdrawOutcome
    {
        public readonly EconomyRequestVerdict Verdict;

        /// <summary><c>false</c> 면 참가자가 아니라서 아무것도 발행하지 않는다.</summary>
        public readonly bool HasEvents;

        /// <summary>
        /// 항상 발행한다. 실제로 물리지 않았으면 <c>Delta</c> 가 0 이다.
        /// 돌려받았으면 <c>Reason</c> 이 <see cref="GoldChangeReason.Refunded"/> 다 —
        /// <b><see cref="GoldChangeReason.Earned"/> 가 아니다.</b>
        /// </summary>
        public readonly GoldChanged Gold;

        /// <summary>항상 발행한다. 바퀴 상태 <b>전체</b>를 실으므로 재전송이 공짜다.</summary>
        public readonly RouletteStateChanged State;

        /// <summary>물리지 못했고 그것이 사용자에게 알릴 만한 일일 때만 참이다.</summary>
        public readonly bool HasRejection;

        public readonly RouletteWithdrawRejected Rejection;

        internal RouletteWithdrawOutcome(
            EconomyRequestVerdict verdict,
            bool hasEvents,
            GoldChanged gold,
            RouletteStateChanged state,
            bool hasRejection,
            RouletteWithdrawRejected rejection)
        {
            Verdict = verdict;
            HasEvents = hasEvents;
            Gold = gold;
            State = state;
            HasRejection = hasRejection;
            Rejection = rejection;
        }
    }

    /// <summary>
    /// 바퀴를 한 틱 돌린 결과 (S-GD-7VKZA5).
    ///
    /// <code>
    /// var r = rouletteGate.Tick(remainingTicks, random, viewerId, isMine);
    /// for (int i = 0; i &lt; r.Settlements.Length; i++) EventBus.Publish(r.Settlements[i]);
    /// for (int i = 0; i &lt; r.Golds.Length; i++)        EventBus.Publish(r.Golds[i]);
    /// if (r.HasStateChange) EventBus.Publish(r.State);
    /// </code>
    ///
    /// <para>
    /// <b>정산은 방 전원분이 나온다.</b> 한 판의 결과를 모두가 함께 보기 때문이다 —
    /// 그래서 배열이고, 각 항목의 <c>IsMine</c> 은 이미 채워져 있다.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="HasStateChange"/> 가 거짓이면 상태 이벤트를 발행하지 않는다.</b>
    /// 매 틱 발행하면 방 인원수만큼 이벤트가 매 틱 늘어난다. 남은 시간 카운트다운은 화면이 센다.
    /// </para>
    /// </summary>
    public readonly struct RouletteTickOutcome
    {
        /// <summary>이번 틱에 상태가 바뀌었거나 정산이 일어났다.</summary>
        public readonly bool HasStateChange;

        /// <summary>보는 사람 기준의 바퀴 상태. <see cref="HasStateChange"/> 일 때만 발행한다.</summary>
        public readonly RouletteStateChanged State;

        /// <summary>이번 틱의 정산 전부. <b><c>null</c> 이 아니다</b> — 없으면 길이 0.</summary>
        public readonly RouletteSettled[] Settlements;

        /// <summary>
        /// 정산으로 골드가 변한 사람들. <see cref="Settlements"/> 와 <b>같은 순서·같은 길이</b>다.
        /// <b><c>null</c> 이 아니다.</b>
        /// </summary>
        public readonly GoldChanged[] Golds;

        internal RouletteTickOutcome(
            bool hasStateChange,
            RouletteStateChanged state,
            RouletteSettled[] settlements,
            GoldChanged[] golds)
        {
            HasStateChange = hasStateChange;
            State = state;
            Settlements = settlements ?? Array.Empty<RouletteSettled>();
            Golds = golds ?? Array.Empty<GoldChanged>();
        }
    }
}
