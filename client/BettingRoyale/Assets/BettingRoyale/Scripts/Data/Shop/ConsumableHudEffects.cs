using System;
using System.Collections.Generic;
using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 소모품 효과를 <b>HUD 효과 칸</b>에 올린다 — 효과를 건 아이템마다 아이콘 하나 (S-UX-3ESB5T · S-UX-XEC4CX).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>아이템 단위로 띄운다.</b> 켜진 효과를 건 아이템으로 묶고, 그 아이템의 그림(<see cref="ItemHudIcons"/>)을 쓴다.
    /// 프로틴 쉐이크는 방어력·공격력 두 칸이 켜져도 아이콘 <b>하나</b>, 상자 과잉 보상은 상자 그림 <b>하나</b>다.
    /// 다른 아이템끼리는 덮어쓰지 않으므로(S-GD-G3QJCW) 질주 도중 신속을 마시면 <b>두 아이콘이 함께</b> 뜨고 각자 제 시간에
    /// 사라진다. 즉발(회복·대시 즉시 충전)은 남은 시간이 없어 띄우지 않는다.
    /// </para>
    /// <para>
    /// 건 아이템을 모르는 칸(근원 0)은 예전처럼 <b>효과 칸마다</b> 공용 시트에서 빌린 아이콘으로 띄운다(S-UX-7DRFJQ §1·§2).
    /// </para>
    /// <para>한 프레임에 할당하지 않는다 — 효과 키 문자열은 아이템 번호마다 한 번만 만든다.</para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class ConsumableHudEffects
    {
        public const int SwordIcon = ItemHudIcons.SwordIcon;
        public const int RedPotionIcon = ItemHudIcons.HealthPotionIcon;
        public const int RingIcon = ItemHudIcons.RingIcon;
        public const int ShieldIcon = 7;
        public const int CrossedSwordsIcon = 8;
        public const int WingedBootIcon = 9;
        public const int ImpactIcon = 11;

        /// <summary>상자 과잉 보상 묶음의 효과 키.</summary>
        public const string ChestOverflowEffectId = "item-chest-overflow";

        /// <summary>
        /// 한 프레임에 올릴 수 있는 최대 칸 수 — 효과를 거는 근원 수다. 시간이 있는 효과를 거는 아이템 10종
        /// (신속·질주·투지·광란·철벽·보호막 부적·대시 충전석·재생·무적 부적·프로틴 쉐이크)에 상자 과잉 보상 하나를 더한 11.
        /// 체력 물약 2종은 즉발이라 칸을 쓰지 않는다. HUD 넘침 정책의 근거로 쓴다.
        /// </summary>
        public const int Maximum = 11;

        private static readonly Dictionary<int, string> ItemEffectIds = new Dictionary<int, string>();

        /// <summary>
        /// 켜져 있는 효과를 <paramref name="target"/> 뒤에 <b>덧붙인다</b>(비우지 않는다). 순서는 효과가 켜진 순서이며,
        /// 켜진 순서(FIFO)로 다시 세우는 것은 받는 쪽(<see cref="HudStatusEffectOrder"/>)의 몫이다.
        /// </summary>
        /// <param name="sourceId">효과의 주인 — 마신 전투원. 같은 칸의 갱신을 같은 인스턴스로 묶는 키의 일부다.</param>
        public static void Capture(ConsumableBuffState state, float now, int sourceId, List<HudStatusEffect> target)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            for (int i = 0; i < state.EntryCount; i++)
            {
                float remaining = state.EntryRemaining(i, now);
                if (remaining <= 0f)
                {
                    continue;
                }

                int item = state.EntrySource(i);
                if (item == 0)
                {
                    // 근원을 모르는 효과는 (칸, 근원 0) 이 하나뿐이라 효과 칸마다 하나다.
                    ConsumableBuffChannel channel = state.EntryChannel(i);
                    float duration = state.EntryDuration(i);
                    target.Add(new HudStatusEffect(Icon(channel), sourceId, EffectId(channel), Category(channel),
                        remaining, duration > 0f ? remaining / duration : 1f));
                    continue;
                }

                // 같은 아이템의 앞 효과가 이미 이 묶음을 올렸다.
                if (HasEarlierEntryFrom(state, item, i, now))
                {
                    continue;
                }

                target.Add(CaptureItem(state, now, sourceId, item, i, remaining));
            }
        }

        /// <summary>
        /// 아이템 하나의 묶음. 남은 시간은 묶음에서 <b>가장 늦게 끝나는</b> 효과의 것이다 — 아이콘은 효과가 전부 끝날 때 사라진다.
        /// </summary>
        private static HudStatusEffect CaptureItem(ConsumableBuffState state, float now, int sourceId, int item,
            int first, float firstRemaining)
        {
            float remaining = firstRemaining;
            int longest = first;
            bool shield = false;
            bool invulnerable = false;
            for (int i = first; i < state.EntryCount; i++)
            {
                float entryRemaining = state.EntryRemaining(i, now);
                if (state.EntrySource(i) != item || entryRemaining <= 0f)
                {
                    continue;
                }

                ConsumableBuffChannel channel = state.EntryChannel(i);
                shield |= channel == ConsumableBuffChannel.Shield;
                invulnerable |= channel == ConsumableBuffChannel.Invulnerable;
                if (entryRemaining > remaining)
                {
                    remaining = entryRemaining;
                    longest = i;
                }
            }

            float duration = state.EntryDuration(longest);
            return new HudStatusEffect(ItemIcon(item), sourceId, ItemEffectId(item),
                ItemCategory(shield, invulnerable), remaining, duration > 0f ? remaining / duration : 1f);
        }

        private static bool HasEarlierEntryFrom(ConsumableBuffState state, int item, int before, float now)
        {
            for (int i = 0; i < before; i++)
            {
                if (state.EntrySource(i) == item && state.EntryRemaining(i, now) > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>효과를 건 아이템의 아이콘. 상자 과잉 보상은 상자 그림이다.</summary>
        public static int ItemIcon(int sourceItemId) => sourceItemId == ShopItemEffectCatalog.ChestOverflowSourceId
            ? ItemHudIcons.TreasureChestIcon
            : ItemHudIcons.IconIndex(sourceItemId);

        /// <summary>
        /// 아이템 묶음의 효과 키. 축복 효과 키와 겹치지 않도록 <c>item-</c> 으로 시작한다 — 축복 보호막(<c>shield</c>)과
        /// 아이템 보호막은 따로 쌓이는 별개 효과라(S-GD-RVVB4H §5) 칸도 따로다.
        /// </summary>
        public static string ItemEffectId(int sourceItemId)
        {
            if (sourceItemId == ShopItemEffectCatalog.ChestOverflowSourceId)
            {
                return ChestOverflowEffectId;
            }

            if (!ItemEffectIds.TryGetValue(sourceItemId, out string id))
            {
                id = "item-" + sourceItemId;
                ItemEffectIds.Add(sourceItemId, id);
            }

            return id;
        }

        /// <summary>
        /// 아이템 묶음의 테두리 색 분류. 무적을 걸면 대시 무적 표시와 같은 <c>Condition</c>, 보호막을 걸면 축복 보호막과
        /// 같은 <c>Ability</c>, 나머지는 일반 버프다.
        /// </summary>
        public static HudStatusEffectCategory ItemCategory(bool grantsShield, bool grantsInvulnerability) =>
            grantsInvulnerability ? HudStatusEffectCategory.Condition
            : grantsShield ? HudStatusEffectCategory.Ability
            : HudStatusEffectCategory.GeneralBuff;

        /// <summary>근원을 모르는 효과 칸의 효과 키(S-UX-7DRFJQ §1).</summary>
        public static string EffectId(ConsumableBuffChannel channel) => channel switch
        {
            ConsumableBuffChannel.MoveSpeed => "item-move-speed",
            ConsumableBuffChannel.AttackSpeed => "item-attack-speed",
            ConsumableBuffChannel.DamageBonus => "item-damage-bonus",
            ConsumableBuffChannel.DamageTaken => "item-damage-taken",
            ConsumableBuffChannel.DashRecharge => "item-dash-recharge",
            ConsumableBuffChannel.Shield => "item-shield",
            ConsumableBuffChannel.Invulnerable => "item-invulnerable",
            ConsumableBuffChannel.Defense => "item-defense",
            ConsumableBuffChannel.AttackPower => "item-attack-power",
            ConsumableBuffChannel.Regeneration => "item-regeneration",
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "모르는 효과 칸입니다."),
        };

        /// <summary>근원을 모르는 효과 칸의 테두리 색 분류(S-UX-7DRFJQ §2).</summary>
        public static HudStatusEffectCategory Category(ConsumableBuffChannel channel) => channel switch
        {
            ConsumableBuffChannel.Shield => HudStatusEffectCategory.Ability,
            ConsumableBuffChannel.Invulnerable => HudStatusEffectCategory.Condition,
            _ => HudStatusEffectCategory.GeneralBuff,
        };

        /// <summary>근원을 모르는 효과 칸에 빌려 쓰는 공용 시트 아이콘(S-UX-7DRFJQ §2).</summary>
        public static int Icon(ConsumableBuffChannel channel) => channel switch
        {
            ConsumableBuffChannel.MoveSpeed => WingedBootIcon,
            ConsumableBuffChannel.AttackSpeed => CrossedSwordsIcon,
            ConsumableBuffChannel.DamageBonus => ImpactIcon,
            ConsumableBuffChannel.DamageTaken => ShieldIcon,
            ConsumableBuffChannel.DashRecharge => WingedBootIcon,
            ConsumableBuffChannel.Shield => ShieldIcon,
            ConsumableBuffChannel.Invulnerable => ShieldIcon,
            ConsumableBuffChannel.Defense => RingIcon,
            ConsumableBuffChannel.AttackPower => SwordIcon,
            ConsumableBuffChannel.Regeneration => RedPotionIcon,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "모르는 효과 칸입니다."),
        };
    }
}
