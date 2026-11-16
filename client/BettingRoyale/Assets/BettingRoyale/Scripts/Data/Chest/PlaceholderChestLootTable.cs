using BettingRoyale.Data.Shop;

namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// 지금 쓰는 상자 보상 값 (S-GD-DDXPX9). 데이터 파이프라인(D-041)이 오기 전까지의 자리다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ChestLootTable"/>·<see cref="ChestRewardResolver"/> 는 기본값을 갖지 않는다(S-GD-GQNA2B).
    /// 조립하는 쪽이 여기서 꺼내 넘긴다 — <c>PlaceholderRouletteConfig</c>·<c>PlaceholderShopCatalog</c> 와 같은 방식.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class PlaceholderChestLootTable
    {
        /// <summary>
        /// 상자 보상 골드 <b>300</b> — 기획 엑셀 <c>CHEST_REWARD_GOLD</c> 를 따른다 (S-GD-BPB9S3 가 S-GD-DDXPX9 의 200 을 대체).
        /// </summary>
        public const int RewardGold = 300;

        /// <summary>
        /// 상자 개봉 시간(초). <b>5초</b> 확정 (S-GD-GQNA2B · 기획 엑셀 <c>CHEST_OPEN_TIME</c>). 게이지가 다 차면 그 자리에서
        /// 지급한다 (S-GD-RC63HQ). 제품 온라인과 Local Play 가 이 값을 쓴다 — CombatLab 직접 실행의 QA 프로필
        /// (10초, S-QA-K6Z6T6)은 쓰지 않는다.
        /// </summary>
        public const int OpenSeconds = 5;

        /// <summary>
        /// 마지막 라운드가 아닐 때 — 일반 58 · 고급 30 · 희귀 12.
        /// <b>등급 안에서 균등</b>하므로 아이템 하나로는 일반 29% · 고급 5% · 희귀 3% 다.
        /// </summary>
        public static readonly ChestRarityWeights RegularWeights = new(common: 58, rare: 30, epic: 12);

        /// <summary>
        /// 마지막 라운드 — 일반 38 · 고급 42 · 희귀 20. 전투로 끝내야 하므로 위 등급을 늘린다.
        /// 아이템 하나로는 일반 19% · 고급 7% · 희귀 5% 다.
        /// </summary>
        public static readonly ChestRarityWeights FinalRoundWeights = new(common: 38, rare: 42, epic: 20);

        /// <summary>
        /// 12종. <b>전부 그 전투에서 쓰는 물건이다</b> — 골드 주머니·재추첨권은 폐기했다(S-GD-DDXPX9 §7).
        /// </summary>
        public static readonly ChestLootEntry[] Entries =
        {
            // 일반 2종 — 상점과 같은 물건
            new(PlaceholderShopCatalog.HealthPotionItemId, ChestItemRarity.Common),
            new(PlaceholderShopCatalog.SwiftnessPotionItemId, ChestItemRarity.Common),

            // 고급 6종 — 상자 전용
            new(RecommendedChestItemCatalog.SprintPotionItemId, ChestItemRarity.Rare),
            new(RecommendedChestItemCatalog.FuryPotionItemId, ChestItemRarity.Rare),
            new(RecommendedChestItemCatalog.FrenzyPotionItemId, ChestItemRarity.Rare),
            new(RecommendedChestItemCatalog.BulwarkPotionItemId, ChestItemRarity.Rare),
            new(RecommendedChestItemCatalog.ShieldCharmItemId, ChestItemRarity.Rare),
            new(RecommendedChestItemCatalog.RegenerationPotionItemId, ChestItemRarity.Rare),

            // 희귀 4종 — 상자 전용
            new(RecommendedChestItemCatalog.LargeHealthPotionItemId, ChestItemRarity.Epic),
            new(RecommendedChestItemCatalog.DashStoneItemId, ChestItemRarity.Epic),
            new(RecommendedChestItemCatalog.InvulnerabilityCharmItemId, ChestItemRarity.Epic),
            new(RecommendedChestItemCatalog.ProteinShakeItemId, ChestItemRarity.Epic),
        };

        public static ChestLootTable Create() => new(Entries, RegularWeights, FinalRoundWeights);
    }
}
