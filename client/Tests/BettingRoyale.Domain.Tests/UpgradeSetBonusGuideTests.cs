using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 강화 세트 효과를 글로 옮기는 안내 — 강화 창 세트 카드와 HUD 효과 칸이 함께 쓴다 (S-UX-JZXPV9 · S-GD-A2YDDJ).
    /// </summary>
    public class UpgradeSetBonusGuideTests
    {
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        public void 붙는_세트_수는_세트_단계_이하인_세트를_모두_센다(int setLevel, int expected)
        {
            Assert.That(UpgradeSetBonusGuide.AchievedTierCount(UpgradeSetBonusGuide.DefaultCatalog, setLevel),
                Is.EqualTo(expected), "세트는 누적이다 — 5강이면 1·3·5강 셋.");
        }

        [Test]
        public void 세트_단계마다_주는_것을_스탯_이름과_부호로_적는다()
        {
            var bonuses = UpgradeSetBonusGuide.DefaultCatalog.SetBonuses;

            Assert.That(bonuses.Select(b => b.Level), Is.EqualTo(new[] { 1, 3, 5 }));
            // 엑셀 upgrade_sets (S15P21A606-304). 무기 전용 줄은 그 무기 이름을 앞에 붙인다.
            Assert.That(UpgradeSetBonusGuide.Describe(bonuses[0]),
                Is.EqualTo("이동 속도 +3%\n피해 증가 +3%\n받는 피해 -3%\n검: 공격 사거리 +10%\n활: 화살 충돌 반경 +10%"),
                "받는 피해는 줄어드는 것이라 음수다.");
            Assert.That(UpgradeSetBonusGuide.Describe(bonuses[1]),
                Is.EqualTo("이동 속도 +3%\n피해 증가 +3%\n받는 피해 -3%\n검: 공격 사거리 +10%\n활: 화살 충돌 반경 +10%"));
            Assert.That(UpgradeSetBonusGuide.Describe(bonuses[2], " · "),
                Is.EqualTo("이동 속도 +4% · 피해 증가 +4% · 받는 피해 -4% · 검: 공격 사거리 +10% · 활: 화살 충돌 반경 +10% · 공격력 +3 · 방어력 +2"),
                "공격력·방어력은 비율이 아니라 고정 수다.");
        }

        [Test]
        public void 안내는_경제의_기본_강화_표와_같은_세트_값을_읽는다()
        {
            // 경제는 표를 받지 않으면 PlaceholderUpgradeCatalog.Create() 를 쓴다(MatchEconomyState). 안내가 다른 표를 읽으면
            // 창에 적힌 세트와 실제로 붙는 세트가 갈린다.
            var economy = PlaceholderUpgradeCatalog.Create().SetBonuses;
            var guide = UpgradeSetBonusGuide.DefaultCatalog.SetBonuses;

            Assert.That(guide.Count, Is.EqualTo(economy.Count));
            for (int i = 0; i < guide.Count; i++)
            {
                Assert.That(guide[i].Level, Is.EqualTo(economy[i].Level));
                Assert.That(guide[i].Stats.Select(s => (s.StatId, s.Value, s.Stack)),
                    Is.EqualTo(economy[i].Stats.Select(s => (s.StatId, s.Value, s.Stack))));
            }
        }

        [TestCase(UpgradeStatKeys.MoveSpeed, 0.03f, "+3%")]
        [TestCase(UpgradeStatKeys.AttackSpeed, 0.155f, "+15.5%")]
        [TestCase(DamageStatKeys.DamageTakenMult, -0.05f, "-5%")]
        [TestCase(DamageStatKeys.AttackPower, 2f, "+2")]
        [TestCase(UpgradeStatKeys.MaxHp, 25f, "+25")]
        public void 비율_스탯만_퍼센트로_적는다(string statId, float value, string expected)
        {
            Assert.That(UpgradeSetBonusGuide.FormatValue(statId, value), Is.EqualTo(expected));
        }

        [TestCase(0, 0, 0)]
        [TestCase(5, 1, 1)]
        [TestCase(2, 4, 2)]
        [TestCase(5, 5, 5)]
        public void 세트_단계는_두_부위_중_낮은_쪽이다(int weapon, int armor, int expected)
        {
            Assert.That(UpgradeState.SetLevelOf(weapon, armor), Is.EqualTo(expected));
        }
    }
}
