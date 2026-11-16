using System.Linq;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 강화 창 부위 카드의 "한 단계 더 올리면 지금보다 얼마나 오르는가" (S-UX-FBN9YW · S-UX-7M655J).
    /// 표가 0강 대비 누적이라 오르는 양은 다음 − 지금이다 (S-GD-A2YDDJ).
    /// </summary>
    public class UpgradeGainGuideTests
    {
        private static UpgradeCatalog Catalog => UpgradeSetBonusGuide.DefaultCatalog;

        private static string[] Gains(UpgradeSlot slot, WeaponType weapon, int level) =>
            UpgradeGainGuide.NextGains(Catalog, slot, weapon, level).Select(s => UpgradeSetBonusGuide.DescribeStat(s)).ToArray();

        [Test]
        public void 검_3강에서_4강은_누적표의_차이만큼_오른다()
        {
            // 검 3강 { 공격력 9, 공속 15% } → 4강 { 12, 20% } (엑셀 upgrades, S15P21A606-304)
            Assert.That(Gains(UpgradeSlot.Weapon, WeaponType.Sword, 3),
                Is.EqualTo(new[] { "공격력 +3", "공격 속도 +5%" }));
        }

        [Test]
        public void 활은_같은_단계에서도_검과_다른_표를_읽는다()
        {
            // 활 4강 { 10.6, 20% } → 5강 { 14, 25% } · 검 4강 { 12 } → 5강 { 16 } (엑셀 upgrades, S15P21A606-304)
            Assert.That(Gains(UpgradeSlot.Weapon, WeaponType.Bow, 4),
                Is.EqualTo(new[] { "공격력 +3.4", "공격 속도 +5%" }));
            Assert.That(Gains(UpgradeSlot.Weapon, WeaponType.Sword, 4),
                Is.EqualTo(new[] { "공격력 +4", "공격 속도 +5%" }));
        }

        [Test]
        public void 변하지_않는_스탯은_적지_않는다()
        {
            // 방어구 3강 { 최대 체력 36, 방어력 3.6 } → 4강 { 50, 4.8 } — 둘 다 오르면 둘 다 적는다 (엑셀 upgrades, S-GD-2TZT7S).
            Assert.That(Gains(UpgradeSlot.Armor, WeaponType.Sword, 3), Is.EqualTo(new[] { "최대 체력 +14", "방어력 +1.2" }));
            // 0강 → 1강 검 { 3, 5% } — 표에 없는 스탯(관통)은 적지 않는다.
            Assert.That(Gains(UpgradeSlot.Weapon, WeaponType.Sword, 0), Is.EqualTo(new[] { "공격력 +3", "공격 속도 +5%" }));
        }

        [Test]
        public void 최대_단계면_오를_것이_없다()
        {
            Assert.That(Gains(UpgradeSlot.Weapon, WeaponType.Sword, 5), Is.Empty);
            Assert.That(UpgradeGainGuide.Describe(Catalog, UpgradeSlot.Armor, WeaponType.Sword, 5, 5), Is.Empty);
        }

        [TestCase(2, 3, 3)]   // 방어구 2 → 3, 무기 3 — 세트 2 → 3, 3강 세트
        [TestCase(0, 5, 1)]   // 0 → 1, 상대 5 — 1강 세트
        [TestCase(3, 5, 0)]   // 3 → 4, 상대 5 — 세트 3 → 4, 4강 세트는 없다
        [TestCase(4, 5, 5)]   // 4 → 5, 상대 5 — 5강 세트
        [TestCase(2, 1, 0)]   // 2 → 3 이어도 상대가 1 이면 세트는 1 그대로
        public void 낮은_쪽을_올려_새_세트에_닿을_때만_세트_달성을_알린다(int level, int other, int expected)
        {
            Assert.That(UpgradeGainGuide.SetTierReachedBy(Catalog, level, other), Is.EqualTo(expected));
        }

        [Test]
        public void 세트에_닿으면_오르는_스탯_뒤에_한_줄을_붙인다()
        {
            string text = UpgradeGainGuide.Describe(Catalog, UpgradeSlot.Armor, WeaponType.Sword, 2, 3, "★ {0}강 세트 달성");

            // 방어구 2강 { 24, 2.4 } → 3강 { 36, 3.6 } (엑셀 upgrades, S-GD-2TZT7S)
            Assert.That(text, Is.EqualTo("최대 체력 +12\n방어력 +1.2\n★ 3강 세트 달성"));
        }
    }
}
