using System;
using BettingRoyale.Core;
using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// 상점 진열대 한 칸을 화면이 그리는 데 필요한 것 전부 (S-GD-PVVHP9).
    ///
    /// <para>
    /// <b>이름이 <c>~View</c> 인 이유</b>: 도메인의 <c>ShopOffer</c> 와 칸이 겹치지만 같은 타입이 아니다.
    /// 도메인 타입을 그대로 계약에 실으면 도메인을 고칠 때마다 화면이 깨진다
    /// (<c>BlessingCandidateView</c> 가 같은 이유로 분리돼 있다).
    /// </para>
    /// </summary>
    public readonly struct ShopOfferView
    {
        /// <summary>진열 칸 번호. 화면은 이 값을 <see cref="ShopPurchaseCommand"/> 에 그대로 싣는다.</summary>
        public readonly int OfferId;

        /// <summary>지급될 아이템 번호. 화면이 아이콘·이름을 찾는 데 쓴다.</summary>
        public readonly int ItemId;

        /// <summary>
        /// 이 칸이 파는 것의 종류. 화면이 진열대를 나눠 그리는 데 쓴다.
        ///
        /// <para>
        /// <b>⚠️ <see cref="EquipmentItemKind.None"/> 이 올 수 있다.</b> 진열에는 있는데 그 아이템의
        /// 정의가 없다는 뜻이다 — 가격표와 장비 카탈로그가 다른 곳에 살아서 어긋날 수 있다.
        /// 그때 <see cref="CanAfford"/> 도 <c>false</c> 이므로 살 수는 없지만, <b>화면이
        /// <c>Consumable</c>·<c>Accessory</c> 두 값만 있다고 가정하면 그 칸을 한 칸도 못 그린다.</b>
        /// </para>
        ///
        /// <para>
        /// <see cref="EquipmentItemKind.Weapon"/> 은 오지 않는다 — 상점은 무기를 팔지 않고
        /// (S-GD-PVVHP9 규칙 2) 게이트가 무기 칸을 진열 단계에서 거른다.
        /// </para>
        /// </summary>
        public readonly EquipmentItemKind Kind;

        public readonly int Price;

        /// <summary>한 번 사면 몇 개인가. 1 이면 화면이 수량을 표시하지 않아도 된다.</summary>
        public readonly int Quantity;

        /// <summary>
        /// 지금 이 사람이 이 칸을 살 수 있는가.
        ///
        /// <b>잔액만 보는 값이 아니다</b> — 준비 단계가 아니거나 칸이 비면 여기서 <c>false</c> 가 된다.
        /// 화면은 이 값으로 버튼을 흐리게 하고, 그래도 눌렸을 때의 사유는
        /// <see cref="ShopPurchaseRejected"/> 로 온다. <b>둘 다 필요하다</b> — 이 값만 믿고 요청을
        /// 안 보내면 도메인이 판정할 기회가 없고, 이 값이 없으면 살 수 없는 버튼이 멀쩡해 보인다.
        /// </summary>
        public readonly bool CanAfford;

        /// <summary>
        /// <b>이 칸을 지금 사면 그 물건의 보유 한도를 넘는가</b> (S-GD-FDHBNA).
        ///
        /// <para>
        /// 화면은 이 값이 <c>true</c> 인 칸을 <b>비활성으로 그리고 사유를 그 자리에 적는다</b> —
        /// 문구는 <b>"최대 보유 갯수 이상 구매할 수 없습니다"</b> 다. 비활성만 하고 문구를 빼면
        /// 지금보다 나빠진다(스펙의 경고) — 지금은 눌러서
        /// <see cref="ShopPurchaseRejectReason.CarryLimitReached"/> 라도 받는다.
        /// </para>
        ///
        /// <para>
        /// <b>⚠️ <see cref="CanAfford"/> 와 다른 축이다.</b> 한도에 닿아도 돈은 있을 수 있어
        /// <c>CanAfford</c> 는 <c>true</c> 로 남는다. 둘을 하나로 합치면 화면이 <b>"돈이 없다"</b> 와
        /// <b>"더 못 든다"</b> 를 구분할 수 없게 되고, 그 구분이 없어서 생긴 거짓 안내가 바로
        /// 이 스펙이 없애려던 것이다.
        /// </para>
        ///
        /// <para>
        /// <b>⚠️ 상태이지 이력이 아니다.</b> Host 는 진열을 만들 때마다 <b>그 순간의 보유 개수</b>와
        /// 한도를 다시 비교해서 이 값을 채운다. "한도에 닿았다" 를 기억해 두지 않는다 — 구매
        /// 되돌리기가 생겨 개수가 줄어도 기억해 둔 플래그는 비활성인 채로 남기 때문이다.
        /// </para>
        ///
        /// <para>
        /// <b>소모품에만 붙는다.</b> 장신구는 한 개만 낄 수 있고 이미
        /// <see cref="ShopPurchaseRejectReason.AlreadyOwned"/> 가 막는다. 한도를 알 수 없는 경우
        /// (아이템 정의 없음·인벤토리가 아직 없음)는 <c>false</c> 다 — <b>모르는 것을 "못 산다" 로
        /// 그리지 않는다.</b> 최종 판정은 어차피 Host 가 다시 한다.
        /// </para>
        /// </summary>
        public readonly bool CarryLimitReached;

        public ShopOfferView(
            int offerId,
            int itemId,
            EquipmentItemKind kind,
            int price,
            int quantity,
            bool canAfford,
            bool carryLimitReached)
        {
            OfferId = offerId;
            ItemId = itemId;
            Kind = kind;
            Price = price;
            Quantity = quantity;
            CanAfford = canAfford;
            CarryLimitReached = carryLimitReached;
        }
    }

    /// <summary>
    /// 상점 진열대의 <b>현재 확정 상태 전체</b> (S-GD-PVVHP9).
    ///
    /// <para>
    /// <b>바뀐 칸만 보내지 않고 목록 전체를 싣는다.</b> 이벤트 하나를 놓쳐도 화면이 실제와 어긋나지
    /// 않게 하기 위해서다 (<c>client/AGENTS.md</c> 7절, <c>BlessingOfferChanged</c> 와 같은 이유).
    /// 목록이 6칸이면 6칸 전부가 매번 온다 — 낭비처럼 보이지만 재전송이 공짜가 된다.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="Offers"/> 는 절대 <c>null</c> 이 아니다.</b> 팔 것이 없으면 길이 0 이다.
    /// 화면이 <c>null</c> 검사를 잊는 것은 늘 생기고, 그때 터지는 자리가 도메인이 아니라 화면이라
    /// 원인을 찾기 어렵다.
    /// </para>
    /// </summary>
    public readonly struct ShopOffersChanged : IGameEvent
    {
        /// <summary><c>Scripts/Network/</c> 의 <c>PlayerSlot.PlayerId</c> 와 같은 값.</summary>
        public readonly int PlayerId;

        /// <summary>지금 화면을 보는 사람의 것인가. 정본 설명은 <see cref="GoldChanged.IsMine"/> 에 있다.</summary>
        public readonly bool IsMine;

        /// <summary>진열 순서대로. 길이 0 일 수 있고 <c>null</c> 은 아니다.</summary>
        public readonly ShopOfferView[] Offers;

        public ShopOffersChanged(int playerId, bool isMine, ShopOfferView[] offers)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Offers = offers ?? Array.Empty<ShopOfferView>();
        }
    }

    /// <summary>
    /// 구매가 거절된 이유 (S-GD-PVVHP9).
    ///
    /// <b>⚠️ 값은 뒤에만 붙인다.</b> 이 열거형은 계약에 실려 회선을 넘을 수 있다.
    /// 중간에 끼우면 이미 배포된 값의 뜻이 밀린다.
    /// </summary>
    public enum ShopPurchaseRejectReason
    {
        /// <summary>그런 진열 칸이 없다. 목록에 없는 번호를 보냈거나 목록이 바뀌었다.</summary>
        NoSuchOffer = 0,

        /// <summary>
        /// 준비 단계가 아니다. 전투 중에는 살 수 없다.
        ///
        /// <b>재전송과 구분된다.</b> 같은 요청을 두 번 보낸 것은 거절이 아니라 조용한 현재 상태이고,
        /// 이 값은 <b>처음 보내는 요청인데 단계가 닫혀 있을 때만</b> 나간다.
        /// </summary>
        NotPreparation = 1,

        /// <summary>골드가 모자란다.</summary>
        NotEnoughGold = 2,

        /// <summary>
        /// 넣을 자리가 없다. 소모품 칸 3개가 전부 차 있고 누적도 안 되는 경우다
        /// (S-GD-B7Q44N). <b>골드는 빠지지 않았다.</b>
        /// </summary>
        InventoryFull = 3,

        /// <summary>
        /// 그 칸은 애초에 팔 수 없는 칸이다. <b>데이터가 어긋난 것이다</b> —
        /// 가격표와 장비 카탈로그가 서로 다른 곳에 살아서 생긴다. 셋 중 하나다.
        ///
        /// <list type="number">
        /// <item>그 아이템의 정의가 없다</item>
        /// <item>무기다 — 상점은 소모품과 장신구만 판다 (S-GD-PVVHP9 규칙 2)</item>
        /// <item>소모품이 아닌데 묶음 수량이 붙어 있다 — 인벤토리가 항상 거절한다</item>
        /// </list>
        ///
        /// <b>정상 플레이에서는 나오지 않는다.</b> 진열 단계에서 이미 걸러지므로 화면에는
        /// 살 수 없는 칸으로 보인다. 화면에 문구를 띄우기보다 로그로 남길 종류다.
        /// </summary>
        UnknownItem = 4,

        /// <summary>
        /// 이 사람의 인벤토리를 찾을 수 없다. 전투자가 아직 스폰되지 않았거나 이미 사라졌다.
        /// 준비 단계 진입 직후의 한 프레임에서 날 수 있다.
        /// </summary>
        InventoryUnavailable = 5,

        /// <summary>
        /// 지금은 물건을 받을 수 있는 상태가 아니다 — 빈사·대시·공격·상호작용 중이다.
        /// <b>잠시 뒤 다시 누르면 된다</b>는 뜻이라 다른 사유와 문구가 달라야 한다.
        ///
        /// ⚠️ <b>2026-09-08 현재 이 값은 나오지 않는다.</b> 지금 인벤토리 구현이 지급 경로에서
        /// 상태를 보지 않기 때문이다. 값을 미리 두는 이유는 <c>EquipmentInventoryFailure</c> 가
        /// 이미 그 실패들을 선언하고 있어 <b>구현이 붙는 순간 뜻 없는 사유로 새어 나가는 것을
        /// 막기 위해서</b>다. 화면은 문구를 준비하되 지금은 안 보인다고 알고 있으면 된다.
        /// </summary>
        Busy = 6,

        /// <summary>
        /// 이미 그 장신구를 끼고 있다. <b>사도 아무것도 바뀌지 않는데 골드만 나간다</b> —
        /// 장신구는 교체가 곧 소멸이라 같은 것으로 바꾸면 들어온 것도 나간 것도 없다.
        /// 환불이 없으므로(S-GD-PVVHP9 규칙 4) 도메인이 미리 막는다.
        /// </summary>
        AlreadyOwned = 7,

        /// <summary>
        /// 상점 구역 밖이다 (S-CLI-9QYFCT). 상점은 다가가서 쓰는 장소이고, 구역을 벗어나면
        /// 화면도 닫히므로 <b>정상 플레이에서는 거의 나오지 않는다</b> — 창이 닫히는 순간과
        /// 클릭이 겹쳤거나, 보내는 쪽이 위치를 지어낸 경우다.
        /// </summary>
        NotInRange = 8,

        /// <summary>먼저 이번 라운드의 축복 선택을 확정해야 한다.</summary>
        SelectionRequired = 9,

        /// <summary>
        /// <b>그 물건을 최대 개수만큼 이미 갖고 있다</b> (S-GD-FDHBNA, 2026-09-14 추가).
        ///
        /// <para>
        /// ⚠️ <b><see cref="InventoryFull"/> 과 다르다.</b> 저쪽은 *"넣을 칸이 없다"* 이고
        /// 이것은 *"이 물건은 여기까지만 들 수 있다"* 다. 소모품은 한 칸에 여러 개가 쌓이므로
        /// <b>한도에 닿아도 칸은 비어 있을 수 있다</b> — 마력 폭발병은 한 칸에 3개까지 쌓이고
        /// 소모품 칸은 3개다. 그래서 3개를 들고 4번째를 사면 칸은 두 개나 비어 있는데
        /// <c>InventoryFull</c> 로 답하게 되고, 화면에 *"가방이 가득 찼습니다"* 라는
        /// <b>거짓 안내</b>가 뜬다. 이 값이 생기기 전까지 실제로 그랬다.
        /// </para>
        ///
        /// <para>
        /// 화면 문구는 <b>"최대 보유 갯수 이상 구매할 수 없습니다"</b> 다 (S-GD-FDHBNA).
        /// 진열에서 그 칸을 비활성으로 보여주는 것은 후속이며, 그때도 이 사유는 남는다 —
        /// 진열이 낡았거나 경계에서 어긋나면 비활성이어도 요청이 도달할 수 있고,
        /// 최종 판정은 Host 다(규약 3).
        /// </para>
        /// </summary>
        CarryLimitReached = 10,
    }

    /// <summary>
    /// 구매가 거절됐다 (S-GD-PVVHP9).
    ///
    /// <para>
    /// <b>재전송에는 이 이벤트가 오지 않는다.</b> 같은 요청을 두 번 보낸 것은 거절이 아니다 —
    /// 사용자의 클릭은 이미 한 번 성공했으므로 거절을 띄우면 거짓말이 된다. 그 구분은
    /// <c>EconomyRequestLedger</c> 가 만든다.
    /// </para>
    /// </summary>
    public readonly struct ShopPurchaseRejected : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>지금 화면을 보는 사람의 것인가. 정본 설명은 <see cref="GoldChanged.IsMine"/> 에 있다.</summary>
        public readonly bool IsMine;

        /// <summary>거절된 요청이 지목한 칸. 화면이 그 칸 위에 사유를 띄우는 데 쓴다.</summary>
        public readonly int OfferId;

        public readonly ShopPurchaseRejectReason Reason;

        public ShopPurchaseRejected(int playerId, bool isMine, int offerId, ShopPurchaseRejectReason reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            OfferId = offerId;
            Reason = reason;
        }
    }
}
