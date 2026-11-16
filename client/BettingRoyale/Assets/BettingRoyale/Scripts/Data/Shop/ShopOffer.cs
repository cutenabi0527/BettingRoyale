using System;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 상점 진열대의 상품 한 칸 (S-GD-PVVHP9).
    ///
    /// <para>
    /// <b>상품 번호와 아이템 번호를 따로 둔다.</b> 화면이 보내는 것은 <see cref="OfferId"/>(몇 번째 칸)이고
    /// 실제로 지급되는 것은 <see cref="ItemId"/>(무슨 물건)이다. 둘을 합치면 같은 물건을 다른 가격·다른 묶음으로
    /// 두 칸에 놓을 수 없고, 무엇보다 <b>화면이 아이템 번호를 알게 된다</b> — 그러면 진열되지 않은 물건을
    /// 골라 보내는 요청이 만들어질 수 있다. 화면은 칸 번호만 알면 된다.
    /// </para>
    ///
    /// <para>
    /// <b>가격은 여기에 있고 아이템 정의에는 없다.</b> <c>EquipmentItemDefinition</c> 은 축2 소유이고
    /// 위치 인자 생성자 하나뿐이라 칸을 늘리면 모든 호출부가 깨진다. 그리고 같은 물건이 상점 밖(상자·드롭)에서도
    /// 나오므로 "값" 은 물건의 속성이 아니라 <b>진열의 속성</b>이다.
    /// </para>
    ///
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </para>
    /// </summary>
    public readonly struct ShopOffer
    {
        /// <summary>진열 칸 번호. 1부터. 화면이 <c>ShopPurchaseCommand</c> 에 싣는 값이다.</summary>
        public readonly int OfferId;

        /// <summary>지급할 아이템 번호. 장비 카탈로그(<c>IEquipmentItemCatalog</c>)의 키와 같은 값이다.</summary>
        public readonly int ItemId;

        /// <summary>한 번 사는 데 드는 골드. 0 도 허용한다 — 튜토리얼·이벤트용 무료 상품을 막을 이유가 없다.</summary>
        public readonly int Price;

        /// <summary>
        /// 한 번 사면 몇 개를 주는가. 소모품 묶음 판매를 위한 값이다.
        ///
        /// <b>무기·장신구는 반드시 1 이다</b> — <c>EquipmentInventory.TryPickup</c> 이 소모품이 아닌데
        /// 수량이 1 이 아니면 거절한다. 여기서 막지 않으면 상점 목록에는 보이는데 사려고 하면
        /// 항상 거절당하는 칸이 생긴다.
        /// </summary>
        public readonly int Quantity;

        public ShopOffer(int offerId, int itemId, int price, int quantity = 1)
        {
            if (offerId < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offerId), offerId, "상품 칸 번호는 1 이상이어야 합니다.");
            }

            if (itemId < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemId), itemId, "아이템 번호는 1 이상이어야 합니다.");
            }

            if (price < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(price), price, "가격은 음수일 수 없습니다.");
            }

            if (quantity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity), quantity, "수량은 1 이상이어야 합니다.");
            }

            OfferId = offerId;
            ItemId = itemId;
            Price = price;
            Quantity = quantity;
        }

        public override string ToString() =>
            Quantity == 1
                ? $"#{OfferId} item{ItemId} {Price:N0}G"
                : $"#{OfferId} item{ItemId}×{Quantity} {Price:N0}G";
    }
}
