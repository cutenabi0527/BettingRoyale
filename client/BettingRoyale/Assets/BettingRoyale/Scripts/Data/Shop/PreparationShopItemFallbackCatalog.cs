using System.Collections.Generic;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Throwing;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 준비 단계 상점이 진열하는 <b>모든</b> 칸을 실제로 지급할 수 있게 만드는 <b>공용</b> 코드 폴백이다.
    ///
    /// <para>
    /// <b>⚠️ 정의를 여기서 만들지 않는다.</b> 이 클래스는 <b>순서만</b> 정하는 합성기다 —
    /// 실제 정의는 확정된 상점 소모품·장신구 정의(<see cref="RecommendedShopItemCatalog"/>,
    /// S-GD-ZD76H8)와 추천 투척 정의(<see cref="RecommendedMagicBottleCatalog"/>, S-GD-78REEH)
    /// 가 갖고 있다. 새 가격·새 효과·새 보유 한도를 여기서 만들지 않는다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>정의를 한 벌 더 두면 안 된다.</b> 예전에는 여기에 QA 임시 정의
    /// (<see cref="PlaceholderShopItemCatalog"/>)를 물려 두었는데, 제품 런타임이 확정 정의로
    /// 옮겨간 뒤에도 <b>도메인 테스트만 옛 정의를 보고 있었다</b>. 그러면 "테스트가 검증한
    /// 카탈로그와 게임이 쓰는 카탈로그가 다른" 상태가 되고, 그건 진열 6칸 중 하나만 팔리던
    /// 2026-09-15 결함을 아무도 못 잡게 만든 바로 그 구조다 (D-CLI-R69V68).
    /// 그래서 이 파일은 <b>제품이 실제로 쓰는 정의</b>만 합성한다.
    /// </para>
    ///
    /// <para>
    /// <b>아직 없는 최종 제품 데이터</b> (2026-09-15 기준):
    /// <list type="bullet">
    /// <item>소모품·장신구의 <b>실제 효과</b>(회복량·이동 속도·공격력·방어)가 정해진 적이 없다.
    /// 여기 합성이 채우는 것은 <b>지급 판정이 묻는 최소 정보</b>(종류·원형·한 칸 적재량)뿐이다.</item>
    /// <item>모델·아이콘이 없다. 아트가 <c>EquipmentVisualDefinition</c> 을 만들면
    /// <c>EquipmentItemCatalogBehaviour</c> 의 <b>직렬화 배열이 먼저 잡히므로</b> 이 폴백은
    /// 코드 변경 없이 비켜난다.</item>
    /// <item>최종 가격·묶음 수량은 데이터 파이프라인(D-041)이 공급한다. 그때
    /// <see cref="PlaceholderShopCatalog"/> 와 이 파일을 함께 삭제한다.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>게이트 전용 카탈로그가 아니다</b> (D-CLI-R69V68). 상점 게이트는 여전히 전투원 인벤토리가
    /// 지급 판정에 쓰는 카탈로그를 <b>그대로</b> 받고, 이 정의들은 <b>그 카탈로그 안쪽</b>에 들어간다.
    /// 그래서 "진열은 되는데 지급만 실패" 가 구조적으로 생길 수 없다. 이 객체를 게이트에만 따로
    /// 주입하면 정확히 그 결정이 금지한 상태가 된다 — 절대 그렇게 배선하지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>순서가 계약이다.</b> 마력 폭발병(2103)은 투척 정의까지 갖고 있으므로
    /// <see cref="RecommendedMagicBottleCatalog"/> 가 먼저 답한다. 같은 id 를 두 곳이 다르게
    /// 정의하면 어느 쪽이 이기는지가 호출 순서에 숨는다.
    /// </para>
    ///
    /// <para>
    /// <b>보유 한도를 지어내지 않는다.</b> 확정된 한도만 답한다 — 마력 폭발병(S-GD-78REEH, 합계 3개)
    /// 과 물약 2종(S-GD-ZD76H8, 체력 3 · 신속 2)이다. <b>장신구에는 한도라는 개념이 없어</b>
    /// <c>false</c> 로 답한다. 0 을 지어내면 <see cref="EquipmentInventory"/> 가
    /// "들 수 없는 물건"(<c>InvalidItem</c>)으로 읽어 아예 못 사게 된다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class PreparationShopItemFallbackCatalog
        : IEquipmentItemCatalog, IEquipmentItemCarryLimitCatalog, IThrowingItemCatalog
    {
        private static readonly RecommendedMagicBottleCatalog Throwables = new();

        /// <summary>상태가 없고 불변이라 인스턴스를 들고 다닐 이유가 없다.</summary>
        public static readonly PreparationShopItemFallbackCatalog Instance = new();

        /// <summary>
        /// 이 폴백이 답할 수 있는 상점 아이템 ItemId. 진열 목록과의 교집합 회귀 검사가 읽는다.
        /// <b>마력 폭발병은 여기 없다</b> — 그건 투척 카탈로그의 것이다.
        /// </summary>
        /// <summary>기획 엑셀 items 시트 카탈로그. 데이터가 없으면 <c>null</c> 이고 아래 코드 체인이 대역이다.</summary>
        private static BettingRoyale.Data.Balance.DataItemCatalog Data =>
            BettingRoyale.Data.Balance.BalanceCatalogs.Current.Items;

        public static IReadOnlyCollection<int> ItemIds
        {
            get
            {
                var data = Data;
                if (data != null)
                {
                    return data.ShelfItemIds;
                }

                var ids = new List<int>(RecommendedShopItemCatalog.All.Length);

                for (int i = 0; i < RecommendedShopItemCatalog.All.Length; i++)
                {
                    ids.Add(RecommendedShopItemCatalog.All[i].ItemId);
                }

                return ids;
            }
        }

        public bool TryGet(int itemId, out EquipmentItemDefinition definition) =>
            Data != null
                ? Data.TryGet(itemId, out definition)
                : Throwables.TryGet(itemId, out definition)
            || RecommendedShopItemCatalog.Instance.TryGet(itemId, out definition)
            // 상자 전용품은 진열 목록(ItemIds)에는 넣지 않고 인벤토리 정의 체인에만 잇는다 (S-GD-DDXPX9).
            || RecommendedChestItemCatalog.Instance.TryGet(itemId, out definition);

        public bool TryGetCarryLimit(int itemId, out int totalCarryLimit) =>
            Data != null
                ? Data.TryGetCarryLimit(itemId, out totalCarryLimit)
                : Throwables.TryGetCarryLimit(itemId, out totalCarryLimit)
            || RecommendedShopItemCatalog.Instance.TryGetCarryLimit(itemId, out totalCarryLimit)
            || RecommendedChestItemCatalog.Instance.TryGetCarryLimit(itemId, out totalCarryLimit);

        public bool TryGetThrowing(int itemId, out ThrowingItemDefinition definition) =>
            Data != null
                ? Data.TryGetThrowing(itemId, out definition)
                : Throwables.TryGetThrowing(itemId, out definition);
    }
}
