using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// <b>상자에서만 나오는</b> 소모품 10종의 존재 — 번호·종류·외형·소지 한도 (S-GD-DDXPX9 · S-GD-RVVB4H).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>무엇을 하는지는 여기 없다.</b> 효과 수치는 <see cref="ShopItemEffectCatalog"/> 한 곳에 있다.
    /// 상점 물약 2종(체력·신속)은 상자에서도 나오지만 같은 아이템이라 <see cref="RecommendedShopItemCatalog"/> 에 있다.
    /// </para>
    /// <para>
    /// ⚠️ <b>상점 진열 카탈로그와 섞지 않는다.</b> 상점 쪽 검사(진열 목록과 정의 목록이 짝이 맞는가)가
    /// <see cref="RecommendedShopItemCatalog.All"/> 을 진열과 대조하므로, 상자 전용품을 거기 넣으면
    /// "진열에 없는 정의" 로 잡힌다. 인벤토리 폴백 체인(<see cref="PreparationShopItemFallbackCatalog"/>)에만 잇는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>번호·외형·한도는 추정값이다</b>(S-GD-RVVB4H §3·§4). 한 칸에 1개, 보유 한도 1 —
    /// 같은 상자 전용품이 또 뽑히면 같은 등급 안에서 다시 뽑고, 그 등급에 들 것이 없을 때만 과잉 보상이다 (S-GD-ZPV7Q4).
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public sealed class RecommendedChestItemCatalog : IEquipmentItemCatalog, IEquipmentItemCarryLimitCatalog
    {
        public static readonly RecommendedChestItemCatalog Instance = new();

        // 소모품 대역(2100번대)을 잇는다 — 2101 체력 · 2102 신속 · 2103 마력 폭발병 다음.
        public const int LargeHealthPotionItemId = 2104;
        public const int SprintPotionItemId = 2105;
        public const int FuryPotionItemId = 2106;
        public const int FrenzyPotionItemId = 2107;
        public const int BulwarkPotionItemId = 2108;
        public const int ShieldCharmItemId = 2109;
        public const int DashStoneItemId = 2110;
        public const int RegenerationPotionItemId = 2111;
        public const int InvulnerabilityCharmItemId = 2112;

        // 2113·2114 는 폐기된 골드 주머니·재추첨권이 쓰던 번호다 (S-GD-DDXPX9 §7). 다시 쓰지 않는다.
        public const int ProteinShakeItemId = 2115;

        /// <summary>
        /// 외형 번호. 상점 물약(5101·5102)을 잇는다. HUD 아이콘은 이 번호가 아니라 아이템 번호로
        /// <see cref="ItemHudIcons"/> 가 고른다 — 상자 아이템 시트의 칸 순서가 <see cref="All"/> 순서다.
        /// </summary>
        private const int VisualIdOffset = 3000;

        /// <summary>상자 전용품 한 칸에 쌓이는 수이자 보유 한도 (S-GD-RVVB4H §4).</summary>
        public const int ChestItemCarryLimit = 1;

        /// <summary>
        /// 인벤토리에 들어가는 상자 전용품 10종. 전부 그 전투에서 쓰는 물건이다.
        /// ⚠️ <b>순서를 바꾸지 않는다</b> — 상자 아이템 아이콘 시트(<c>UI_ChestItemIcons.png</c>)의 칸 순서다.
        /// </summary>
        public static readonly EquipmentItemDefinition[] All =
        {
            Consumable(LargeHealthPotionItemId),
            Consumable(SprintPotionItemId),
            Consumable(FuryPotionItemId),
            Consumable(FrenzyPotionItemId),
            Consumable(BulwarkPotionItemId),
            Consumable(ShieldCharmItemId),
            Consumable(DashStoneItemId),
            Consumable(RegenerationPotionItemId),
            Consumable(InvulnerabilityCharmItemId),
            Consumable(ProteinShakeItemId),
        };

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

        public bool TryGetCarryLimit(int itemId, out int totalCarryLimit)
        {
            if (TryGet(itemId, out _))
            {
                totalCarryLimit = ChestItemCarryLimit;
                return true;
            }

            totalCarryLimit = 0;
            return false;
        }

        private static EquipmentItemDefinition Consumable(int itemId) => new(
            itemId,
            EquipmentItemKind.Consumable,
            EquipmentArchetype.None,
            itemId + VisualIdOffset,
            WeaponType.None,
            ChestItemCarryLimit);
    }
}
