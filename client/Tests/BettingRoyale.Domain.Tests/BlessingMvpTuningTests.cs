using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 코드로 동작하는 축복이 <b>엑셀 수치</b>로 도는지 (S-GD-RTB6H2). 사용자가 엑셀에서 리밸런싱했다 —
    /// 카드에 보이는 숫자와 실제 전투 숫자가 같아야 한다.
    /// </summary>
    [TestFixture]
    public sealed class BlessingMvpTuningTests
    {
        private const string BackAlley = "back-alley-t2";   // 코드 키: damage_bonus · half_angle
        private const string SafeAsset = "safe-asset-t1";   // 코드 키: damage_reduction · idle_seconds · shield · shield_seconds

        [TearDown]
        public void TearDown() => GameContentProvider.Reset();

        /// <summary>엑셀이 올라온 것으로 치려면 스탯도 한 줄은 있어야 한다(<see cref="BlessingBalance.IsFromGameData"/>).</summary>
        private static void Load(params (string Id, string Key, string Value)[] rows)
        {
            GameContentProvider.Set(new GameContent(
                schemaVersion: 1,
                weapons: Array.Empty<WeaponRow>(),
                blessings: Array.Empty<BlessingDefinition>(),
                stats: new StatRegistry(new[] { new StatDefinition("attack-power", "공격력", 0f, StatKind.Flat) }),
                effects: new BlessingEffectCatalog(rows.Select(r => new BlessingEffectRow(r.Id, r.Key, r.Value))),
                constants: new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        private static BlessingMvpDefinition Get(string id)
        {
            Assert.That(BlessingMvpCatalog.TryGet(id, out BlessingMvpDefinition entry), Is.True, id);
            return entry;
        }

        private static float Code(string id, string key) =>
            BlessingMvpCatalog.All.Single(e => e.Definition.Id == id).Number(key);

        // ── 엑셀이 없을 때 ─────────────────────────────────────────────────

        [Test]
        public void 엑셀이_없으면_코드_수치_그대로다()
        {
            BlessingMvpDefinition entry = Get(BackAlley);

            Assert.That(entry, Is.SameAs(BlessingMvpCatalog.All.Single(e => e.Definition.Id == BackAlley)),
                "배치모드(CI·EditMode)에서는 아무것도 바꾸지 않는다.");
        }

        // ── 엑셀이 있을 때 ─────────────────────────────────────────────────

        [Test]
        public void 엑셀이_있으면_엑셀_수치로_돈다()
        {
            Load((BackAlley, "damage_bonus", "1.00"), (BackAlley, "half_angle", "90"));

            BlessingMvpDefinition entry = Get(BackAlley);

            Assert.That(entry.Number("damage_bonus"), Is.EqualTo(1.0f), "엑셀 +100% — 코드의 +30% 가 아니다.");
            Assert.That(entry.Number("half_angle"), Is.EqualTo(90f));
        }

        [Test]
        public void TripleSlash_NewAngleParametersUseExcelAndIgnoreLegacyAbsoluteAngle()
        {
            const string tripleSlash = "triple-slash-t2";
            Load((tripleSlash, "half_angle", "20"),
                (tripleSlash, "half_angle_reduction", "17"),
                (tripleSlash, "min_half_angle", "12"));

            BlessingMvpDefinition entry = Get(tripleSlash);
            Assert.That(entry.Number("half_angle_reduction"), Is.EqualTo(17f));
            Assert.That(entry.Number("min_half_angle"), Is.EqualTo(12f));
            Assert.That(entry.NumberKeys, Does.Not.Contain("half_angle"));
            Assert.That(CombatantStatFormula.HalfAngle(45f, -1f,
                entry.Number("half_angle_reduction"), entry.Number("min_half_angle")), Is.EqualTo(28f));
        }

        [Test]
        public void TripleSlash_OldDataUsesNewClientFallbackAndAuditsMissingKeys()
        {
            const string tripleSlash = "triple-slash-t2";
            Load((tripleSlash, "half_angle", "20"));

            BlessingMvpDefinition entry = Get(tripleSlash);
            Assert.That(entry.Number("half_angle_reduction"), Is.EqualTo(25f));
            Assert.That(entry.Number("min_half_angle"), Is.EqualTo(10f));
            Assert.That(BlessingMvpTuning.Audit().Any(p => p.Contains(tripleSlash)
                && p.Contains("half_angle_reduction")), Is.True);
            Assert.That(BlessingMvpTuning.Audit().Any(p => p.Contains(tripleSlash)
                && p.Contains("min_half_angle")), Is.True);
        }

        [Test]
        public void 엑셀에_없는_키는_코드_값을_유지한다()
        {
            // 키를 지우면 Number 가 전투 중에 던진다. 없는 키는 옛 값으로 두고 알린다.
            Load((BackAlley, "damage_bonus", "1.00"));

            BlessingMvpDefinition entry = Get(BackAlley);

            Assert.That(entry.Number("damage_bonus"), Is.EqualTo(1.0f));
            Assert.That(entry.Number("half_angle"), Is.EqualTo(Code(BackAlley, "half_angle")));
            Assert.That(BlessingMvpTuning.Audit().Any(p => p.Contains(BackAlley) && p.Contains("half_angle")), Is.True);
        }

        [Test]
        public void 엑셀이_하이픈으로_적은_능력치_키도_찾는다()
        {
            Load((BackAlley, "damage-bonus", "0.5"), (BackAlley, "half_angle", "60"));

            Assert.That(Get(BackAlley).Number("damage_bonus"), Is.EqualTo(0.5f));
        }

        /// <summary>
        /// 엑셀은 조건부 보호막을 <c>shield_amount</c> 로 적는다 — <c>shield</c> 로 두면 능력치로 잡혀 상시 보호막이 된다.
        /// 코드의 조건부 로직은 여전히 <c>shield</c> 를 읽는다.
        /// </summary>
        [Test]
        public void 이름을_바꾼_보호막_키를_코드_키로_잇는다()
        {
            Load((SafeAsset, "idle_seconds", "3"), (SafeAsset, "damage_reduction", "0.12"),
                (SafeAsset, "shield_amount", "15"), (SafeAsset, "shield_seconds", "6"));

            BlessingMvpDefinition entry = Get(SafeAsset);

            Assert.That(entry.Number("shield"), Is.EqualTo(15f), "코드 값 12 가 아니라 엑셀 15.");
            Assert.That(entry.Number("shield_seconds"), Is.EqualTo(6f));
            Assert.That(BlessingMvpTuning.Audit().Where(p => p.Contains(SafeAsset)), Is.Empty);
        }

        [TestCase("DOT")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        public void 숫자가_아닌_엑셀_값은_쓰지_않고_알린다(string bad)
        {
            Load((BackAlley, "damage_bonus", bad), (BackAlley, "half_angle", "90"));

            BlessingMvpDefinition entry = Get(BackAlley);

            Assert.That(entry.Number("damage_bonus"), Is.EqualTo(Code(BackAlley, "damage_bonus")));
            Assert.That(entry.Number("half_angle"), Is.EqualTo(90f));
            Assert.That(BlessingMvpTuning.Audit().Any(p => p.Contains(BackAlley) && p.Contains("damage_bonus")), Is.True);
        }

        // ── 보존 ───────────────────────────────────────────────────────────

        [Test]
        public void 수치_말고는_그대로다()
        {
            BlessingMvpDefinition code = BlessingMvpCatalog.All.Single(e => e.Definition.Id == SafeAsset);
            Load((SafeAsset, "shield_amount", "15"));

            BlessingMvpDefinition tuned = Get(SafeAsset);

            Assert.That(tuned, Is.Not.SameAs(code));
            Assert.That(tuned.Definition, Is.SameAs(code.Definition), "설명·등급·이름은 코드 것 — 토큰 구간이 이 설명에 맞춰져 있다.");
            Assert.That(tuned.Effect, Is.EqualTo(code.Effect));
            Assert.That(tuned.IconIndex, Is.EqualTo(code.IconIndex));
            Assert.That(tuned.StatTokens.Count, Is.EqualTo(code.StatTokens.Count));
            Assert.That(tuned.NumberKeys, Is.EquivalentTo(code.NumberKeys), "키를 늘리거나 줄이지 않는다.");
        }

        [Test]
        public void 엑셀에_그_축복이_없으면_코드_것_그대로다()
        {
            Load((BackAlley, "damage_bonus", "1.00"));

            Assert.That(Get(SafeAsset), Is.SameAs(BlessingMvpCatalog.All.Single(e => e.Definition.Id == SafeAsset)));
        }

        // ── 캐시 ───────────────────────────────────────────────────────────

        [Test]
        public void 같은_데이터에서는_한_번만_만든다()
        {
            Load((BackAlley, "damage_bonus", "1.00"));

            Assert.That(Get(BackAlley), Is.SameAs(Get(BackAlley)), "전투 중 매 공격마다 새로 만들지 않는다.");
        }

        [Test]
        public void 데이터가_바뀌면_다시_입힌다()
        {
            Load((BackAlley, "damage_bonus", "1.00"));
            Assert.That(Get(BackAlley).Number("damage_bonus"), Is.EqualTo(1.0f));

            GameContentProvider.Reset();
            Load((BackAlley, "damage_bonus", "0.45"));

            Assert.That(Get(BackAlley).Number("damage_bonus"), Is.EqualTo(0.45f));
        }

        [Test]
        public void 엑셀이_내려가면_코드_수치로_돌아간다()
        {
            Load((BackAlley, "damage_bonus", "1.00"));
            Get(BackAlley);

            GameContentProvider.Reset();

            Assert.That(Get(BackAlley).Number("damage_bonus"), Is.EqualTo(Code(BackAlley, "damage_bonus")));
        }
    }
}
