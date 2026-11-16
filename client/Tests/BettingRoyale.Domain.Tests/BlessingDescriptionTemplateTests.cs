using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Remote;
using MessagePack;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복 설명 템플릿 채우기 (S15P21A606-201). 숫자 표기는 <c>scripts/generate-blessings.py</c> 와 같아야 한다 —
    /// 코드에 직접 적힌 60종의 설명문이 그 생성기로 만들어졌다.
    /// </summary>
    [TestFixture]
    public sealed class BlessingDescriptionTemplateTests
    {
        private static Dictionary<string, string> Values(params (string Key, string Value)[] pairs) =>
            pairs.ToDictionary(p => p.Key, p => p.Value);

        // ── 숫자 표기 — 생성기의 test_generate_blessings.py 와 같은 사례 ─────

        [TestCase("3", "3")]
        [TestCase("2.20", "2.2")]
        [TestCase("1.25", "1.25")]
        [TestCase("100", "100")]
        [TestCase("0.08", "0.08")]
        [TestCase("0", "0")]
        [TestCase("-0", "0")]
        [TestCase("-0.0", "0")]
        public void 값은_끝의_0을_떼고_적는다(string raw, string expected)
        {
            Assert.That(BlessingDescriptionTemplate.Render("{v}", Values(("v", raw))), Is.EqualTo(expected));
        }

        [TestCase("0.06", "6%")]
        [TestCase("0.12", "12%")]
        [TestCase("0.065", "6.5%")]
        [TestCase("1", "100%")]
        [TestCase("0.07", "7%")]
        public void 백분율은_100을_곱해_퍼센트를_붙인다(string raw, string expected)
        {
            // 0.07 은 double 로 곱하면 7.000000000000001 이 된다. decimal 로 계산해야 한다.
            Assert.That(BlessingDescriptionTemplate.Render("{v:pct}", Values(("v", raw))), Is.EqualTo(expected));
        }

        // ── 부호 — 크기만 적고 방향은 설명문의 말이 정한다 (S-UX-B3CJ97) ──────

        [TestCase("-0.10", "10%")]
        [TestCase("-0.40", "40%")]
        [TestCase("-1", "100%")]
        public void 음수_백분율도_크기만_적는다(string raw, string expected)
        {
            // "-10% 줄어든다" 는 이중 부정이라 반대로 읽힌다. 방향은 엑셀 설명문이 말로 적는다.
            Assert.That(BlessingDescriptionTemplate.Render("{v:pct}", Values(("v", raw))), Is.EqualTo(expected));
        }

        [TestCase("-15", "15")]
        [TestCase("-2.5", "2.5")]
        public void 음수_숫자도_크기만_적는다(string raw, string expected)
        {
            Assert.That(BlessingDescriptionTemplate.Render("{v}", Values(("v", raw))), Is.EqualTo(expected));
        }

        [Test]
        public void 언더독_설명은_받는_피해가_10퍼센트_줄어든다고_적는다()
        {
            // 엑셀 원문 그대로. damage_taken 은 계산에 쓰는 증감분이라 음수이고, 글은 이미 "줄어든다" 다.
            string text = BlessingDescriptionTemplate.Render(
                "나보다 체력 비율이 높은 적에게 주는 피해가 {damage_bonus:pct} 오르고 받는 피해가 {damage_taken:pct} 줄어든다.",
                Values(("damage_bonus", "0.50"), ("damage_taken", "-0.20")));

            Assert.That(text, Is.EqualTo(
                "나보다 체력 비율이 높은 적에게 주는 피해가 50% 오르고 받는 피해가 20% 줄어든다."));
            Assert.That(text, Does.Not.Contain("-"));
        }

        // ── 채우기 ─────────────────────────────────────────────────────────

        [Test]
        public void 자리표시자를_전부_채운다()
        {
            string text = BlessingDescriptionTemplate.Render(
                "최대 체력의 {hp_cost:pct}를 걸고 {duration}초 동안 피해를 {damage_bonus:pct} 올린다.",
                Values(("hp_cost", "0.1"), ("duration", "6"), ("damage_bonus", "0.40")));

            Assert.That(text, Is.EqualTo("최대 체력의 10%를 걸고 6초 동안 피해를 40% 올린다."));
        }

        [Test]
        public void 하이픈_키도_채운다()
        {
            // 엑셀은 능력치 파라미터를 능력치 이름 그대로(move-speed) 적는다. 생성기와 다른 점이다.
            string text = BlessingDescriptionTemplate.Render(
                "이동 속도가 {move-speed:pct}, 공격 속도가 {attack-speed:pct} 오른다.",
                Values(("move-speed", "0.20"), ("attack-speed", "0.20")));

            Assert.That(text, Is.EqualTo("이동 속도가 20%, 공격 속도가 20% 오른다."));
        }

        [Test]
        public void 자리표시자가_없으면_그대로다()
        {
            Assert.That(BlessingDescriptionTemplate.Render("입힌 피해만큼 회복한다.", Values()),
                Is.EqualTo("입힌 피해만큼 회복한다."));
            Assert.That(BlessingDescriptionTemplate.Render(null, Values()), Is.EqualTo(string.Empty));
        }

        [Test]
        public void 숫자가_아닌_값은_그대로_적는다()
        {
            var problems = new List<string>();
            Assert.That(BlessingDescriptionTemplate.Render("{src} 면역", Values(("src", "DOT")), problems),
                Is.EqualTo("DOT 면역"));
            Assert.That(problems, Is.Empty);
        }

        // ── 채울 수 없을 때 — 던지지 않고 원문을 남기고 알린다 ──────────────

        [Test]
        public void 없는_키는_원문을_남기고_문제로_적는다()
        {
            var problems = new List<string>();

            string text = BlessingDescriptionTemplate.Render(
                "치명타 확률이 {crit_chance:pct} 오른다.", Values(("crit-chance", "0.25")), problems, "critical-t1");

            Assert.That(text, Is.EqualTo("치명타 확률이 {crit_chance:pct} 오른다."),
                "지어낸 숫자보다 중괄호가 보이는 편이 낫다 — 기획이 바로 알아챈다.");
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("critical-t1").And.Contain("crit_chance"));
        }

        [Test]
        public void 해석할_수_없는_자리표시자는_원문을_남긴다()
        {
            var problems = new List<string>();

            Assert.That(BlessingDescriptionTemplate.Render("{Big Bonus}", Values(("Big Bonus", "1")), problems),
                Is.EqualTo("{Big Bonus}"));
            Assert.That(problems, Has.Count.EqualTo(1));
        }

        [Test]
        public void 백분율인데_숫자가_아니면_문제로_적는다()
        {
            var problems = new List<string>();

            Assert.That(BlessingDescriptionTemplate.Render("{v:pct}", Values(("v", "DOT")), problems), Is.EqualTo("DOT"));
            Assert.That(problems, Has.Count.EqualTo(1));
        }

        [Test]
        public void 짝이_안_맞는_중괄호는_문제로_적는다()
        {
            var problems = new List<string>();

            BlessingDescriptionTemplate.Render("피해가 {v 오른다.", Values(("v", "1")), problems);

            Assert.That(problems, Has.Count.EqualTo(1));
        }

        // ── 로더 연결 ───────────────────────────────────────────────────────

        [TearDown]
        public void TearDown() => GameContentProvider.Reset();

        /// <summary>로더가 만든 축복의 설명문에 중괄호가 남지 않아야 한다 — 축복 카드가 이 문자열을 그대로 그린다.</summary>
        [Test]
        public void 로더가_설명문을_채워서_넘긴다()
        {
            var data = new GameData
            {
                SchemaVersion = GameDataLoader.SupportedSchemaVersion,
                Blessings = new[]
                {
                    new BlessingRow
                    {
                        Id = "critical-t1",
                        Name = "치명적인 공격",
                        Tier = "Silver",
                        Tag = "Common",
                        DescriptionTemplate = "치명타 확률이 {crit-chance:pct} 오른다.",
                        Parameters = new[] { new KeyValueRow { Key = "crit-chance", Value = "0.25" } },
                    },
                },
                Stats = new[] { new StatRow { Id = "crit-chance", DisplayName = "치명타 확률", Kind = "Flat" } },
            };

            GameDataLoadResult result = GameDataLoader.Load(MessagePackSerializer.Serialize(data));

            Assert.That(result.Success, Is.True, result.Failure);
            Assert.That(result.Content.Blessings.Single().Description, Is.EqualTo("치명타 확률이 25% 오른다."));
            Assert.That(result.Warnings.Where(w => w.Contains("critical-t1")), Is.Empty);
        }

        [Test]
        public void 같은_파라미터가_두_번이면_먼저_온_값으로_채운다()
        {
            var data = new GameData
            {
                SchemaVersion = GameDataLoader.SupportedSchemaVersion,
                Blessings = new[]
                {
                    new BlessingRow
                    {
                        Id = "dup-t1",
                        Name = "중복",
                        Tier = "Silver",
                        Tag = "Common",
                        DescriptionTemplate = "{duration}초",
                        Parameters = new[]
                        {
                            new KeyValueRow { Key = "duration", Value = "3" },
                            new KeyValueRow { Key = "duration", Value = "9" },
                        },
                    },
                },
            };

            GameDataLoadResult result = GameDataLoader.Load(MessagePackSerializer.Serialize(data));

            Assert.That(result.Content.Blessings.Single().Description, Is.EqualTo("3초"));
        }

        [Test]
        public void 로더는_채우지_못한_설명문을_경고로_올린다()
        {
            var data = new GameData
            {
                SchemaVersion = GameDataLoader.SupportedSchemaVersion,
                Blessings = new[]
                {
                    new BlessingRow
                    {
                        Id = "typo-t1",
                        Name = "오타",
                        Tier = "Silver",
                        Tag = "Common",
                        DescriptionTemplate = "피해가 {damage_bonsu:pct} 오른다.",
                        Parameters = new[] { new KeyValueRow { Key = "damage_bonus", Value = "0.1" } },
                    },
                },
            };

            GameDataLoadResult result = GameDataLoader.Load(MessagePackSerializer.Serialize(data));

            Assert.That(result.Success, Is.True, "설명문 하나 때문에 게임 데이터 전체를 버리지 않는다.");
            Assert.That(result.Warnings.Any(w => w.Contains("typo-t1") && w.Contains("damage_bonsu")), Is.True);
        }
    }
}
