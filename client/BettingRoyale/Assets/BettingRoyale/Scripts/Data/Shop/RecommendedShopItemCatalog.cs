using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// <b>상점 소모품 2종·장신구 3종의 장비 정의</b> (S-GD-ZD76H8 확정).
    ///
    /// <para>
    /// <b>⚠️ 여기 있는 것은 "팔 물건이 존재한다" 는 사실뿐이다.</b> 효과(회복량·이동 속도·공격력
    /// 같은 것)는 이 파일에 없다 — 그건 스탯 시트가 할 일이고 아직 안 이어졌다. 이 카탈로그가
    /// 채우는 것은 <b>지급 판정이 물어보는 최소 정보</b>(종류·한 칸에 쌓이는 수·보유 한도)다.
    /// </para>
    ///
    /// <para>
    /// <b>왜 이게 필요했나.</b> 진열 6칸 중 <b>마력 폭발병(2103) 하나만</b> 실제로 팔렸다
    /// (2026-09-15 Play 실측 — 사용자 보고: <i>"상점은 마력 폭발병 제외하고는 구매가 안되더라고"</i>).
    /// 원인은 상점이 아니라 <b>장비 정의가 없다는 것</b>이었다: <c>EquipmentCatalog.prefab</c> 에는
    /// 무기 8종(1101~1108)뿐이고, 폭발병만 코드 폴백
    /// (<see cref="Throwing.RecommendedMagicBottleCatalog"/>)을 갖고 있었다. 나머지 5개는
    /// 사거리·골드를 다 통과한 마지막 지급 단계에서 <c>InvalidItem</c> 으로 떨어졌다.
    /// </para>
    ///
    /// <para>
    /// <b>이 파일은 지우기 위해 있다.</b> 아트가 <c>EquipmentVisualDefinition</c> 5개를 만들어
    /// <c>EquipmentCatalog.prefab</c> 에 넣으면 그쪽이 먼저 잡히므로
    /// (<c>EquipmentItemCatalogBehaviour</c> 는 직렬화 배열을 먼저 본다) <b>이 폴백은 코드 변경 없이
    /// 자동으로 비켜난다.</b> 폭발병이 같은 자리에 같은 방식으로 서 있는 것도 그래서다.
    /// </para>
    ///
    /// <para>
    /// <b>게이트 전용 카탈로그가 아니다</b>(D-CLI-R69V68). 상점 게이트는 여전히 전투원 인벤토리가
    /// 쓰는 카탈로그를 그대로 받고, 이 정의들은 <b>그 카탈로그 안쪽</b>에 들어간다 —
    /// 그래서 "진열은 되는데 지급만 실패" 가 생길 수 없다. 게이트 옆에 따로 붙이면 정확히 그
    /// 결정이 금지한 상태가 된다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class RecommendedShopItemCatalog
        : IEquipmentItemCatalog, IEquipmentItemCarryLimitCatalog
    {
        // ── 아이템 번호 — PlaceholderShopCatalog 의 진열과 같은 값이어야 한다 ────────────
        //
        // ⚠️ 상수를 다시 적지 않고 그쪽을 참조한다. 두 벌로 두면 한쪽만 고쳐서
        //    "진열에는 있는데 정의가 없는" 상태가 조용히 돌아온다 — 방금 고친 그 상태다.

        public const int HealthPotionVisualId = 5101;
        public const int SwiftnessPotionVisualId = 5102;

        public const int RingOfStrengthVisualId = 5301;
        public const int NecklaceOfHasteVisualId = 5302;
        public const int AmuletOfWardingVisualId = 5303;

        /// <summary>체력 물약: 보유 한도 3, 한 칸에 3개. 그래서 <b>한 칸만 차지한다</b>.</summary>
        public const int HealthPotionCarryLimit = 3;

        /// <summary>신속 물약: 보유 한도 2, 한 칸에 2개.</summary>
        public const int SwiftnessPotionCarryLimit = 2;

        /// <summary>
        /// 체력 물약(2101). <b>한 칸에 쌓이는 수 = 보유 한도</b>라 소모품 칸 하나만 쓴다 (S-GD-ZD76H8).
        /// </summary>
        public static readonly EquipmentItemDefinition HealthPotion = new(
            PlaceholderShopCatalog.HealthPotionItemId,
            EquipmentItemKind.Consumable,
            EquipmentArchetype.None,
            HealthPotionVisualId,
            WeaponType.None,
            HealthPotionCarryLimit);

        /// <summary>신속 물약(2102).</summary>
        public static readonly EquipmentItemDefinition SwiftnessPotion = new(
            PlaceholderShopCatalog.SwiftnessPotionItemId,
            EquipmentItemKind.Consumable,
            EquipmentArchetype.None,
            SwiftnessPotionVisualId,
            WeaponType.None,
            SwiftnessPotionCarryLimit);

        /// <summary>
        /// 힘의 반지(3101). <b>장신구는 한 개만 지닌다</b> — 사면 갖고 있던 것이 사라진다
        /// (S-GD-B7Q44N). 그래서 <see cref="EquipmentItemDefinition.MaxStack"/> 는 1 이고
        /// 보유 한도라는 개념 자체가 없다.
        /// </summary>
        public static readonly EquipmentItemDefinition RingOfStrength = new(
            PlaceholderShopCatalog.RingOfStrengthItemId,
            EquipmentItemKind.Accessory,
            EquipmentArchetype.None,
            RingOfStrengthVisualId,
            WeaponType.None,
            1);

        /// <summary>신속의 목걸이(3102).</summary>
        public static readonly EquipmentItemDefinition NecklaceOfHaste = new(
            PlaceholderShopCatalog.NecklaceOfHasteItemId,
            EquipmentItemKind.Accessory,
            EquipmentArchetype.None,
            NecklaceOfHasteVisualId,
            WeaponType.None,
            1);

        /// <summary>수호의 부적(3103).</summary>
        public static readonly EquipmentItemDefinition AmuletOfWarding = new(
            PlaceholderShopCatalog.AmuletOfWardingItemId,
            EquipmentItemKind.Accessory,
            EquipmentArchetype.None,
            AmuletOfWardingVisualId,
            WeaponType.None,
            1);

        /// <summary>
        /// 이 폴백이 아는 전부. <b>마력 폭발병(2103)은 여기 없다</b> —
        /// <see cref="Throwing.RecommendedMagicBottleCatalog"/> 가 이미 갖고 있고,
        /// 두 곳에 두면 어느 쪽이 이기는지가 호출 순서에 숨는다.
        /// </summary>
        public static readonly EquipmentItemDefinition[] All =
        {
            HealthPotion,
            SwiftnessPotion,
            RingOfStrength,
            NecklaceOfHaste,
            AmuletOfWarding,
        };

        /// <summary>인스턴스를 들고 다닐 이유가 없다 — 불변이고 상태가 없다.</summary>
        public static readonly RecommendedShopItemCatalog Instance = new();

        public bool TryGet(int itemId, out EquipmentItemDefinition definition)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].ItemId == itemId)
                {
                    definition = All[i];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        /// <summary>
        /// 보유 한도. <b>소모품만 답한다.</b>
        /// </summary>
        /// <remarks>
        /// 장신구에 <c>false</c> 를 주는 것은 모르는 것이 아니라 <b>한도가 성립하지 않기 때문</b>이다.
        /// 한도 0 을 지어내면 <c>EquipmentInventory</c> 가 "이 물건은 들 수 없다"(<c>InvalidItem</c>)로
        /// 읽어 장신구를 통째로 못 사게 된다 — 상점 게이트도 소모품에만 한도를 적용한다
        /// (<c>ShopRequestGate.WouldExceedCarryLimit</c>).
        /// </remarks>
        public bool TryGetCarryLimit(int itemId, out int totalCarryLimit)
        {
            if (itemId == PlaceholderShopCatalog.HealthPotionItemId)
            {
                totalCarryLimit = HealthPotionCarryLimit;
                return true;
            }

            if (itemId == PlaceholderShopCatalog.SwiftnessPotionItemId)
            {
                totalCarryLimit = SwiftnessPotionCarryLimit;
                return true;
            }

            totalCarryLimit = 0;
            return false;
        }
    }
}
