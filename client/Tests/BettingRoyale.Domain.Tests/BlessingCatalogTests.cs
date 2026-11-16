using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.LocalPlay;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복 카탈로그의 <b>등급별 id 규칙</b> (S-GD-ZQMB7Y).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것은 하나다 — 상위 등급이 조용히 사라지지 않는다.</b>
    /// <see cref="BlessingDefinition"/> 의 동등성이 <c>Id</c> 만 보고 등급은 안 본다. 그래서 세 등급이
    /// 같은 id 를 쓰면 1라운드에 먹은 축복의 상위 등급이 <b>재추첨을 몇 번 하든 다시 뜨지 않는다.</b>
    /// </para>
    ///
    /// <para>
    /// <b>이게 실제로 있었던 버그다</b>(2026-09-03 실측). 그리고 <b>아무 신호 없이</b> 후보에서 빠지기만
    /// 해서 "확률이 낮은가 보다" 로 넘어갔다 — 컴파일러도 다른 테스트도 못 잡는다.
    /// 그래서 여기서 잠근다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class BlessingCatalogTests
    {
        [Test]
        public void 등급이_달라도_id_가_같으면_안_된다()
        {
            var ids = LocalPlayBlessingCatalog.All.Select(b => b.Id).ToList();

            Assert.That(
                ids.Distinct().Count(),
                Is.EqualTo(ids.Count),
                "id 가 겹친다. 겹치면 상위 등급이 후보에서 조용히 사라진다: "
                    + string.Join(", ", ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key)));
        }

        [Test]
        public void 등급_접미사는_t1_t2_t3_다()
        {
            // 등급 이름(실버·골드·플래티넘)을 id 에 넣지 않는다 (S-GD-ZQMB7Y).
            foreach (var blessing in LocalPlayBlessingCatalog.All)
            {
                string expected = blessing.Tier switch
                {
                    BlessingTier.Silver => "-t1",
                    BlessingTier.Gold => "-t2",
                    BlessingTier.Platinum => "-t3",
                    _ => null,
                };

                Assert.That(expected, Is.Not.Null, $"등급 표기가 없는 축복: {blessing.Id}");
                Assert.That(blessing.Id, Does.EndWith(expected), $"{blessing.Id} 의 접미사가 등급과 다르다");
                Assert.That(blessing.Id, Is.EqualTo(blessing.Id.ToLowerInvariant()),
                    "id 는 소문자와 하이픈으로 통일한다 — 동등성이 대소문자를 구분한다");
            }
        }

        /// <summary>
        /// <b>계열은 접미사를 뗀 앞부분이다.</b> 같은 계열이 세 등급 모두 있어야 등급을 모으는
        /// 빌드가 성립한다 — 지금 목록은 6계열 × 3등급이다.
        /// </summary>
        [Test]
        public void 기존_계열마다_세_등급이_모두_있다()
        {
            var bySeries = LocalPlayBlessingCatalog.All
                .Where(b => !BlessingMvpCatalog.TryGet(b.Id, out _))
                .GroupBy(b => b.Id.Substring(0, b.Id.LastIndexOf('-')))
                .ToList();

            Assert.That(bySeries.Count, Is.EqualTo(6), "계열 수가 바뀌었다");

            foreach (var series in bySeries)
            {
                Assert.That(
                    series.Select(b => b.Tier).OrderBy(t => t),
                    Is.EqualTo(new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum }),
                    $"계열 {series.Key} 에 빠진 등급이 있다");
            }
        }

        /// <summary>
        /// <b>실제 증상을 재현한다.</b> 1라운드에 먹은 축복의 상위 등급이 2라운드 후보 풀에
        /// 남아 있어야 한다. id 를 공유하던 시절에는 여기가 실패했다.
        /// </summary>
        [Test]
        public void 하위_등급을_먹어도_상위_등급은_다시_뜰_수_있다()
        {
            var pool = LocalPlayBlessingCatalog.All;
            var acquired = new BlessingLoadout();

            // 1라운드(실버)에서 하나 먹는다.
            var silver = pool.First(b => b.Tier == BlessingTier.Silver && b.Tag == WeaponTag.Common);
            acquired.Add(silver);

            string series = silver.Id.Substring(0, silver.Id.LastIndexOf('-'));

            // 같은 계열의 골드판은 아직 안 먹었으므로 "이미 가진 것" 이 아니어야 한다.
            var gold = pool.First(b => b.Tier == BlessingTier.Gold && b.Id.StartsWith(series));

            Assert.That(
                acquired.Contains(gold),
                Is.False,
                $"{silver.Id} 를 먹었다고 {gold.Id} 까지 가진 것으로 판정된다. "
                    + "이러면 상위 등급이 재추첨을 몇 번 하든 다시 뜨지 않는다");

            // 2라운드 후보를 실제로 뽑아 보면 그 골드판이 풀에 살아 있다.
            var draft = BlessingDraft.Begin(
                BlessingConfig.Default, pool, WeaponTag.Sword, round: 2, acquired, new DeterministicRandom(7));

            Assert.That(draft.Candidates.Count, Is.EqualTo(BlessingConfig.Default.CandidateCount));
            Assert.That(
                draft.Candidates.All(c => c.Blessing.Tier == BlessingTier.Gold),
                Is.True,
                "2라운드 후보는 전부 골드여야 한다");
        }

        /// <summary>
        /// <b>3라운드까지 후보를 채울 수 있어야 한다.</b> 매 라운드 하나씩 먹으면 풀이 줄어드는데,
        /// 등급별로 나뉘어 있으므로 줄어드는 것은 그 등급 안에서만이다.
        /// </summary>
        [Test]
        public void 세_라운드_내내_후보가_모자라지_않는다()
        {
            var pool = LocalPlayBlessingCatalog.All;
            var acquired = new BlessingLoadout();
            var random = new DeterministicRandom(20260909);

            for (int round = 1; round <= 3; round++)
            {
                var draft = BlessingDraft.Begin(
                    BlessingConfig.Default, pool, WeaponTag.Sword, round, acquired, random);

                Assert.That(
                    draft.Candidates.Count,
                    Is.EqualTo(BlessingConfig.Default.CandidateCount),
                    $"{round}라운드에서 후보를 채우지 못했다");

                acquired.Add(draft.Candidates[0].Blessing);
            }

            Assert.That(acquired.Acquired.Count, Is.EqualTo(3));
        }
    }
}
