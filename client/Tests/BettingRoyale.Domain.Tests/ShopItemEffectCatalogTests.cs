using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상점 소모품·장신구의 효과가 <b>확정 스펙 그대로</b> 스탯 시트에 들어가는지 못 박는다
    /// (S-GD-YMKKDJ, S-GD-ZD76H8 을 대체).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>무엇이 깨져 있었나.</b> 상점에서 살 수는 있는데 마셔도 장착해도 아무 일이 없었다.
    /// 효과 수치를 담은 코드가 저장소에 존재한 적이 없다 — 앞 스펙 자신이
    /// "효과 = 스탯 시트 적용 경로 = ⏳ 미구현" 이라고 적어두었다.
    /// </para>
    /// <para>
    /// ⚠️ <b>여기서 보는 것은 "시트에 올바른 값이 들어가는가" 까지다.</b> 그 값이 실제 전투에서
    /// 체감되는지는 별개이고, 이동·공격·피해 컴포넌트가 그 시트를 읽어야 완성된다.
    /// </para>
    /// </remarks>
    public class ShopItemEffectCatalogTests
    {
        private StatSheet sheet;

        [SetUp]
        public void SetUp() => sheet = new StatSheet(PlaceholderBlessingBalance.CreateStatRegistry());

        // ── 장신구 ───────────────────────────────────────────────────────────

        [Test]
        public void RingOfStrength_GivesMorePowerToMelee()
        {
            ShopItemEffectCatalog.ApplyAccessoryToSheet(
                sheet, PlaceholderShopCatalog.RingOfStrengthItemId, isRangedWeapon: false);

            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void RingOfStrength_GivesLessPowerToRanged()
        {
            ShopItemEffectCatalog.ApplyAccessoryToSheet(
                sheet, PlaceholderShopCatalog.RingOfStrengthItemId, isRangedWeapon: true);

            // 근접 +5 / 원거리 +3 — 무기에 따라 다른 것이 이 장신구의 규칙이다.
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void NecklaceOfHaste_SpeedsUpMovementAndAttacks()
        {
            ShopItemEffectCatalog.ApplyAccessoryToSheet(
                sheet, PlaceholderShopCatalog.NecklaceOfHasteItemId, isRangedWeapon: false);

            // 두 스탯 모두 기준값 1.0 에 증감분이 더해진다 — 강화 세트와 같은 규약이다.
            Assert.That(sheet.Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1.05f).Within(0.001f));
            Assert.That(sheet.Get(UpgradeStatKeys.AttackSpeed), Is.EqualTo(1.20f).Within(0.001f));
        }

        [Test]
        public void AmuletOfWarding_GivesFiveDefense()
        {
            ShopItemEffectCatalog.ApplyAccessoryToSheet(
                sheet, PlaceholderShopCatalog.AmuletOfWardingItemId, isRangedWeapon: false);

            // ⚠️ +7 이다. 앞 스펙(S-GD-ZD76H8)의 +5 를 S-GD-YMKKDJ 가 대체했다.
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void SwappingAccessory_DropsTheOldContribution()
        {
            ShopItemEffectCatalog.ApplyAccessoryToSheet(
                sheet, PlaceholderShopCatalog.AmuletOfWardingItemId, isRangedWeapon: false);
            ShopItemEffectCatalog.ApplyAccessoryToSheet(
                sheet, PlaceholderShopCatalog.RingOfStrengthItemId, isRangedWeapon: false);

            // 장신구는 한 개만 지닌다(S-GD-B7Q44N). 이전 것이 남으면 둘 다 붙은 셈이 된다.
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(0f).Within(0.001f));
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void ApplyingTheSameAccessoryTwice_DoesNotStack()
        {
            for (int i = 0; i < 3; i++)
            {
                ShopItemEffectCatalog.ApplyAccessoryToSheet(
                    sheet, PlaceholderShopCatalog.AmuletOfWardingItemId, isRangedWeapon: false);
            }

            // 스냅샷이 주기적으로 오면 이 함수가 여러 번 불린다. 겹치면 방어력이 21 이 된다.
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void NoAccessory_ContributesNothing()
        {
            ShopItemEffectCatalog.ApplyAccessoryToSheet(sheet, 0, isRangedWeapon: false);

            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(0f).Within(0.001f));
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(0f).Within(0.001f));
            Assert.That(sheet.Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void UnknownItem_ContributesNothingInsteadOfThrowing()
        {
            // 데이터가 어긋나도 전투가 멈추면 안 된다.
            Assert.DoesNotThrow(() => ShopItemEffectCatalog.ApplyAccessoryToSheet(sheet, 999999, false));
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void AccessoryDoesNotWipeOtherSources()
        {
            // 강화·축복은 다른 출처 이름을 쓴다. 장신구를 갈아 끼워도 그쪽은 남아야 한다.
            sheet.Add(DamageStatKeys.Defense, 3f, StatStackMode.Add, "upgrade");
            ShopItemEffectCatalog.ApplyAccessoryToSheet(
                sheet, PlaceholderShopCatalog.AmuletOfWardingItemId, isRangedWeapon: false);
            ShopItemEffectCatalog.ApplyAccessoryToSheet(sheet, 0, isRangedWeapon: false);

            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(3f).Within(0.001f));
        }

        // ── 소모품 ───────────────────────────────────────────────────────────

        [Test]
        public void HealthPotion_HealsTwentyFive()
        {
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffect(
                PlaceholderShopCatalog.HealthPotionItemId, out ConsumableEffect effect), Is.True);

            Assert.That(effect.Kind, Is.EqualTo(ConsumableEffectKind.InstantHeal));
            Assert.That(effect.HealAmount, Is.EqualTo(25f).Within(0.001f));
            Assert.That(effect.DurationSeconds, Is.Zero, "즉시 효과는 지속 시간이 없다.");
        }

        [Test]
        public void SwiftnessPotion_GivesTenPercentForFiveSeconds()
        {
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffect(
                PlaceholderShopCatalog.SwiftnessPotionItemId, out ConsumableEffect effect), Is.True);

            Assert.That(effect.Kind, Is.EqualTo(ConsumableEffectKind.TimedMoveSpeed));
            Assert.That(effect.MoveSpeedBonus, Is.EqualTo(0.10f).Within(0.001f),
                "증감분이다 — 1.10 이 아니라 0.10 이어야 스탯 시트 규약과 맞는다.");
            Assert.That(effect.DurationSeconds, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void ThrowingBomb_IsNotAConsumableEffect()
        {
            // 마력 폭발병은 투척물이라 피해·반경이 RecommendedMagicBottleCatalog 에 따로 있다.
            // 여기서 효과를 주면 같은 아이템이 두 경로로 작동한다.
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffect(
                PlaceholderShopCatalog.ThrowingBombItemId, out _), Is.False);
        }

        [Test]
        public void AccessoriesAreNotConsumable()
        {
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffect(
                PlaceholderShopCatalog.AmuletOfWardingItemId, out _), Is.False);
        }
    }
}
