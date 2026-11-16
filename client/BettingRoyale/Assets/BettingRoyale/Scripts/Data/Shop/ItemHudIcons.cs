namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 아이템 번호 → HUD 아이콘 번호. 인벤토리 칸과 효과 칸이 <b>같은 표</b>를 쓴다 (S-UX-XEC4CX · S-UX-3XAJ35).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 아이콘 번호는 <c>UI.MatchHudAssets.Icon</c> 의 번호다 — 0~15 공용 게임플레이 시트, 16~35 축복 시트,
    /// <see cref="ChestItemArtOffset"/> 부터 상자 아이템 시트(4×4), <see cref="AccessoryArtOffset"/> 부터 장신구 시트(4×4)다.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="ChestItemArtOffset"/>·<see cref="AccessoryArtOffset"/> 은 <c>UI.MatchHudAssets</c> 의
    /// <c>ChestItemIconOffset</c>·<c>AccessoryIconOffset</c> 과 같은 값이어야 한다.
    /// 이 계층은 UI 를 참조하지 못해 값을 다시 적었다 — 둘을 대조하는 EditMode 테스트가 어긋남을 잡는다
    /// (축복 쪽 <c>CombatBlessingAgent.BlessingArtOffset</c> 과 같은 방식).
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class ItemHudIcons
    {
        /// <summary>그림 없음 — 빈 칸.</summary>
        public const int None = -1;

        // ── 공용 게임플레이 시트 칸 ─────────────────────────────────────────────
        public const int SwordIcon = 0;
        public const int BowIcon = 1;
        public const int HealthPotionIcon = 2;
        public const int SwiftnessPotionIcon = 3;
        public const int MagicBottleIcon = 4;
        public const int RingIcon = 5;
        public const int TreasureChestIcon = 15;

        /// <summary>상자 아이템 시트의 첫 칸 번호. 시트 칸 순서는 <see cref="RecommendedChestItemCatalog.All"/> 순서다.</summary>
        public const int ChestItemArtOffset = 36;

        /// <summary>상자 아이템 시트의 칸 수(4×4). 앞 10칸만 그림이 있다.</summary>
        public const int ChestItemArtCells = 16;

        /// <summary>장신구 시트의 첫 칸 번호 — 상자 아이템 시트 바로 뒤.</summary>
        public const int AccessoryArtOffset = ChestItemArtOffset + ChestItemArtCells;

        /// <summary>장신구 시트의 칸 수(4×4). 앞 2칸만 그림이 있다.</summary>
        public const int AccessoryArtCells = 16;

        /// <summary>
        /// 장신구 시트의 칸 순서 (S-UX-3XAJ35). 힘의 반지는 여기 없다 — 공용 시트의 기존 반지 그림(<see cref="RingIcon"/>)을
        /// 그대로 쓴다.
        /// </summary>
        private static readonly int[] AccessoryArtItemIds =
        {
            PlaceholderShopCatalog.NecklaceOfHasteItemId,
            PlaceholderShopCatalog.AmuletOfWardingItemId,
        };

        private const int SwordItemId = 1101;
        private const int BowItemId = 1104;
        private const int AccessoryItemIdStart = 3000;

        /// <summary>
        /// 이 아이템의 아이콘 번호. 모르는 소모품은 폭발병 그림(예전 동작), 전용 그림이 없는 3000번대(힘의 반지 포함)는
        /// 반지, 0 이하는 <see cref="None"/>.
        /// </summary>
        public static int IconIndex(int itemId)
        {
            switch (itemId)
            {
                case SwordItemId:
                    return SwordIcon;
                case BowItemId:
                    return BowIcon;
                case PlaceholderShopCatalog.HealthPotionItemId:
                    return HealthPotionIcon;
                case PlaceholderShopCatalog.SwiftnessPotionItemId:
                    return SwiftnessPotionIcon;
                case PlaceholderShopCatalog.ThrowingBombItemId:
                    return MagicBottleIcon;
            }

            if (TryGetChestItemArt(itemId, out int chestIcon))
            {
                return chestIcon;
            }

            if (TryGetAccessoryArt(itemId, out int accessoryIcon))
            {
                return accessoryIcon;
            }

            if (itemId >= AccessoryItemIdStart)
            {
                return RingIcon;
            }

            return itemId > 0 ? MagicBottleIcon : None;
        }

        /// <summary>상자 전용품이면 상자 아이템 시트의 아이콘 번호를 준다.</summary>
        public static bool TryGetChestItemArt(int itemId, out int iconIndex)
        {
            var all = RecommendedChestItemCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].ItemId == itemId)
                {
                    iconIndex = ChestItemArtOffset + i;
                    return true;
                }
            }

            iconIndex = None;
            return false;
        }

        /// <summary>전용 그림이 있는 장신구면 장신구 시트의 아이콘 번호를 준다. 힘의 반지는 <c>false</c> 다.</summary>
        public static bool TryGetAccessoryArt(int itemId, out int iconIndex)
        {
            for (int i = 0; i < AccessoryArtItemIds.Length; i++)
            {
                if (AccessoryArtItemIds[i] == itemId)
                {
                    iconIndex = AccessoryArtOffset + i;
                    return true;
                }
            }

            iconIndex = None;
            return false;
        }
    }
}
