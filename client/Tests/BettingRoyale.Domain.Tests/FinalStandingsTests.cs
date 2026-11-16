using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 라운드별 점수 배분과 최종 등수 (Jira S15P21A606-31 완료 조건 6).
    ///
    /// <para>
    /// <c>MatchRoundTally</c> 가 *"O-023 이 닫히면 그때 정렬을 붙인다"* 로 남겨둔 자리를 채운다.
    /// 배점은 S-GD-P7SC3Q(킬 10 / 어시 3 / 표를 앞에서 자른다), 동점 9단계는 S-GD-MNFBV7 다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class FinalStandingsTests
    {
        private static RoundPlacement[] At(params (int Round, int Placement)[] places) =>
            places.Select(p => new RoundPlacement(p.Round, p.Placement)).ToArray();

        private static PlayerMatchFacts Facts(
            int playerId,
            int kills = 0,
            int assists = 0,
            int rangedAssists = 0,
            int goldRemaining = 0,
            int goldEarnedTotal = 0,
            RoundPlacement[] placements = null,
            int countedRounds = -1,
            int lobbyJoinOrder = PlayerMatchFacts.UnassignedJoinOrder)
        {
            placements ??= System.Array.Empty<RoundPlacement>();
            var counted = countedRounds >= 0 ? countedRounds : placements.Length;
            var tally = new MatchTallyEntry(
                playerId, kills, assists, rangedAssists,
                survivedRounds: 0, countedRounds: counted, placements: placements);

            return new PlayerMatchFacts(playerId, tally, goldRemaining, goldEarnedTotal, lobbyJoinOrder);
        }

        private static FinalStanding[] Resolve(int playerCount, params PlayerMatchFacts[] facts) =>
            FinalStandings.Resolve(facts, ScoringConfig.Default, playerCount);

        private static int ScoreOf(FinalStanding[] s, int playerId) =>
            s.First(x => x.PlayerId == playerId).Score;

        // ── 라운드별 점수 배분 ────────────────────────────────────────────────

        /// <summary>순위 점수는 라운드마다 붙는다 — 3라운드 전승이면 80 이 세 번이다.</summary>
        [Test]
        public void 순위_점수는_라운드마다_붙는다()
        {
            var s = Resolve(8,
                Facts(1, placements: At((1, 1), (2, 1), (3, 1))),
                Facts(2, placements: At((1, 8), (2, 8), (3, 8))));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(240), "80 × 3");
            Assert.That(ScoreOf(s, 2), Is.EqualTo(60), "20 × 3");
        }

        [Test]
        public void 킬은_10점_어시스트는_3점이다()
        {
            var s = Resolve(8,
                Facts(1, kills: 2, assists: 3, placements: At((1, 3))),
                Facts(2, placements: At((1, 3))));

            Assert.That(ScoreOf(s, 2), Is.EqualTo(50), "3등 = 50");
            Assert.That(ScoreOf(s, 1), Is.EqualTo(50 + 20 + 9));
        }

        /// <summary>
        /// 저점 매수를 고른 라운드는 킬 점수가 배가 된다(S-GD-TMPBEF). 누적기가 라운드마다 배율을 곱해 두고,
        /// 동점 처리의 "킬 많은 순" 은 실제 킬 수를 본다.
        /// </summary>
        [Test]
        public void 라운드_킬_점수_배율은_그_라운드_킬에만_곱한다()
        {
            var tally = new MatchRoundTally();
            tally.SetKillScoreMultiplier(round: 1, playerId: 1, multiplier: 3f);
            Assert.That(tally.TryApplyRound(1, new[]
            {
                new RoundOutcome(1, kills: 2, assists: 0, survived: true),
                new RoundOutcome(2, kills: 2, assists: 0, survived: true),
            }), Is.True);
            Assert.That(tally.TryApplyRound(2, new[]
            {
                new RoundOutcome(1, kills: 1, assists: 0, survived: true),
                new RoundOutcome(2, kills: 1, assists: 0, survived: true),
            }), Is.True);

            Assert.That(tally.GetEntry(1).Kills, Is.EqualTo(3), "실제 킬 수는 그대로다");
            Assert.That(tally.GetEntry(1).ScoredKills, Is.EqualTo(2 * 3 + 1), "1라운드 킬만 3배");
            Assert.That(tally.GetEntry(2).ScoredKills, Is.EqualTo(3), "배율이 없으면 킬 수와 같다");

            var standings = FinalStandings.Resolve(new[]
            {
                new PlayerMatchFacts(1, tally.GetEntry(1), 0, 0),
                new PlayerMatchFacts(2, tally.GetEntry(2), 0, 0),
            }, ScoringConfig.Default, 2);
            Assert.That(standings.First(x => x.PlayerId == 1).KillScore, Is.EqualTo(7 * ScoringConfig.Default.KillPoints));
            Assert.That(standings.First(x => x.PlayerId == 1).Kills, Is.EqualTo(3));
        }

        [Test]
        public void 라운드_킬_점수_배율은_반영한_뒤_바꿔도_소급되지_않고_1_이하는_무시한다()
        {
            var tally = new MatchRoundTally();
            tally.SetKillScoreMultiplier(1, 1, .5f);
            tally.TryApplyRound(1, new[] { new RoundOutcome(1, kills: 2, assists: 0, survived: true) });
            Assert.That(tally.GetEntry(1).ScoredKills, Is.EqualTo(2), "킬 점수를 깎는 배율은 없다");

            tally.SetKillScoreMultiplier(1, 1, 3f);
            Assert.That(tally.TryApplyRound(1, new[] { new RoundOutcome(1, kills: 2, assists: 0, survived: true) }), Is.False);
            Assert.That(tally.GetEntry(1).ScoredKills, Is.EqualTo(2), "이미 반영한 라운드는 다시 셈하지 않는다");

            tally.ResetForNewMatch();
            tally.TryApplyRound(1, new[] { new RoundOutcome(1, kills: 2, assists: 0, survived: true) });
            Assert.That(tally.GetEntry(1).ScoredKills, Is.EqualTo(2), "새 매치에는 옛 배율이 남지 않는다");
        }

        /// <summary>배점 숫자를 코드에 박지 않는다 — 설정을 바꾸면 결과가 따라온다 (S-GD-P7SC3Q).</summary>
        [Test]
        public void 배점은_설정에서_온다()
        {
            var config = new ScoringConfig(
                new[] { 80, 65, 50, 40, 35, 30, 25, 20 },
                killPoints: 5, meleeAssistPoints: 2, rangedAssistPoints: 2);

            var s = FinalStandings.Resolve(
                new[] { Facts(1, kills: 2, assists: 3, placements: At((1, 3))) }, config, 8);

            Assert.That(s[0].Score, Is.EqualTo(50 + 10 + 6), "옛 배점 5/2 를 주면 옛 결과가 나온다");
        }

        // ── 인원이 8명 미만이면 표를 앞에서 자른다 (S-GD-P7SC3Q, O-024) ──────

        [TestCase(8, 20)]
        [TestCase(6, 30)]
        [TestCase(4, 40)]
        [TestCase(2, 65)]
        public void 인원이_적으면_표를_앞에서_자른다(int playerCount, int expectedLastPlace)
        {
            var s = Resolve(playerCount,
                Facts(1, placements: At((1, 1))),
                Facts(2, placements: At((1, playerCount))));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(80), "1등 점수는 인원과 무관하다");
            Assert.That(ScoreOf(s, 2), Is.EqualTo(expectedLastPlace));
        }

        /// <summary>
        /// 앞에서 자르는 이유 — 남아 있는 <b>간격</b>이 전부 보존된다.
        /// 점수는 매치 밖으로 안 나가므로 절대값이 아니라 차이만 의미가 있다.
        /// </summary>
        [Test]
        public void 자른_뒤에도_상위_간격이_그대로다()
        {
            foreach (var n in new[] { 8, 6, 4, 3 })
            {
                var s = Resolve(n,
                    Facts(1, placements: At((1, 1))),
                    Facts(2, placements: At((1, 2))),
                    Facts(3, placements: At((1, 3))));

                Assert.That(ScoreOf(s, 1) - ScoreOf(s, 2), Is.EqualTo(15), $"{n}인 1↔2 간격");
                Assert.That(ScoreOf(s, 2) - ScoreOf(s, 3), Is.EqualTo(15), $"{n}인 2↔3 간격");
            }
        }

        [Test]
        public void 표보다_많은_인원은_거부한다()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => Resolve(9, Facts(1, placements: At((1, 1)))));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => Resolve(1, Facts(1, placements: At((1, 1)))));
        }

        // ── 이탈자 (S-GD-QJA6NY) ─────────────────────────────────────────────

        /// <summary>순위가 없는 라운드는 꼴등 점수를 받는다. 이탈자 규칙이 여기서 성립한다.</summary>
        [Test]
        public void 순위가_없는_라운드는_꼴등_점수다()
        {
            var s = Resolve(8,
                // 1라운드만 3등이고 2·3라운드는 이탈해 순위가 없다.
                Facts(1, placements: At((1, 3)), countedRounds: 3));

            Assert.That(s[0].Score, Is.EqualTo(50 + 20 + 20));
        }

        // ── 사용자가 확인한 성질 (S-GD-P7SC3Q) ───────────────────────────────

        /// <summary>4등 1킬(50)과 3등 0킬(50)은 동점이고, 2단계(킬)가 싸운 쪽 손을 든다.</summary>
        [Test]
        public void 한_등급_아래에서_1킬이면_동점이고_싸운_쪽이_이긴다()
        {
            var s = Resolve(8,
                Facts(1, kills: 1, placements: At((1, 4))),
                Facts(2, placements: At((1, 3))));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(50));
            Assert.That(ScoreOf(s, 2), Is.EqualTo(50));
            Assert.That(s[0].PlayerId, Is.EqualTo(1), "동점이면 킬이 많은 쪽이 앞선다");
        }

        [Test]
        public void 두_킬이면_어느_구간이든_한_등급_위를_넘는다()
        {
            for (var placement = 2; placement <= 8; placement++)
            {
                var s = Resolve(8,
                    Facts(1, kills: 2, placements: At((1, placement))),
                    Facts(2, placements: At((1, placement - 1))));

                Assert.That(s[0].PlayerId, Is.EqualTo(1),
                    $"{placement}등 2킬이 {placement - 1}등 무킬을 못 넘었습니다");
            }
        }

        [Test]
        public void 다섯_킬_2등이_한_킬_1등보다_높다()
        {
            var s = Resolve(8,
                Facts(1, kills: 5, placements: At((1, 2))),
                Facts(2, kills: 1, placements: At((1, 1))));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(115));
            Assert.That(ScoreOf(s, 2), Is.EqualTo(90));
            Assert.That(s[0].PlayerId, Is.EqualTo(1));
        }

        // ── 어시스트 분류별 배점 (S-GD-P7SC3Q) ───────────────────────────────

        [Test]
        public void 오늘_배점에서는_어시스트_분류가_결과를_바꾸지_않는다()
        {
            var s = Resolve(8,
                Facts(1, assists: 4, rangedAssists: 4, placements: At((1, 5))),
                Facts(2, assists: 4, rangedAssists: 0, placements: At((1, 5))));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(ScoreOf(s, 2)), "근접·원거리 어시가 둘 다 3점이다");
        }

        /// <summary>확장 자리가 실제로 동작하는지 — 원거리를 2점으로 내리면 갈린다.</summary>
        [Test]
        public void 원거리_어시스트를_내리면_결과가_갈린다()
        {
            var config = new ScoringConfig(
                ScoringConfig.Default.PlacementPoints.ToArray(),
                killPoints: 10, meleeAssistPoints: 3, rangedAssistPoints: 2);

            var s = FinalStandings.Resolve(
                new[]
                {
                    Facts(1, assists: 4, rangedAssists: 4, placements: At((1, 5))),
                    Facts(2, assists: 4, rangedAssists: 0, placements: At((1, 5))),
                },
                config, 8);

            Assert.That(ScoreOf(s, 1), Is.EqualTo(35 + 8), "원거리 4어시 × 2");
            Assert.That(ScoreOf(s, 2), Is.EqualTo(35 + 12), "근접 4어시 × 3");
            Assert.That(s[0].PlayerId, Is.EqualTo(2));
        }

        [Test]
        public void 원거리_어시스트가_총합을_넘으면_거부한다()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new RoundOutcome(1, 0, assists: 2, rangedAssists: 3, survived: true, placement: 1));
        }

        // ── 동점 처리 8단계 (S-GD-MNFBV7) ────────────────────────────────────

        /// <summary>각 단계는 <b>앞 단계가 동점일 때만</b> 발동한다.</summary>
        [Test]
        public void 동점_처리는_단계_순서를_지킨다()
        {
            // 2단계 — 점수 동점, 킬이 가른다.
            var byKills = Resolve(8,
                Facts(1, kills: 1, placements: At((1, 4))),
                Facts(2, assists: 0, placements: At((1, 3))));
            Assert.That(byKills[0].PlayerId, Is.EqualTo(1), "2단계 킬");

            // 3단계 — 점수·킬 동점, 어시스트가 가른다.
            var byAssists = Resolve(8,
                Facts(1, kills: 1, assists: 1, goldRemaining: 0, placements: At((1, 5))),
                Facts(2, kills: 1, assists: 1, goldRemaining: 0, placements: At((1, 5))));
            Assert.That(byAssists[0].Score, Is.EqualTo(byAssists[1].Score));

            // 4단계 — 그 위가 전부 같고 남은 골드가 가른다.
            var byRemaining = Resolve(8,
                Facts(1, goldRemaining: 100, placements: At((1, 5))),
                Facts(2, goldRemaining: 900, placements: At((1, 5))));
            Assert.That(byRemaining[0].PlayerId, Is.EqualTo(2), "4단계 남은 골드");

            // 5단계 — 남은 골드까지 같고 모은 누계가 가른다.
            var byEarned = Resolve(8,
                Facts(1, goldRemaining: 100, goldEarnedTotal: 100, placements: At((1, 5))),
                Facts(2, goldRemaining: 100, goldEarnedTotal: 5000, placements: At((1, 5))));
            Assert.That(byEarned[0].PlayerId, Is.EqualTo(2), "5단계 모은 골드");
        }

        /// <summary>6·7·8단계는 <b>늦은 라운드부터</b> 본다. 마지막 라운드를 잘한 쪽을 우대한다.</summary>
        [Test]
        public void 라운드_순위는_늦은_라운드부터_본다()
        {
            // 두 사람의 순위 합이 같다(1+2+3). 3라운드에서 갈려야 한다.
            var s = Resolve(8,
                Facts(1, placements: At((1, 1), (2, 2), (3, 3))),
                Facts(2, placements: At((1, 3), (2, 2), (3, 1))));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(ScoreOf(s, 2)), "순위 점수 합이 같아야 이 테스트가 의미 있다");
            Assert.That(s[0].PlayerId, Is.EqualTo(2), "3라운드 1등이 앞선다");
        }

        [Test]
        public void 라운드_순위가_같으면_그_앞_라운드로_거슬러_올라간다()
        {
            var s = Resolve(8,
                Facts(1, placements: At((1, 1), (2, 3), (3, 2))),
                Facts(2, placements: At((1, 3), (2, 1), (3, 2))));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(ScoreOf(s, 2)));
            Assert.That(s[0].PlayerId, Is.EqualTo(2), "3라운드 동률 → 2라운드에서 갈린다");
        }

        /// <summary>8단계까지 같으면 결정론적으로 자른다 — 모든 피어가 같은 순위를 봐야 한다.</summary>
        [Test]
        public void 완전_동점이면_결정론적으로_자른다()
        {
            var a = Resolve(8,
                Facts(7, placements: At((1, 4))),
                Facts(3, placements: At((1, 4))));
            var b = Resolve(8,
                Facts(3, placements: At((1, 4))),
                Facts(7, placements: At((1, 4))));

            Assert.That(a.Select(x => x.PlayerId), Is.EqualTo(b.Select(x => x.PlayerId)),
                "입력 순서가 달라도 결과가 같아야 한다");
        }

        // ── 결과 모양 ────────────────────────────────────────────────────────

        [Test]
        public void 순위는_1부터_빈틈없이_매겨진다()
        {
            var s = Resolve(8,
                Facts(1, placements: At((1, 1))),
                Facts(2, placements: At((1, 2))),
                Facts(3, placements: At((1, 3))));

            Assert.That(s.Select(x => x.Rank), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void 점수를_성분별로_분해해_돌려준다()
        {
            var s = Resolve(8, Facts(1, kills: 2, assists: 1, placements: At((1, 2), (2, 3))));

            Assert.That(s[0].PlacementScore, Is.EqualTo(115), "65 + 50");
            Assert.That(s[0].KillScore, Is.EqualTo(20));
            Assert.That(s[0].AssistScore, Is.EqualTo(3));
            Assert.That(s[0].Score, Is.EqualTo(138));
        }

        // ── 배점표 검증 ──────────────────────────────────────────────────────

        [Test]
        public void 오름차순_배점표는_거부한다()
        {
            Assert.Throws<System.ArgumentException>(
                () => new ScoringConfig(new[] { 20, 80 }, 10, 3, 3),
                "1등이 꼴등보다 낮으면 순위가 뒤집힌다");
        }

        [Test]
        public void 두_칸_미만_배점표는_거부한다()
        {
            Assert.Throws<System.ArgumentException>(() => new ScoringConfig(new[] { 80 }, 10, 3, 3));
        }

        // ── 누계 골드 (동점 5단계의 입력) ────────────────────────────────────

        [Test]
        public void 모은_골드_누계는_쓰거나_소멸해도_줄지_않는다()
        {
            var wallet = new Wallet();
            wallet.Earn(1_000);
            wallet.Earn(500);

            Assert.That(wallet.TotalInflow, Is.EqualTo(1_500));

            wallet.TrySpend(1_200);
            Assert.That(wallet.Gold, Is.EqualTo(300));
            Assert.That(wallet.TotalInflow, Is.EqualTo(1_500), "쓴다고 줄지 않는다");

            wallet.SettleRound(CarryOverLimit.FromSetting(100));
            Assert.That(wallet.Gold, Is.EqualTo(100));
            Assert.That(wallet.TotalInflow, Is.EqualTo(1_500), "이월로 소멸해도 줄지 않는다");

            wallet.ResetForNewMatch();
            Assert.That(wallet.TotalInflow, Is.Zero, "새 매치면 초기화된다");
        }

        // ── 실제 누적 경로 ───────────────────────────────────────────────────

        /// <summary><c>MatchRoundTally</c> 를 통해 들어온 값이 그대로 점수가 되는지.</summary>
        [Test]
        public void 라운드_누적을_거쳐도_같은_결과가_나온다()
        {
            var tally = new MatchRoundTally();
            tally.TryApplyRound(1, new[]
            {
                new RoundOutcome(1, kills: 1, assists: 0, rangedAssists: 0, survived: true, placement: 2),
                new RoundOutcome(2, kills: 0, assists: 2, rangedAssists: 2, survived: false, placement: 5),
            });
            tally.TryApplyRound(2, new[]
            {
                new RoundOutcome(1, kills: 2, assists: 1, rangedAssists: 0, survived: true, placement: 1),
                new RoundOutcome(2, kills: 0, assists: 0, rangedAssists: 0, survived: false, placement: 8),
            });

            var facts = new[]
            {
                new PlayerMatchFacts(1, tally.GetEntry(1), 0, 0),
                new PlayerMatchFacts(2, tally.GetEntry(2), 0, 0),
            };

            var s = FinalStandings.Resolve(facts, ScoringConfig.Default, 8);

            Assert.That(ScoreOf(s, 1), Is.EqualTo(65 + 80 + 30 + 3));
            Assert.That(ScoreOf(s, 2), Is.EqualTo(35 + 20 + 6));
            Assert.That(s[0].PlayerId, Is.EqualTo(1));
        }

        /// <summary>같은 라운드가 두 번 와도 순위가 두 번 세어지지 않는다.</summary>
        [Test]
        public void 라운드_재전송은_순위_점수를_부풀리지_않는다()
        {
            var tally = new MatchRoundTally();
            var round = new[] { new RoundOutcome(1, 0, 0, 0, true, 1) };

            Assert.That(tally.TryApplyRound(1, round), Is.True);
            Assert.That(tally.TryApplyRound(1, round), Is.False);

            var s = FinalStandings.Resolve(
                new[] { new PlayerMatchFacts(1, tally.GetEntry(1), 0, 0) }, ScoringConfig.Default, 8);

            Assert.That(s[0].Score, Is.EqualTo(80), "80 이 두 번 더해지면 안 된다");
        }

        /// <summary>어댑터가 부를 한 줄짜리 입구가 실제로 도는지 — 전투 누적과 지갑이 여기서 합쳐진다.</summary>
        [Test]
        public void 경제_상태에서_한_번에_최종_순위를_뽑는다()
        {
            var pool = new List<BettingRoyale.Data.Blessing.BlessingDefinition>();
            foreach (var tier in new[]
                     {
                         BettingRoyale.Data.Blessing.BlessingTier.Silver,
                         BettingRoyale.Data.Blessing.BlessingTier.Gold,
                         BettingRoyale.Data.Blessing.BlessingTier.Platinum,
                     })
            {
                for (var i = 0; i < 6; i++)
                {
                    pool.Add(new BettingRoyale.Data.Blessing.BlessingDefinition(
                        $"{tier}-{i}", $"{tier} {i}", BettingRoyale.Data.Blessing.WeaponTag.Common, tier));
                }
            }

            var economy = new MatchEconomyState(
                EconomyConfig.Default, BettingRoyale.Data.Blessing.BlessingConfig.Default, pool);
            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(1, BettingRoyale.Data.Blessing.WeaponTag.Sword),
                    new MatchParticipant(2, BettingRoyale.Data.Blessing.WeaponTag.Bow),
                },
                seed: 4242);

            economy.TryApplyRoundResults(1, new[]
            {
                new RoundOutcome(1, kills: 1, assists: 0, rangedAssists: 0, survived: true, placement: 1),
                new RoundOutcome(2, kills: 0, assists: 1, rangedAssists: 1, survived: false, placement: 2),
            });

            var standings = economy.BuildFinalStandings();

            Assert.That(standings.Length, Is.EqualTo(2));
            Assert.That(standings[0].Rank, Is.EqualTo(1));
            Assert.That(standings[0].PlayerId, Is.EqualTo(1), "2인 매치라 표는 80·65 다");
            Assert.That(standings[0].Score, Is.EqualTo(80 + 10));
            Assert.That(standings[1].Score, Is.EqualTo(65 + 3));
        }

        // ── 9단계 대기실 접속 순서 (S-GD-MNFBV7 / S-GD-MQJPFK) ───────────────

        /// <summary>
        /// 8단계까지 완전히 같으면 <b>대기실에 먼저 들어온 쪽</b>이 이긴다.
        /// 순서가 없던 시절의 fallback(<c>PlayerId</c> 오름차순)이라면 1번이 이겼을 상황이다 —
        /// 2번이 이겨야 접속 순서가 실제로 발동한 것이다.
        /// </summary>
        [Test]
        public void 대기실에_먼저_들어온_쪽이_마지막_단계에서_이긴다()
        {
            var s = Resolve(8,
                Facts(1, placements: At((1, 3)), lobbyJoinOrder: 7),
                Facts(2, placements: At((1, 3)), lobbyJoinOrder: 3));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(ScoreOf(s, 2)), "동점이어야 이 테스트가 의미 있다");
            Assert.That(s[0].PlayerId, Is.EqualTo(2), "3번째로 들어온 쪽이 7번째보다 앞선다");
            Assert.That(s[0].Rank, Is.EqualTo(1));
            Assert.That(s[1].Rank, Is.EqualTo(2),
                "매치 최종 순위에는 공동 순위를 만들지 않는다 — 서버가 rank 를 유일하게 요구한다");
        }

        /// <summary>
        /// 한쪽만 접속 순서가 확정된 과도기에는 <b>반쪽 데이터로 가르지 않는다.</b>
        /// 1번은 순서가 9(불리)지만 2번이 미확정이라 그 단계를 건너뛰고 <c>PlayerId</c> 로 떨어진다.
        /// </summary>
        [Test]
        public void 접속_순서가_한쪽만_확정되면_그_단계를_건너뛴다()
        {
            var s = Resolve(8,
                Facts(1, placements: At((1, 3)), lobbyJoinOrder: 9),
                Facts(2, placements: At((1, 3)), lobbyJoinOrder: PlayerMatchFacts.UnassignedJoinOrder));

            Assert.That(s[0].PlayerId, Is.EqualTo(1),
                "순서 9 가 미확정에게 지지 않는다 — 그 단계를 아예 건너뛰고 PlayerId 로 갈린다");
        }

        /// <summary>
        /// Host 가 아직 접속 순서를 실어 나르지 않는 현재 동작을 고정한다.
        /// <b>이 fallback 은 스펙이 아니다</b> — 값이 들어오기 시작하면 위 테스트가 이 경로를 대체한다.
        /// </summary>
        [Test]
        public void 접속_순서가_둘_다_없으면_PlayerId_로_떨어진다()
        {
            var s = Resolve(8,
                Facts(2, placements: At((1, 3))),
                Facts(1, placements: At((1, 3))));

            Assert.That(s[0].PlayerId, Is.EqualTo(1), "미확정끼리는 PlayerId 오름차순 (임시 처리)");
        }

        /// <summary>접속 순서는 <b>마지막</b> 단계다. 앞 단계에서 갈리면 보지 않는다.</summary>
        [Test]
        public void 접속_순서는_앞_단계가_갈리면_보지_않는다()
        {
            var s = Resolve(8,
                Facts(1, goldRemaining: 900, placements: At((1, 3)), lobbyJoinOrder: 9),
                Facts(2, goldRemaining: 100, placements: At((1, 3)), lobbyJoinOrder: 1));

            Assert.That(s[0].PlayerId, Is.EqualTo(1),
                "4단계 남은 골드가 먼저 갈리므로 1번째로 들어온 쪽이 이기지 못한다");
        }

        [Test]
        public void 접속_순서는_음수를_거부한다()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => Facts(1, placements: At((1, 1)), lobbyJoinOrder: -1));
        }

        /// <summary>
        /// 라운드 안에서는 <b>공동 순위가 나올 수 있다</b> (S-GD-P8V682). 정산이 그것을 견뎌야 한다 —
        /// 같은 라운드 순위를 둘이 받아도 배점표 조회가 같은 점수를 주고, 최종 순위는 뒤 단계로 넘어간다.
        /// </summary>
        [Test]
        public void 라운드_공동_순위를_둘이_받아도_같은_점수를_준다()
        {
            var s = Resolve(8,
                Facts(1, placements: At((1, 3))),
                Facts(2, placements: At((1, 3))));

            Assert.That(ScoreOf(s, 1), Is.EqualTo(50), "공동 3등이면 둘 다 3등 점수다");
            Assert.That(ScoreOf(s, 2), Is.EqualTo(50));
            Assert.That(s[0].Rank, Is.EqualTo(1), "그래도 최종 순위는 유일하게 나온다");
            Assert.That(s[1].Rank, Is.EqualTo(2));
        }

        /// <summary>
        /// 공동 순위 다음 사람은 <b>번호를 건너뛴다</b> — SQL 의 <c>RANK()</c> 방식이다
        /// (S-GD-P8V682, 조영운 2026-09-04 확정). 3등이 둘이면 다음은 <b>5등</b>이고 4등은 없다.
        ///
        /// <para>
        /// ⚠️ <b>이것은 순위 번호를 매기는 규칙의 테스트가 아니다.</b> 번호를 만드는 것은 전투·어댑터
        /// 쪽이고 아직 없다. 여기서 잠그는 것은 <b>건너뛴 번호가 들어와도 정산이 올바른 배점을
        /// 조회하고 "인원 밖 순위" 폴백에 걸리지 않는다</b> 는 것이다.
        /// </para>
        /// </summary>
        [Test]
        public void 공동_순위_다음_사람은_건너뛴_번호로_배점을_받는다()
        {
            // 1·2·3·3·5·6·7·8 — 3등이 둘이라 4등이 없다.
            var s = Resolve(8,
                Facts(1, placements: At((1, 1))),
                Facts(2, placements: At((1, 2))),
                Facts(3, placements: At((1, 3))),
                Facts(4, placements: At((1, 3))),
                Facts(5, placements: At((1, 5))),
                Facts(6, placements: At((1, 6))),
                Facts(7, placements: At((1, 7))),
                Facts(8, placements: At((1, 8))));

            Assert.That(ScoreOf(s, 3), Is.EqualTo(50), "공동 3등 둘 다 3등 점수");
            Assert.That(ScoreOf(s, 4), Is.EqualTo(50));
            Assert.That(ScoreOf(s, 5), Is.EqualTo(35),
                "RANK 방식이라 다음은 5등(35점)이다 — DENSE_RANK 였다면 4등(40점)이었다");

            Assert.That(s.Any(x => x.Score == 40), Is.False, "4등 점수를 받은 사람이 없어야 한다");
            Assert.That(ScoreOf(s, 8), Is.EqualTo(20), "꼴등 폴백이 아니라 정상 조회여야 한다");
            Assert.That(s.Sum(x => x.Score), Is.EqualTo(355),
                "동점 없으면 345 인데 공동 3등 하나로 +10 이다. DENSE_RANK 였다면 375 가 된다");
        }
    }
}