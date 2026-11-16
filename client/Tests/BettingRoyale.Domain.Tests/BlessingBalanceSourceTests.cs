using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복 수치가 기획 엑셀에서 오는지, 그리고 <b>스탯과 계수가 항상 같은 출처인지</b> 못 박는다
    /// (S15P21A606-201 · D-CLI-ETYTBX).
    /// </summary>
    [TestFixture]
    public sealed class BlessingBalanceSourceTests
    {
        [TearDown]
        public void TearDown() => GameContentProvider.Reset();

        private static GameContent Content(
            IReadOnlyList<StatDefinition> stats, IReadOnlyList<BlessingEffectRow> effects) =>
            new GameContent(
                schemaVersion: 1,
                weapons: Array.Empty<WeaponRow>(),
                blessings: Array.Empty<BlessingDefinition>(),
                stats: new StatRegistry(stats),
                effects: new BlessingEffectCatalog(effects),
                constants: new Dictionary<string, string>(StringComparer.Ordinal));

        /// <summary>엑셀에만 있는 스탯. 하드코딩 대역(12종)에는 없다.</summary>
        private const string ExcelOnlyStat = "crit-chance";

        private static readonly StatDefinition[] ExcelStats =
        {
            new StatDefinition(DamageStatKeys.AttackPower, "공격력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(ExcelOnlyStat, "치명타 확률", 0.05f, StatKind.Flat, min: 0f),
        };

        private static readonly BlessingEffectRow[] ExcelEffects =
        {
            new BlessingEffectRow("critical-t1", ExcelOnlyStat, "0.10"),
        };

        // ── 출처 선택 ───────────────────────────────────────────────────────

        [Test]
        public void 게임데이터가_없으면_하드코딩_대역을_준다()
        {
            // 배치모드(CI·EditMode)의 정상 경로다 — Bootstrap 이 원격 콘텐츠를 통째로 건너뛴다.
            Assert.That(BlessingBalance.IsFromGameData, Is.False);
            Assert.That(BlessingBalance.StatRegistry.Contains(DamageStatKeys.AttackPower), Is.True);
            Assert.That(BlessingBalance.StatRegistry.Contains(ExcelOnlyStat), Is.False,
                "대역에는 엑셀에만 있는 스탯이 없다.");
        }

        [Test]
        public void 게임데이터가_있으면_그쪽_스탯과_계수를_준다()
        {
            GameContentProvider.Set(Content(ExcelStats, ExcelEffects));

            Assert.That(BlessingBalance.IsFromGameData, Is.True);
            Assert.That(BlessingBalance.StatRegistry.Contains(ExcelOnlyStat), Is.True);
            Assert.That(BlessingBalance.EffectCatalog.Contains("critical-t1"), Is.True);
        }

        [Test]
        public void 대역은_매번_같은_것을_준다()
        {
            // 스탯 정의·계수표는 조회 전용이라 매치마다 새로 만들 이유가 없다.
            Assert.That(BlessingBalance.StatRegistry, Is.SameAs(BlessingBalance.StatRegistry));
            Assert.That(BlessingBalance.EffectCatalog, Is.SameAs(BlessingBalance.EffectCatalog));
        }

        // ── 섞이지 않는다 ───────────────────────────────────────────────────

        /// <summary>
        /// 스탯만 실린 데이터는 정본으로 치지 않는다. 계수가 없으면 축복이 아무 일도 안 하는데,
        /// 그게 "엑셀대로 동작하는 것" 처럼 보이면 원인을 못 찾는다.
        /// </summary>
        [Test]
        public void 계수가_비면_스탯도_대역으로_돌아간다()
        {
            GameContentProvider.Set(Content(ExcelStats, Array.Empty<BlessingEffectRow>()));

            Assert.That(BlessingBalance.IsFromGameData, Is.False);
            Assert.That(BlessingBalance.StatRegistry.Contains(ExcelOnlyStat), Is.False);
        }

        /// <summary>
        /// 반대쪽. 스탯이 비면 <see cref="BlessingEffectBinder"/> 가 모든 계수를 "스탯이 아님" 으로
        /// 분류해 <b>조용히 전부 무효</b>가 된다.
        /// </summary>
        [Test]
        public void 스탯이_비면_계수도_대역으로_돌아간다()
        {
            GameContentProvider.Set(Content(Array.Empty<StatDefinition>(), ExcelEffects));

            Assert.That(BlessingBalance.IsFromGameData, Is.False);
            Assert.That(BlessingBalance.EffectCatalog.Contains("critical-t1"), Is.False);
        }

        // ── 왜 섞으면 안 되는가 ─────────────────────────────────────────────

        /// <summary>
        /// <b>이 테스트가 위 규칙의 이유다.</b> 엑셀 계수를 하드코딩 스탯에 물리면
        /// 스탯 이름이 없는 행이 <c>KeyNotFoundException</c> 으로 터진다.
        /// </summary>
        [Test]
        public void 엑셀_계수를_대역_스탯에_물리면_터진다()
        {
            var mixed = new BlessingEffectBinder(
                PlaceholderBlessingBalance.CreateStatRegistry(),
                new BlessingEffectCatalog(ExcelEffects));
            var sheet = new StatSheet(PlaceholderBlessingBalance.CreateStatRegistry());

            Assert.Throws<KeyNotFoundException>(
                () => mixed.Apply(sheet, new[] { "critical-t1" }),
                "대역 스탯에 없는 'crit-chance' 를 물리면 드러나야 한다.");
        }

        /// <summary>같은 출처끼리 물리면 그대로 시트에 얹힌다.</summary>
        [Test]
        public void 같은_출처끼리_물리면_스탯_기여가_된다()
        {
            GameContentProvider.Set(Content(ExcelStats, ExcelEffects));

            var binder = new BlessingEffectBinder(BlessingBalance.StatRegistry, BlessingBalance.EffectCatalog);
            var sheet = new StatSheet(BlessingBalance.StatRegistry);

            BlessingBindResult result = binder.Apply(sheet, new[] { "critical-t1" });

            Assert.That(result.AppliedStatCount, Is.EqualTo(1));
            Assert.That(sheet.Get(ExcelOnlyStat), Is.EqualTo(0.15f).Within(1e-6),
                "기본 0.05 에 기여 0.10 이 더해진다.");
        }

        [Test]
        public void 대역끼리_물려도_터지지_않는다()
        {
            // 배치모드에서 매 테스트가 이 조합으로 돈다. 여기가 깨지면 EditMode 가 통째로 깨진다.
            var binder = new BlessingEffectBinder(BlessingBalance.StatRegistry, BlessingBalance.EffectCatalog);
            var sheet = new StatSheet(BlessingBalance.StatRegistry);

            Assert.DoesNotThrow(() => binder.Apply(sheet, BlessingBalance.EffectCatalog.BlessingIds));
        }
    }
}
