using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 상점·상자 소모품과 장신구가 <b>실제로 무엇을 하는가</b> (S-GD-YMKKDJ · S-GD-DDXPX9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RecommendedShopItemCatalog"/> 는 <b>물건이 존재한다</b>는 사실만 담는다 —
    /// 종류·외형·소지 한도. 그래서 살 수는 있었지만 마셔도 장착해도 아무 일이 없었다.
    /// 이 파일이 그 빈자리다.
    /// </para>
    /// <para>
    /// <b>수치를 여기 한 곳에만 둔다.</b> 장신구 셋은 스탯 시트로, 물약 둘은 사용 즉시 효과로
    /// 나가지만 출처는 이 표 하나다. 두 곳에 적으면 한쪽만 고쳐서 조용히 갈린다.
    /// </para>
    /// <para>
    /// <b>마력 폭발병(2103)은 여기 없다.</b> 그건 투척물이라 피해·반경·사거리가
    /// <c>RecommendedMagicBottleCatalog</c> 에 따로 있고, 스탯 시트를 거치지 않는다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class ShopItemEffectCatalog
    {
        /// <summary>
        /// 스탯 시트에서 장신구 기여를 식별하는 출처 이름.
        /// <b>강화·축복과 다른 이름이어야</b> 서로 걷어내지 않는다.
        /// </summary>
        public const string AccessorySourceId = "shop-accessory";

        // ── S-GD-YMKKDJ 확정값 ───────────────────────────────────────────────

        /// <summary>체력 물약이 즉시 회복하는 양.</summary>
        public const float HealthPotionHeal = 25f;

        /// <summary>
        /// 신속 물약의 이동 속도 <b>증감분</b>과 지속 시간(초).
        /// ⚠️ 0.10 은 "+10%" 다 — 시트의 <c>Add</c> 규약은 배율이 아니라 증감분이다 (§1.2-B).
        /// </summary>
        public const float SwiftnessPotionMoveBonus = 0.10f;

        /// <inheritdoc cref="SwiftnessPotionMoveBonus"/>
        public const float SwiftnessPotionSeconds = 5f;

        /// <summary>힘의 반지 공격력. <b>근접과 원거리가 다르다.</b></summary>
        public const float RingMeleeAttackPower = 5f;

        /// <inheritdoc cref="RingMeleeAttackPower"/>
        public const float RingRangedAttackPower = 3f;

        /// <summary>
        /// 신속의 목걸이 이동·공격 속도 <b>증감분</b>. 0.05 = +5%, 0.20 = +20% (S-GD-2YQQG4).
        /// 강화 세트도 같은 규약이다(<c>Add(MoveSpeed, 0.03f)</c> = +3%).
        /// </summary>
        public const float NecklaceMoveBonus = 0.05f;

        /// <inheritdoc cref="NecklaceMoveBonus"/>
        public const float NecklaceAttackSpeedBonus = 0.20f;

        /// <summary>
        /// 수호의 부적 방어력. <b>+5</b> 다 (S-GD-2YQQG4 가 S-GD-YMKKDJ 의 +7 을 대체했다 — 세 장신구의 100G당 효율을 맞춘다).
        /// </summary>
        public const float AmuletDefense = 5f;

        // ── S-GD-DDXPX9 확정값 — 상자 전용품 ─────────────────────────────────

        /// <summary>큰 체력 물약 즉시 회복량.</summary>
        public const float LargeHealthPotionHeal = 50f;

        /// <summary>질주 물약 이동 속도 증감분·지속(초).</summary>
        public const float SprintPotionMoveBonus = 0.30f;

        /// <inheritdoc cref="SprintPotionMoveBonus"/>
        public const float SprintPotionSeconds = 4f;

        /// <summary>투지 물약 주는 피해 증감분·지속(초).</summary>
        public const float FuryPotionDamageBonus = 0.20f;

        /// <inheritdoc cref="FuryPotionDamageBonus"/>
        public const float FuryPotionSeconds = 6f;

        /// <summary>광란 물약 공격 속도 증감분·지속(초).</summary>
        public const float FrenzyPotionAttackSpeedBonus = 0.30f;

        /// <inheritdoc cref="FrenzyPotionAttackSpeedBonus"/>
        public const float FrenzyPotionSeconds = 6f;

        /// <summary>철벽 물약 받는 피해 <b>감소분</b>(0.25 = −25%)·지속(초).</summary>
        public const float BulwarkPotionDamageReduction = 0.25f;

        /// <inheritdoc cref="BulwarkPotionDamageReduction"/>
        public const float BulwarkPotionSeconds = 6f;

        /// <summary>보호막 부적 보호막 양·유지(초).</summary>
        public const float ShieldCharmAmount = 30f;

        /// <inheritdoc cref="ShieldCharmAmount"/>
        public const float ShieldCharmSeconds = 10f;

        /// <summary>대시 충전석 — 재충전 시간 배율과 그 지속(초). 충전은 즉시 최대다.</summary>
        public const float DashStoneRechargeMultiplier = 0.5f;

        /// <inheritdoc cref="DashStoneRechargeMultiplier"/>
        public const float DashStoneRechargeSeconds = 3f;

        /// <summary>재생 물약 초당 회복·지속(초). <b>8초 × 5 = 총 40</b> (초당 6 → 5 하향).</summary>
        public const float RegenerationPotionHealPerSecond = 5f;

        /// <inheritdoc cref="RegenerationPotionHealPerSecond"/>
        public const float RegenerationPotionSeconds = 8f;

        /// <summary>무적 부적 무적 시간(초).</summary>
        public const float InvulnerabilityCharmSeconds = 1f;

        /// <summary>
        /// 프로틴 쉐이크 방어력·공격력 <b>고정</b> 증가량과 지속(초).
        /// ⚠️ 퍼센트가 아니다 — 수호의 부적(<see cref="AmuletDefense"/>)·힘의 반지 원거리
        /// (<see cref="RingRangedAttackPower"/> +3)와 <b>같은 단위(고정 수치)</b>다. 부적이 +5 로 내려간 뒤에도
        /// 쉐이크는 +7 을 유지한다 — 두 값이 같다는 연결은 S-GD-2YQQG4 가 끊었다.
        /// </summary>
        public const float ProteinShakeDefense = 7f;

        /// <inheritdoc cref="ProteinShakeDefense"/>
        public const float ProteinShakeAttackPower = 3f;

        /// <inheritdoc cref="ProteinShakeDefense"/>
        public const float ProteinShakeSeconds = 10f;

        // ── 과잉 보상 (S-GD-DDXPX9 §4) — 인벤토리가 뽑힌 아이템을 받지 못할 때 ──────

        /// <summary>과잉 보상 즉시 회복.</summary>
        public const float OverflowHeal = 25f;

        /// <summary>과잉 보상 보호막 양·유지(초).</summary>
        public const float OverflowShieldAmount = 10f;

        /// <inheritdoc cref="OverflowShieldAmount"/>
        public const float OverflowShieldSeconds = 6f;

        /// <summary>과잉 보상 공격·이동 속도 증감분과 지속(초).</summary>
        public const float OverflowSpeedBonus = 0.15f;

        /// <inheritdoc cref="OverflowSpeedBonus"/>
        public const float OverflowSpeedSeconds = 6f;

        /// <summary>
        /// 과잉 보상 효과를 건 근원 번호. 아이템이 아니라 음수로 둔다 — 0(모름)·아이템 번호와 겹치지 않는다.
        /// HUD 는 이 근원을 상자 그림 한 칸으로 묶는다 (S-UX-XEC4CX).
        /// </summary>
        public const int ChestOverflowSourceId = -1;

        /// <summary>
        /// 과잉 보상 효과 묶음. <b>피해 증가는 넣지 않는다</b> — "버티면서 싸워라" 쪽 유도다.
        /// </summary>
        public static IReadOnlyList<ConsumableEffect> ChestOverflowEffects
        {
            get
            {
                var data = BettingRoyale.Data.Balance.BalanceCatalogs.Current.ItemEffects;
                return data != null && data.ChestOverflowEffects.Count > 0 ? data.ChestOverflowEffects : PlaceholderChestOverflowEffects;
            }
        }

        /// <summary>코드 대역 과잉 보상 묶음. 정본은 엑셀 item_effects 의 item_id -1 행이다.</summary>
        public static readonly IReadOnlyList<ConsumableEffect> PlaceholderChestOverflowEffects = new[]
        {
            ConsumableEffect.InstantHeal(OverflowHeal),
            ConsumableEffect.Shield(OverflowShieldAmount, OverflowShieldSeconds),
            ConsumableEffect.TimedAttackSpeed(OverflowSpeedBonus, OverflowSpeedSeconds),
            ConsumableEffect.TimedMoveSpeed(OverflowSpeedBonus, OverflowSpeedSeconds),
        };

        private static readonly IReadOnlyList<StatContribution> Empty = Array.Empty<StatContribution>();

        /// <summary>
        /// 장신구 기여를 시트에 <b>갈아 끼운다</b>. 여러 번 불러도 겹치지 않는다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>걷어내기와 얹기를 <see cref="StatSheet.ReplaceFrom"/> 한 번으로 한다.</b>
        /// 따로 하면 중간에 던졌을 때 부르기 전보다 나빠진 시트가 남는다 —
        /// <c>UpgradeState.ApplyTo</c> 가 같은 이유로 같은 방식을 쓴다.
        /// </para>
        /// <para>
        /// ⚠️ <b>힘의 반지는 무기에 따라 값이 다르다.</b> 근접 +5 / 원거리 +3 이므로
        /// 무기를 바꾸면 이 함수를 다시 불러야 한다. 안 부르면 활을 들고도 근접 값을 쓴다.
        /// </para>
        /// </remarks>
        /// <param name="sheet">합산 시트.</param>
        /// <param name="accessoryItemId">장착한 장신구. 0 이나 모르는 id 면 기여를 비운다.</param>
        /// <param name="isRangedWeapon">지금 든 무기가 원거리인가. 힘의 반지에만 쓰인다.</param>
        public static void ApplyAccessoryToSheet(StatSheet sheet, int accessoryItemId, bool isRangedWeapon)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            sheet.ReplaceFrom(AccessorySourceId, BuildAccessoryStats(accessoryItemId, isRangedWeapon));
        }

        /// <summary>장신구 하나가 시트에 넣을 기여. 모르는 id 면 빈 목록이다.</summary>
        public static IReadOnlyList<StatContribution> BuildAccessoryStats(int accessoryItemId, bool isRangedWeapon)
        {
            // 기획 엑셀(item_effects 의 stat 행)이 있으면 그 표가 정본이다 (S-CLI-QXRTWX).
            var data = BettingRoyale.Data.Balance.BalanceCatalogs.Current.ItemEffects;
            if (data != null && data.HasAccessory(accessoryItemId))
            {
                return data.BuildAccessoryStats(accessoryItemId,
                    isRangedWeapon ? BettingRoyale.Data.Combat.WeaponType.Bow : BettingRoyale.Data.Combat.WeaponType.Sword,
                    isRangedWeapon, AccessorySourceId);
            }

            if (accessoryItemId == PlaceholderShopCatalog.RingOfStrengthItemId)
            {
                float power = isRangedWeapon ? RingRangedAttackPower : RingMeleeAttackPower;
                return new[]
                {
                    new StatContribution(DamageStatKeys.AttackPower, power, StatStackMode.Add, AccessorySourceId),
                };
            }

            if (accessoryItemId == PlaceholderShopCatalog.NecklaceOfHasteItemId)
            {
                return new[]
                {
                    new StatContribution(UpgradeStatKeys.MoveSpeed, NecklaceMoveBonus,
                        StatStackMode.Add, AccessorySourceId),
                    new StatContribution(UpgradeStatKeys.AttackSpeed, NecklaceAttackSpeedBonus,
                        StatStackMode.Add, AccessorySourceId),
                };
            }

            if (accessoryItemId == PlaceholderShopCatalog.AmuletOfWardingItemId)
            {
                return new[]
                {
                    new StatContribution(DamageStatKeys.Defense, AmuletDefense, StatStackMode.Add, AccessorySourceId),
                };
            }

            return Empty;
        }

        /// <summary>
        /// 이 소모품의 <b>첫 번째</b> 효과. 효과가 하나인 물건(대부분)을 짧게 다루려는 편의용이다.
        /// 투척물·장신구·모르는 id 면 <c>false</c> 다.
        /// </summary>
        /// <remarks>대시 충전석·프로틴 쉐이크처럼 효과가 둘 이상인 물건은 <see cref="TryGetConsumableEffects"/> 를 써라.</remarks>
        public static bool TryGetConsumableEffect(int itemId, out ConsumableEffect effect)
        {
            if (TryGetConsumableEffects(itemId, out IReadOnlyList<ConsumableEffect> effects))
            {
                effect = effects[0];
                return true;
            }

            effect = default;
            return false;
        }

        /// <summary>
        /// 이 소모품을 쓰면 일어나는 <b>모든</b> 효과. 투척물·장신구·모르는 id 면 <c>false</c> 다.
        /// </summary>
        public static bool TryGetConsumableEffects(int itemId, out IReadOnlyList<ConsumableEffect> effects)
        {
            // 기획 엑셀(item_effects)이 있으면 그 표가 정본이다 — 회복 비율(S-GD-Q75D75)도 여기서 온다.
            var data = BettingRoyale.Data.Balance.BalanceCatalogs.Current.ItemEffects;
            if (data != null)
            {
                return data.TryGetConsumableEffects(itemId, out effects);
            }

            effects = itemId switch
            {
                PlaceholderShopCatalog.HealthPotionItemId => HealthPotionEffects,
                PlaceholderShopCatalog.SwiftnessPotionItemId => SwiftnessPotionEffects,
                RecommendedChestItemCatalog.LargeHealthPotionItemId => LargeHealthPotionEffects,
                RecommendedChestItemCatalog.SprintPotionItemId => SprintPotionEffects,
                RecommendedChestItemCatalog.FuryPotionItemId => FuryPotionEffects,
                RecommendedChestItemCatalog.FrenzyPotionItemId => FrenzyPotionEffects,
                RecommendedChestItemCatalog.BulwarkPotionItemId => BulwarkPotionEffects,
                RecommendedChestItemCatalog.ShieldCharmItemId => ShieldCharmEffects,
                RecommendedChestItemCatalog.DashStoneItemId => DashStoneEffects,
                RecommendedChestItemCatalog.RegenerationPotionItemId => RegenerationPotionEffects,
                RecommendedChestItemCatalog.InvulnerabilityCharmItemId => InvulnerabilityCharmEffects,
                RecommendedChestItemCatalog.ProteinShakeItemId => ProteinShakeEffects,
                _ => null,
            };
            return effects != null;
        }

        // 효과 묶음은 한 번만 만든다 — 사용할 때마다 배열을 새로 만들지 않는다.
        private static readonly ConsumableEffect[] HealthPotionEffects =
            { ConsumableEffect.InstantHeal(HealthPotionHeal) };

        private static readonly ConsumableEffect[] SwiftnessPotionEffects =
            { ConsumableEffect.TimedMoveSpeed(SwiftnessPotionMoveBonus, SwiftnessPotionSeconds) };

        private static readonly ConsumableEffect[] LargeHealthPotionEffects =
            { ConsumableEffect.InstantHeal(LargeHealthPotionHeal) };

        private static readonly ConsumableEffect[] SprintPotionEffects =
            { ConsumableEffect.TimedMoveSpeed(SprintPotionMoveBonus, SprintPotionSeconds) };

        private static readonly ConsumableEffect[] FuryPotionEffects =
            { ConsumableEffect.TimedDamageBonus(FuryPotionDamageBonus, FuryPotionSeconds) };

        private static readonly ConsumableEffect[] FrenzyPotionEffects =
            { ConsumableEffect.TimedAttackSpeed(FrenzyPotionAttackSpeedBonus, FrenzyPotionSeconds) };

        private static readonly ConsumableEffect[] BulwarkPotionEffects =
            { ConsumableEffect.TimedDamageTaken(BulwarkPotionDamageReduction, BulwarkPotionSeconds) };

        private static readonly ConsumableEffect[] ShieldCharmEffects =
            { ConsumableEffect.Shield(ShieldCharmAmount, ShieldCharmSeconds) };

        private static readonly ConsumableEffect[] DashStoneEffects =
        {
            ConsumableEffect.DashRefill(),
            ConsumableEffect.TimedDashRecharge(DashStoneRechargeMultiplier, DashStoneRechargeSeconds),
        };

        private static readonly ConsumableEffect[] RegenerationPotionEffects =
            { ConsumableEffect.Regeneration(RegenerationPotionHealPerSecond, RegenerationPotionSeconds) };

        private static readonly ConsumableEffect[] InvulnerabilityCharmEffects =
            { ConsumableEffect.Invulnerable(InvulnerabilityCharmSeconds) };

        private static readonly ConsumableEffect[] ProteinShakeEffects =
        {
            ConsumableEffect.TimedDefense(ProteinShakeDefense, ProteinShakeSeconds),
            ConsumableEffect.TimedAttackPower(ProteinShakeAttackPower, ProteinShakeSeconds),
        };
    }
}
