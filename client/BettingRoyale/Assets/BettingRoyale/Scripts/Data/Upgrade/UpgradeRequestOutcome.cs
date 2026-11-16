using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화 요청 하나를 처리한 결과 (S-GD-A2YDDJ). 어댑터는 <b>판단하지 않고 그대로 발행</b>한다.
    ///
    /// <code>
    /// var r = upgradeGate.HandleUpgrade(playerId, payload, ticket, isMine);
    /// if (!r.HasEvents) return;
    /// if (r.HasRejection) EventBus.Publish(r.Rejection);
    /// EventBus.Publish(r.Gold);
    /// EventBus.Publish(r.State);
    /// </code>
    ///
    /// <para>
    /// <c>ShopPurchaseOutcome</c> 과 같은 모양이다 — 성공 이벤트가 따로 없고
    /// <b>바뀐 상태와 골드가 곧 성공</b>이다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>이 타입은 발행하지 않는다.</b> <c>EventBus</c> 가 <c>UnityEngine</c> 을 참조하므로
    /// <c>Scripts/Data/</c> 에서는 부를 수 없다 (D-060).
    /// </para>
    /// </summary>
    public readonly struct UpgradeRequestOutcome
    {
        public readonly EconomyRequestVerdict Verdict;

        /// <summary><c>false</c> 면 참가자가 아니라서 아무것도 발행하지 않는다.</summary>
        public readonly bool HasEvents;

        /// <summary>항상 발행한다. 올리지 못했으면 <c>Delta</c> 가 0 이다.</summary>
        public readonly GoldChanged Gold;

        /// <summary>항상 발행한다. <b>두 부위 전체</b>가 실리므로 재전송이 공짜다.</summary>
        public readonly UpgradeStateChanged State;

        /// <summary>
        /// 올리지 못했고 <b>그것이 사용자에게 알릴 만한 일일 때만</b> 참이다.
        /// 재전송은 거절이 아니다 — 이미 한 번 처리된 클릭이다.
        /// </summary>
        public readonly bool HasRejection;

        public readonly UpgradeSlotRejected Rejection;

        /// <summary>
        /// 시도는 이루어졌지만 <b>확률 판정에서 떨어졌다</b> (S15P21A606-304). 골드는 나갔고 단계는 그대로다.
        /// <b>거절이 아니므로 <see cref="HasRejection"/> 은 거짓</b>이고 발행할 이벤트도 따로 없다 —
        /// 화면은 상태의 앞뒤로 가른다(S-UX-HQ92B5). 이 값은 로그·훈련 안내처럼 <b>권위 쪽이 직접 말할 때</b>만 쓴다.
        /// </summary>
        public readonly bool ChanceFailed;

        internal UpgradeRequestOutcome(
            EconomyRequestVerdict verdict,
            bool hasEvents,
            in GoldChanged gold,
            in UpgradeStateChanged state,
            bool hasRejection,
            in UpgradeSlotRejected rejection,
            bool chanceFailed = false)
        {
            Verdict = verdict;
            HasEvents = hasEvents;
            Gold = gold;
            State = state;
            HasRejection = hasRejection;
            Rejection = rejection;
            ChanceFailed = chanceFailed;
        }
    }
}
