using System.Collections.Generic;
using System.Linq;
using System.Text;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// Jira S15P21A606-31 의 <b>검증 증거</b>를 도메인에서 직접 뽑아낸다.
    ///
    /// <para>
    /// 티켓이 요구하는 증거 두 가지 —
    /// <b>"3라운드 골드·후보·선택·누적 상태 표"</b> 와 <b>"새 매치 초기화 전/후 스냅샷"</b> —
    /// 를 사람이 손으로 적으면 코드와 어긋나도 아무도 모른다.
    /// 그래서 <b>실제 상태에서 뽑아내고, 동시에 단언한다.</b>
    /// </para>
    ///
    /// <para>
    /// 표는 <c>TestContext.WriteLine</c> 으로 나온다:
    /// <c>dotnet test --logger "console;verbosity=detailed"</c>
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchEconomyWalkthroughTests
    {
        private const int Sword = 1;
        private const int Bow = 2;

        private static IReadOnlyList<BlessingDefinition> Pool()
        {
            var pool = new List<BlessingDefinition>();

            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (int i = 0; i < 6; i++)
                {
                    pool.Add(new BlessingDefinition($"{tier}-공용-{i}", $"{tier} 공용 {i}", WeaponTag.Common, tier));
                }

                pool.Add(new BlessingDefinition($"{tier}-검-0", $"{tier} 검 전용", WeaponTag.Sword, tier));
                pool.Add(new BlessingDefinition($"{tier}-활-0", $"{tier} 활 전용", WeaponTag.Bow, tier));
                pool.Add(new BlessingDefinition($"{tier}-근접-0", $"{tier} 근접", WeaponTag.Melee, tier));
                pool.Add(new BlessingDefinition($"{tier}-원거리-0", $"{tier} 원거리", WeaponTag.Ranged, tier));
            }

            return pool;
        }

        private static MatchEconomyState NewMatch()
        {
            var economy = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, Pool());

            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(Sword, WeaponTag.Sword),
                    new MatchParticipant(Bow, WeaponTag.Bow),
                },
                seed: 20260901);

            return economy;
        }

        private static string Candidates(MatchEconomyState economy, int playerId) =>
            string.Join(" · ", economy.GetCandidates(playerId).Select(c => c.Blessing.DisplayName));

        // ------------------------------------------------------------ 증거 1: 3라운드 상태 표

        /// <summary>
        /// 완료 조건 1·2·4·5 를 한 흐름으로 걸으며 표를 만든다.
        /// <b>표의 숫자가 곧 단언이다</b> — 동작이 바뀌면 단언이 먼저 깨진다.
        /// </summary>
        [Test]
        public void 증거1_3라운드_골드와_후보와_선택과_누적_상태표()
        {
            var economy = NewMatch();
            var table = new StringBuilder();

            table.AppendLine();
            table.AppendLine("| 라운드 | 플레이어 | 준비 후 골드 | 후보 3개 | 선택 | 누적 |");
            table.AppendLine("| --- | --- | --- | --- | --- | --- |");

            for (int round = 1; round <= 3; round++)
            {
                economy.BeginPreparation();

                foreach (var playerId in new[] { Sword, Bow })
                {
                    var candidates = economy.GetCandidates(playerId);

                    Assert.That(candidates.Count, Is.EqualTo(3), $"{round}라운드 후보는 3개여야 한다");

                    foreach (var candidate in candidates)
                    {
                        Assert.That(
                            candidate.Blessing.IsCompatibleWith(playerId == Sword ? WeaponTag.Sword : WeaponTag.Bow),
                            Is.True,
                            $"{round}라운드에 호환되지 않는 축복이 후보로 나왔다: {candidate.Blessing.Id}");
                    }

                    string names = Candidates(economy, playerId);
                    int gold = economy.GetGold(playerId);

                    var selected = economy.TrySelect(playerId, 0);
                    Assert.That(selected, Is.Not.Null);

                    table.AppendLine(
                        $"| {round} | {(playerId == Sword ? "검" : "활")} | {gold:N0}G | {names} | {selected.DisplayName} | {economy.GetAcquired(playerId).Count}개 |");
                }

                economy.EndPreparation();

                economy.TryApplyRoundResults(round, new[]
                {
                    new RoundOutcome(Sword, kills: round, assists: 0, survived: true),
                    new RoundOutcome(Bow, kills: 0, assists: round, survived: false),
                });
            }

            Assert.That(economy.GetAcquired(Sword).Count, Is.EqualTo(3), "3라운드면 최대 3개까지 누적된다");
            Assert.That(economy.GetAcquired(Bow).Count, Is.EqualTo(3));

            TestContext.WriteLine("### 증거 1 — 3라운드 골드·후보·선택·누적");
            TestContext.WriteLine(table.ToString());
        }

        // ------------------------------------------------------------ 증거 2: 새 매치 초기화

        /// <summary>완료 조건 5 — 새 매치에서 누적이 0으로 돌아가는지 전/후로 보여준다.</summary>
        [Test]
        public void 증거2_새_매치_초기화_전후_스냅샷()
        {
            var economy = NewMatch();

            for (int round = 1; round <= 3; round++)
            {
                economy.BeginPreparation();
                economy.TrySelect(Sword, 0);
                economy.TrySelect(Bow, 0);
                economy.EndPreparation();
                economy.TryApplyRoundResults(round, new[]
                {
                    new RoundOutcome(Sword, 1, 0, true),
                    new RoundOutcome(Bow, 0, 1, false),
                });
            }

            var before = economy.BuildSnapshot(Sword);
            int beforeGold = economy.GetGold(Sword);
            int beforeAcquired = economy.GetAcquired(Sword).Count;
            int beforeRounds = economy.Tally.CountedRounds.Count;

            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(Sword, WeaponTag.Sword),
                    new MatchParticipant(Bow, WeaponTag.Bow),
                },
                seed: 20260902);

            int afterGold = economy.GetGold(Sword);
            int afterAcquired = economy.GetAcquired(Sword).Count;
            int afterRounds = economy.Tally.CountedRounds.Count;

            Assert.That(beforeAcquired, Is.EqualTo(3), "초기화 전에는 3개가 쌓여 있어야 한다");
            Assert.That(afterAcquired, Is.EqualTo(0), "새 매치에서 축복은 0개로 초기화된다");
            Assert.That(afterRounds, Is.EqualTo(0), "라운드 누적도 함께 초기화된다");

            TestContext.WriteLine("### 증거 2 — 새 매치 초기화 전/후 (검 플레이어)");
            TestContext.WriteLine();
            TestContext.WriteLine("| 항목 | 초기화 전 | 초기화 후 |");
            TestContext.WriteLine("| --- | --- | --- |");
            TestContext.WriteLine($"| 보유 골드 | {beforeGold:N0}G | {afterGold:N0}G |");
            TestContext.WriteLine($"| 누적 축복 | {beforeAcquired}개 | {afterAcquired}개 |");
            TestContext.WriteLine($"| 반영된 라운드 | {beforeRounds}개 | {afterRounds}개 |");
            TestContext.WriteLine($"| 스냅샷 라운드 | {before.Round} | {economy.Round} |");
        }

        // ------------------------------------------------------------ 증거 3: 최종 순서 입력

        /// <summary>
        /// 완료 조건 6 — 중복 합산이 안 되고, 전 참가자의 최종 순서 입력이 나온다.
        ///
        /// ⚠️ <b>정렬은 붙어 있지 않다</b>(O-025). 생존 순위 데이터가 정산 경계에 없어
        /// 배점의 가장 큰 성분을 계산할 수 없기 때문이다.
        /// </summary>
        [Test]
        public void 증거3_라운드_결과가_중복_합산되지_않고_최종_순서_입력을_제공한다()
        {
            var economy = NewMatch();

            for (int round = 1; round <= 3; round++)
            {
                economy.BeginPreparation();
                economy.TrySelect(Sword, 0);
                economy.TrySelect(Bow, 0);
                economy.EndPreparation();

                var results = new[]
                {
                    new RoundOutcome(Sword, kills: 2, assists: 1, survived: true),
                    new RoundOutcome(Bow, kills: 1, assists: 2, survived: false),
                };

                Assert.That(economy.TryApplyRoundResults(round, results), Is.True);
                Assert.That(economy.TryApplyRoundResults(round, results), Is.False,
                    "같은 라운드를 두 번 반영하면 안 된다 — 재전송은 오류가 아니라 정상이다");
            }

            var standings = economy.Tally.BuildStandingInputs();

            Assert.That(standings.Length, Is.EqualTo(2), "전 참가자가 들어가야 한다");

            var sword = standings.First(s => s.PlayerId == Sword);
            Assert.That(sword.Kills, Is.EqualTo(6), "3라운드 × 2킬. 중복 합산되면 12가 된다");
            Assert.That(sword.Assists, Is.EqualTo(3));
            Assert.That(sword.SurvivedRounds, Is.EqualTo(3));
            Assert.That(sword.CountedRounds, Is.EqualTo(3));

            TestContext.WriteLine("### 증거 3 — 최종 순서 입력 (3라운드 종료 시점)");
            TestContext.WriteLine();
            TestContext.WriteLine("| 플레이어 | 킬 | 어시스트 | 생존 라운드 | 반영 라운드 |");
            TestContext.WriteLine("| --- | --- | --- | --- | --- |");

            foreach (var entry in standings)
            {
                TestContext.WriteLine(
                    $"| {(entry.PlayerId == Sword ? "검" : "활")} | {entry.Kills} | {entry.Assists} | {entry.SurvivedRounds} | {entry.CountedRounds} |");
            }

            TestContext.WriteLine();
            TestContext.WriteLine("⚠️ 정렬은 붙어 있지 않다 — 생존 순위 데이터가 없어 배점을 계산할 수 없다 (O-025).");
        }
    }
}
