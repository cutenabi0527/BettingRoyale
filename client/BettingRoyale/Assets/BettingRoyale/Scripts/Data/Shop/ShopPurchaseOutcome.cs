using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 구매 요청 하나를 처리한 결과 (S-CLI-GRZGBK). 어댑터는 <b>판단하지 않고 그대로 발행</b>한다.
    ///
    /// <code>
    /// var r = shopGate.HandlePurchase(playerId, offerId, ticket, isMine);
    /// if (!r.HasEvents) return;
    /// if (r.HasRejection) EventBus.Publish(r.Rejection);
    /// if (r.HasInventoryChange) EventBus.Publish(r.Inventory);
    /// EventBus.Publish(r.Gold);
    /// EventBus.Publish(r.Offers);
    /// </code>
    ///
    /// <para>
    /// ⚠️ <b>이 타입은 발행하지 않는다.</b> <c>EventBus</c> 가 <c>UnityEngine</c> 을 참조하므로
    /// <c>Scripts/Data/</c> 에서는 부를 수 없다 (D-060). 이벤트를 <b>만들어 돌려주기만</b> 한다.
    /// </para>
    /// </summary>
    public readonly struct ShopPurchaseOutcome
    {
        public readonly EconomyRequestVerdict Verdict;

        /// <summary><c>false</c> 면 참가자가 아니라서 아무것도 발행하지 않는다.</summary>
        public readonly bool HasEvents;

        /// <summary>
        /// 항상 발행한다. 실제로 사지 않았으면 <c>Delta</c> 가 0 이다.
        /// <b>0 이어도 보내는 이유</b>: 이 이벤트 하나로 준비 화면의 잔액 줄이 다시 그려진다.
        /// </summary>
        public readonly GoldChanged Gold;

        /// <summary>
        /// 항상 발행한다. 진열 <b>전체</b>를 실으므로 재전송이 공짜다.
        /// 잔액이 바뀌면 살 수 있는 칸도 바뀌므로 <c>Gold</c> 와 짝으로 나간다.
        /// </summary>
        public readonly ShopOffersChanged Offers;

        /// <summary>이번 요청으로 <b>실제로 물건이 들어왔을 때만</b> 있다. 재전송·거절에는 없다.</summary>
        public readonly bool HasInventoryChange;

        public readonly EquipmentInventoryStateChanged Inventory;

        /// <summary>
        /// 사지 못했고 <b>그것이 사용자에게 알릴 만한 일일 때만</b> 참이다.
        /// 재전송은 거절이 아니다 — <see cref="ShopPurchaseRejected"/> 주석 참고.
        /// </summary>
        public readonly bool HasRejection;

        public readonly ShopPurchaseRejected Rejection;

        internal ShopPurchaseOutcome(
            EconomyRequestVerdict verdict,
            bool hasEvents,
            GoldChanged gold,
            ShopOffersChanged offers,
            bool hasInventoryChange,
            EquipmentInventoryStateChanged inventory,
            bool hasRejection,
            ShopPurchaseRejected rejection)
        {
            Verdict = verdict;
            HasEvents = hasEvents;
            Gold = gold;
            Offers = offers;
            HasInventoryChange = hasInventoryChange;
            Inventory = inventory;
            HasRejection = hasRejection;
            Rejection = rejection;
        }
    }
}
