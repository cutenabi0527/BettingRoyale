using System.Collections.Generic;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// ⚠️ <b>임시 아이템 정의다. 확정된 기획이 아니다</b> (S-GD-8MXYRS, status: assumed).
    ///
    /// <para>
    /// 저장소의 현재 직렬화 장비 정의는 <b>무기 8종뿐이고 소모품·장신구는 아직 없다</b>. 그래서
    /// <see cref="PlaceholderShopCatalog"/> 의 6칸을 인벤토리 카탈로그에 그대로 조회시키면
    /// 정의가 없어 <c>UnknownItem</c> 으로 거절된다. 이 카탈로그가 그 구멍을 임시로 메운다.
    /// 실제 조립에서는 <see cref="PreparationShopItemFallbackCatalog"/> 가 이 정의를
    /// <c>EquipmentItemCatalogBehaviour</c> 안쪽에 연결한다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 이건 진짜 아이템이 아니다.</b> 실제 장비는 프리팹에 붙은
    /// <c>EquipmentVisualDefinition</c> 을 <c>EquipmentItemCatalogBehaviour</c> 가 읽어 만든다.
    /// 여기 있는 것은 <b>정의만 있고 모델·아이콘이 없는 껍데기</b>다.
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>구매·차감·인벤토리 칸 채우기는 <b>정상 동작한다</b> — 흐름 검증에는 충분하다</item>
    /// <item>손에 들리거나 화면에 보이는 것은 <b>없다</b> — 아트가 프리팹을 만들어야 한다</item>
    /// </list>
    ///
    /// <para>
    /// <b>이 클래스는 지우기 위해 있다.</b> 소모품·장신구 프리팹이 생기면
    /// <c>EquipmentItemCatalogBehaviour</c> 가 그 자리를 대신하고 이 파일을 삭제한다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>게이트에만 따로 꽂지 마라. 그건 D-CLI-R69V68 이 금지한 배선이다.</b> 여기 정의를
    /// <c>ShopRequestGate</c> 에만 넣으면 게이트는 "판다" 고 하고 인벤토리는 "그런 물건 없다" 고 해서
    /// <b>살 수 없는 물건을 살 수 있는 것처럼 진열하게 된다.</b>
    /// </para>
    ///
    /// <para>
    /// <b>대신 이 정의들은 지급 판정이 쓰는 카탈로그 <i>안쪽</i>에 들어간다</b>
    /// (<see cref="PreparationShopItemFallbackCatalog"/> → <c>EquipmentItemCatalogBehaviour</c>).
    /// 상점 게이트는 여전히 전투원 인벤토리의 카탈로그를 그대로 받으므로
    /// (<c>CombatantEquipmentItemCatalog</c>) 두 답이 갈릴 수 없다. 이 클래스를 직접 쓰는 곳은
    /// 그 폴백과 도메인 테스트의 카탈로그 대역뿐이다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class PlaceholderShopItemCatalog : IEquipmentItemCatalog
    {
        /// <summary>
        /// 소모품 한 종류가 한 칸에 몇 개까지 쌓이는가. ⚠️ 임의값이다.
        /// 소비품 칸은 3개이므로(S-GD-B7Q44N) 종류당 3개면 최대 9개를 들 수 있다.
        /// </summary>
        public const int ConsumableMaxStack = 3;

        // ⚠️ visualId 를 5000번대로 띄운 이유 — 무기 프리팹이 visualId 2101~2108 을 쓰고 있는데,
        //    그 숫자가 이 카탈로그의 소모품 itemId(2101~2103)와 겹쳐 보인다. 서로 다른 번호
        //    체계라 코드는 헷갈리지 않지만 사람이 헷갈린다. 실제 프리팹이 생길 때
        //    아트가 고를 값이므로 여기서만 임시로 멀리 떼어 둔다.
        private const int ConsumableVisualBase = 5100;
        private const int AccessoryVisualBase = 5200;

        private readonly Dictionary<int, EquipmentItemDefinition> definitions;

        public PlaceholderShopItemCatalog()
        {
            definitions = new Dictionary<int, EquipmentItemDefinition>
            {
                // ── 소모품 ─────────────────────────────────────────────
                { PlaceholderShopCatalog.HealthPotionItemId,
                    Consumable(PlaceholderShopCatalog.HealthPotionItemId, ConsumableVisualBase + 1) },
                { PlaceholderShopCatalog.SwiftnessPotionItemId,
                    Consumable(PlaceholderShopCatalog.SwiftnessPotionItemId, ConsumableVisualBase + 2) },
                { PlaceholderShopCatalog.ThrowingBombItemId,
                    Consumable(PlaceholderShopCatalog.ThrowingBombItemId, ConsumableVisualBase + 3) },

                // ── 장신구 ─────────────────────────────────────────────
                { PlaceholderShopCatalog.RingOfStrengthItemId,
                    Accessory(PlaceholderShopCatalog.RingOfStrengthItemId, AccessoryVisualBase + 1) },
                { PlaceholderShopCatalog.NecklaceOfHasteItemId,
                    Accessory(PlaceholderShopCatalog.NecklaceOfHasteItemId, AccessoryVisualBase + 2) },
                { PlaceholderShopCatalog.AmuletOfWardingItemId,
                    Accessory(PlaceholderShopCatalog.AmuletOfWardingItemId, AccessoryVisualBase + 3) },
            };
        }

        public IReadOnlyCollection<int> ItemIds => definitions.Keys;

        public bool TryGet(int itemId, out EquipmentItemDefinition definition) =>
            definitions.TryGetValue(itemId, out definition);

        /// <summary>
        /// 소모품. <c>GameplayWeapon</c> 은 <see cref="WeaponType.None"/> 이다 — 소모품은
        /// 주무기가 아니고, 공격형 축복도 붙지 않는다 (S-GD-9W5RHS).
        /// </summary>
        private static EquipmentItemDefinition Consumable(int itemId, int visualId) =>
            new EquipmentItemDefinition(
                itemId,
                EquipmentItemKind.Consumable,
                EquipmentArchetype.None,
                visualId,
                WeaponType.None,
                ConsumableMaxStack);

        /// <summary>장신구. 한 개만 낀다 — <c>MaxStack</c> 은 1 이다 (S-GD-B7Q44N).</summary>
        private static EquipmentItemDefinition Accessory(int itemId, int visualId) =>
            new EquipmentItemDefinition(
                itemId,
                EquipmentItemKind.Accessory,
                EquipmentArchetype.Accessory,
                visualId,
                WeaponType.None,
                maxStack: 1);
    }
}
