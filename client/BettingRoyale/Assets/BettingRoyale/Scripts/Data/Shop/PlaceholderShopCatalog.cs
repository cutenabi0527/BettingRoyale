namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// ⚠️ <b>임시 상품 목록이다. 확정된 기획이 아니다</b> (S-GD-8MXYRS, status: assumed).
    ///
    /// <para>
    /// 상품 구성·가격이 어디에도 정해져 있지 않은데 상점은 목록 없이 성립하지 않는다. 그래서 흐름을
    /// 끝까지 돌려보기 위한 값을 <b>에이전트가 임의로 정했다.</b> 밸런스 근거가 없다 —
    /// 유일한 기준은 라운드 기본 지급 1,000G(<c>ROUND_GOLD</c>) 안에서
    /// "장신구 하나 또는 소모품 여러 개" 가 되게 맞춘 것뿐이다.
    /// </para>
    ///
    /// <para>
    /// <b>이 클래스는 지우기 위해 있다.</b> 확정 데이터가 데이터 파이프라인으로 들어오면(D-041)
    /// <see cref="ShopCatalog"/> 를 그쪽에서 만들어 넘기고 이 파일을 삭제한다. 이름에 <c>Placeholder</c> 가
    /// 붙어 있으므로 남아 있으면 눈에 띈다.
    /// </para>
    ///
    /// <para>
    /// 저장소에 등록된 직렬화 장비는 무기 8종(1101~1108)뿐이고 소모품·장신구는 <b>0개</b>다.
    /// 가격표만 이 목록에 두면 <c>EquipmentInventory</c> 가 <c>InvalidItem</c> 으로 거절하지만,
    /// 현재 런타임은 <see cref="PreparationShopItemFallbackCatalog"/> 를 인벤토리 카탈로그 안쪽에
    /// 연결해 QA 흐름을 끝까지 돌린다. 최종 장비 데이터가 들어오면 직렬화 정의가 먼저 사용되고
    /// 이 임시 폴백은 제거한다 (S-GD-B7Q44N 이 정한 칸: 무기 1 · 소모품 3 · 장신구 1).
    /// </para>
    ///
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </para>
    /// </summary>
    public static class PlaceholderShopCatalog
    {
        // 아이템 번호 대역 — 무기 1101~1108 과 겹치지 않게 띄웠다.
        //   2100 번대 = 소모품 · 3100 번대 = 장신구
        public const int HealthPotionItemId = 2101;
        public const int SwiftnessPotionItemId = 2102;
        public const int ThrowingBombItemId = 2103;

        public const int RingOfStrengthItemId = 3101;
        public const int NecklaceOfHasteItemId = 3102;
        public const int AmuletOfWardingItemId = 3103;

        /// <summary>
        /// 임시 진열대 6칸. <b>가격·묶음 수량 전부 임의값이다</b> (S-GD-8MXYRS).
        ///
        /// <list type="bullet">
        /// <item>소모품 3종 — 라운드 지급 1,000G 로 두세 개를 살 수 있는 값</item>
        /// <item>장신구 3종 — 하나 사면 그 라운드 예산을 대부분 쓰는 값. 장신구는 매치 내내 유지되므로
        /// 소모품보다 비싸다</item>
        /// </list>
        ///
        /// <b>장신구를 사면 갖고 있던 장신구가 사라진다</b> — 드롭되지 않고 소멸한다
        /// (S-GD-B7Q44N, 장신구 간 마법적 충돌). 상점 구매도 같은 규칙을 탄다.
        /// </summary>
        public static ShopCatalog Create() => new ShopCatalog(new[]
        {
            new ShopOffer(offerId: 1, itemId: HealthPotionItemId, price: 120, quantity: 1),
            new ShopOffer(offerId: 2, itemId: SwiftnessPotionItemId, price: 150, quantity: 1),
            new ShopOffer(offerId: 3, itemId: ThrowingBombItemId, price: 200, quantity: 1),
            new ShopOffer(offerId: 4, itemId: RingOfStrengthItemId, price: 600, quantity: 1),
            new ShopOffer(offerId: 5, itemId: NecklaceOfHasteItemId, price: 600, quantity: 1),
            new ShopOffer(offerId: 6, itemId: AmuletOfWardingItemId, price: 750, quantity: 1),
        });
    }
}
