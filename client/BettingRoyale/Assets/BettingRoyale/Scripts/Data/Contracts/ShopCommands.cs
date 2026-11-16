using BettingRoyale.Core;

namespace BettingRoyale.Data.Contracts
{
    // 이 파일의 명령은 화면 → 도메인 방향이다. 규약은 docs/client/ui-domain-contract.md §4·§5.
    //
    // ⚠️ 명령에는 "누가 보냈는가" 를 담지 않는다 (§5, D-079 와 같은 이유).
    //    칸이 있는 한 남의 id 를 적어 남의 골드를 쓸 수 있다. 누가 보냈는지는 어댑터가
    //    연결 정보에서 판단한다 — Host Mode 에서는 RPC 의 송신자다.
    //
    // ⚠️ IGameEvent 를 구현한다 (§4). EventBus.Publish<T> 에 제약이 있어서
    //    이게 없으면 화면이 명령을 보낼 방법이 아예 없다.

    /// <summary>
    /// 상점에서 상품 한 칸을 산다 (S-GD-PVVHP9).
    ///
    /// <para>
    /// <b>아이템 번호가 아니라 진열 칸 번호를 보낸다.</b> 아이템 번호를 받으면 진열되지 않은 물건도
    /// 지목할 수 있다 — 화면이 아는 것과 살 수 있는 것이 어긋나는 순간 그 틈이 곧 공격 경로다.
    /// 칸 번호는 도메인이 진열한 것 안에서만 뜻을 가진다.
    /// </para>
    ///
    /// <para>
    /// <b>수량을 보내지 않는다.</b> 한 칸을 한 번 사는 것이 한 번의 명령이고, 묶음 수량은 진열이 정한다
    /// (<c>ShopOffer.Quantity</c>). 수량을 화면이 정하게 하면 "10개 사기" 한 번으로 재고·잔액 판정이
    /// 부분 성공할 수 있는데, 그 부분 성공을 어떻게 되돌릴지가 정해진 적이 없다.
    /// </para>
    /// </summary>
    public readonly struct ShopPurchaseCommand : IGameEvent
    {
        /// <summary>진열 칸 번호. <c>ShopOffersChanged</c> 로 받은 목록의 <c>OfferId</c> 를 그대로 보낸다.</summary>
        public readonly int OfferId;

        public ShopPurchaseCommand(int offerId) => OfferId = offerId;
    }
}
