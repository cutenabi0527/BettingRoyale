using System.Linq;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 유일한 표시 순위(S-GD-7G8ZTA)와 이탈 꼴등 배점(S-GD-QJA6NY)이 <b>따로</b> 유지되는지 본다.
    /// 예전에는 이탈자의 순위가 비어 있어서 자동으로 꼴등 점수를 받았다. 이제는 순위를 받으므로
    /// 배점 정책이 조용히 사라지지 않도록 별도 사실(<c>Forfeited</c>)이 필요하다.
    /// </summary>
    [TestFixture]
    public class RoundForfeitureScoringTests
    {
        private const int PlayerCount = 8;

        private static int ScoreOf(FinalStanding[] standings, int playerId) =>
            standings.First(s => s.PlayerId == playerId).Score;

        private static PlayerMatchFacts FactsFrom(MatchRoundTally tally, int playerId, int joinOrder) =>
            new PlayerMatchFacts(playerId, tally.GetEntry(playerId), 0, 0, joinOrder);

        [Test]
        public void Forfeiter_KeepsUniquePlacement_ButScoresAsLastPlace()
        {
            var tally = new MatchRoundTally();
            tally.TryApplyRound(1, new[]
            {
                new RoundOutcome(1, kills: 0, assists: 0, rangedAssists: 0, survived: true, placement: 1),
                // 이탈자다. 표시 순위는 2등이지만 배점은 꼴등이어야 한다.
                new RoundOutcome(2, kills: 0, assists: 0, rangedAssists: 0, survived: false, placement: 2,
                    forfeited: true),
            });

            MatchTallyEntry leaver = tally.GetEntry(2);
            Assert.That(leaver.Placements.Single().Placement, Is.EqualTo(2), "표시 순위는 그대로 유일하다.");
            Assert.That(leaver.Placements.Single().ScoredAsLastPlace, Is.True);

            var standings = FinalStandings.Resolve(
                new[] { FactsFrom(tally, 1, 1), FactsFrom(tally, 2, 2) }, ScoringConfig.Default, PlayerCount);

            ScoringConfig.Default.TryGetLastPlacePoints(PlayerCount, out int lastPlacePoints);
            Assert.That(ScoreOf(standings, 2), Is.EqualTo(lastPlacePoints),
                "2등 배점이 아니라 꼴등 배점을 받아야 한다 (S-GD-QJA6NY).");
        }

        [Test]
        public void NonForfeiter_WithSamePlacement_KeepsPlacementPoints()
        {
            var tally = new MatchRoundTally();
            tally.TryApplyRound(1, new[]
            {
                new RoundOutcome(3, kills: 0, assists: 0, rangedAssists: 0, survived: false, placement: 2),
            });

            var standings = FinalStandings.Resolve(
                new[] { FactsFrom(tally, 3, 1) }, ScoringConfig.Default, PlayerCount);

            ScoringConfig.Default.TryGetPlacementPoints(2, PlayerCount, out int secondPlacePoints);
            ScoringConfig.Default.TryGetLastPlacePoints(PlayerCount, out int lastPlacePoints);
            Assert.That(secondPlacePoints, Is.Not.EqualTo(lastPlacePoints));
            Assert.That(ScoreOf(standings, 3), Is.EqualTo(secondPlacePoints));
        }

        [Test]
        public void LegacyOutcomeConstructors_DefaultToNotForfeited()
        {
            var legacy = new RoundOutcome(1, 2, 3, survived: true);
            var placed = new RoundOutcome(1, 2, 3, rangedAssists: 1, survived: true, placement: 4);

            Assert.That(legacy.Forfeited, Is.False);
            Assert.That(placed.Forfeited, Is.False);
            Assert.That(new RoundPlacement(1, 4).ScoredAsLastPlace, Is.False);
        }
    }
}
