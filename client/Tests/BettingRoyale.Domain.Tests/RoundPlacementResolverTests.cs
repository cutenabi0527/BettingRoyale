using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 라운드 표시 순위 계약 테스트 (S-GD-7G8ZTA).
    /// 늦은 권위 틱 → 킬 → 어시스트 → 실제 적 피해 → 대기실 입장 순서로 <b>유일한</b> 순위를 만든다.
    /// </summary>
    public class RoundPlacementResolverTests
    {
        private static RoundCombatFacts Downed(
            int playerId, int joinOrder, int tick, int kills = 0, int assists = 0, float damage = 0f,
            bool forfeited = false) =>
            new RoundCombatFacts(playerId, joinOrder, false, tick, kills, assists, damage, forfeited);

        private static RoundCombatFacts Survivor(
            int playerId, int joinOrder, int kills = 0, int assists = 0, float damage = 0f) =>
            new RoundCombatFacts(playerId, joinOrder, true, null, kills, assists, damage);

        private static int[] Order(IEnumerable<RoundCombatFacts> facts)
        {
            var list = facts.ToArray();
            return RoundPlacementResolver.Resolve(list, list.Length)
                .Select(entry => entry.PlayerId)
                .ToArray();
        }

        [Test]
        public void SurvivorLeadsEveryEliminatedPlayer_EvenWithoutKills()
        {
            var order = Order(new[]
            {
                Downed(2, 2, tick: 900, kills: 5, assists: 5, damage: 999f),
                Survivor(1, 1),
            });

            Assert.That(order, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void LaterEliminationTickWins_BeforeAnyCombatStat()
        {
            var order = Order(new[]
            {
                Downed(1, 1, tick: 100, kills: 9, assists: 9, damage: 999f),
                Downed(2, 2, tick: 101, kills: 0, assists: 0, damage: 0f),
            });

            Assert.That(order, Is.EqualTo(new[] { 2, 1 }), "다른 틱이면 전투 통계보다 탈락 시점이 우선한다.");
        }

        [Test]
        public void SameTick_ComparesKillsThenAssistsThenDamageThenJoinOrder()
        {
            // 전원 같은 틱에 빈사한 마지막 자기장 상황. 네 단계가 차례로 갈라야 한다.
            var facts = new[]
            {
                Downed(10, 4, tick: 80, kills: 1, assists: 0, damage: 10f), // 킬로 1등
                Downed(11, 3, tick: 80, kills: 0, assists: 2, damage: 10f), // 어시스트로 2등
                Downed(12, 2, tick: 80, kills: 0, assists: 1, damage: 50f), // 어시스트 1 + 피해 50
                Downed(13, 1, tick: 80, kills: 0, assists: 1, damage: 20f), // 어시스트 1 + 피해 20
                Downed(14, 5, tick: 80, kills: 0, assists: 0, damage: 0f),  // 입장 순서로 마지막
                Downed(15, 6, tick: 80, kills: 0, assists: 0, damage: 0f),
            };

            var placements = RoundPlacementResolver.Resolve(facts, facts.Length);

            Assert.That(placements.Select(p => p.PlayerId), Is.EqualTo(new[] { 10, 11, 12, 13, 14, 15 }));
            Assert.That(placements.Select(p => p.Placement), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }),
                "공동 순위와 건너뛰기가 없다.");
        }

        [Test]
        public void FullTripleKnockOut_InSameTick_IsResolvedByFinalTotals_NotArrivalOrder()
        {
            var facts = new[]
            {
                Downed(1, 3, tick: 80, kills: 1, assists: 1, damage: 40f),
                Downed(2, 1, tick: 80, kills: 1, assists: 1, damage: 40f),
                Downed(3, 2, tick: 80, kills: 1, assists: 1, damage: 41f),
            };

            var forward = Order(facts);
            var reversed = Order(facts.Reverse());

            Assert.That(forward, Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(reversed, Is.EqualTo(forward),
                "이벤트 도착 순서를 바꿔도 같은 최종 사실이면 순위가 같다.");
        }

        [Test]
        public void IdenticalStatsAcrossEveryStage_AreSplitByLobbyJoinOrder()
        {
            var order = Order(new[]
            {
                Downed(7, 9, tick: 80, kills: 2, assists: 2, damage: 33.5f),
                Downed(8, 4, tick: 80, kills: 2, assists: 2, damage: 33.5f),
            });

            Assert.That(order, Is.EqualTo(new[] { 8, 7 }), "먼저 입장한 쪽이 앞선다. PlayerId 로 가르지 않는다.");
        }

        [Test]
        public void MultipleSurvivors_AreAlsoUnique()
        {
            var placements = RoundPlacementResolver.Resolve(
                new[] { Survivor(1, 2, kills: 1), Survivor(2, 1, kills: 1) }, 2);

            Assert.That(placements.Select(p => p.PlayerId), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(placements.Select(p => p.Placement), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void RosterSize_IsMatchStartCount_NotCurrentlyConnectedCount()
        {
            var facts = new[] { Survivor(1, 1), Downed(2, 2, tick: 10) };

            Assert.That(
                RoundPlacementResolver.TryResolve(facts, 4, out _, out RoundPlacementFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(RoundPlacementFailure.RosterMismatch));
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void InvalidJoinOrder_IsRejected_InsteadOfFallingBackToPlayerId(int joinOrder)
        {
            var facts = new[] { Survivor(1, 1), Downed(2, joinOrder, tick: 5) };

            Assert.That(
                RoundPlacementResolver.TryResolve(facts, 2, out _, out RoundPlacementFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(RoundPlacementFailure.MissingJoinOrder));
        }

        [Test]
        public void DuplicateJoinOrder_IsRejected()
        {
            var facts = new[] { Downed(1, 3, tick: 5), Downed(2, 3, tick: 5) };

            Assert.That(
                RoundPlacementResolver.TryResolve(facts, 2, out _, out RoundPlacementFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(RoundPlacementFailure.DuplicateJoinOrder));
        }

        [Test]
        public void DuplicatePlayer_IsRejected()
        {
            var facts = new[] { Downed(1, 1, tick: 5), Downed(1, 2, tick: 5) };

            Assert.That(
                RoundPlacementResolver.TryResolve(facts, 2, out _, out RoundPlacementFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(RoundPlacementFailure.DuplicatePlayer));
        }

        [Test]
        public void EliminatedWithoutTick_IsRejected()
        {
            var facts = new[]
            {
                Survivor(1, 1),
                new RoundCombatFacts(2, 2, survived: false, eliminationTick: null, kills: 0, assists: 0,
                    enemyDamageDealt: 0f),
            };

            Assert.That(
                RoundPlacementResolver.TryResolve(facts, 2, out _, out RoundPlacementFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(RoundPlacementFailure.InvalidEliminationTick));
        }

        [Test]
        public void SurvivorWithTick_IsRejected()
        {
            var facts = new[]
            {
                new RoundCombatFacts(1, 1, survived: true, eliminationTick: 3, kills: 0, assists: 0,
                    enemyDamageDealt: 0f),
                Downed(2, 2, tick: 3),
            };

            Assert.That(
                RoundPlacementResolver.TryResolve(facts, 2, out _, out RoundPlacementFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(RoundPlacementFailure.InvalidEliminationTick));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        public void MalformedDamage_IsRejected(float damage)
        {
            var facts = new[] { Survivor(1, 1, damage: damage), Downed(2, 2, tick: 1) };

            Assert.That(
                RoundPlacementResolver.TryResolve(facts, 2, out _, out RoundPlacementFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(RoundPlacementFailure.InvalidCombatStats));
        }

        [Test]
        public void NullFacts_And_ZeroRoster_AreExplicitFailures()
        {
            Assert.That(
                RoundPlacementResolver.TryResolve(null, 2, out _, out RoundPlacementFailure nullFailure),
                Is.False);
            Assert.That(nullFailure, Is.EqualTo(RoundPlacementFailure.MissingFacts));

            Assert.That(
                RoundPlacementResolver.TryResolve(Array.Empty<RoundCombatFacts>(), 0, out _,
                    out RoundPlacementFailure rosterFailure),
                Is.False);
            Assert.That(rosterFailure, Is.EqualTo(RoundPlacementFailure.InvalidRosterSize));
        }

        [Test]
        public void Resolve_Throws_OnInvalidInput()
        {
            Assert.Throws<ArgumentException>(() =>
                RoundPlacementResolver.Resolve(new[] { Survivor(1, 1) }, 3));
        }

        [Test]
        public void Forfeiter_GetsUniquePlacement_ButKeepsLastPlaceScoreFlag()
        {
            var facts = new[]
            {
                Survivor(1, 1),
                Downed(2, 2, tick: 40, kills: 1),
                Downed(3, 3, tick: 12, forfeited: true),
            };

            var placements = RoundPlacementResolver.Resolve(facts, 3);

            Assert.That(placements.Select(p => p.PlayerId), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(placements.Select(p => p.Placement), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(placements[2].ScoredAsLastPlace, Is.True);
            Assert.That(placements[0].ScoredAsLastPlace, Is.False);
        }

        [Test]
        public void TwoForfeiters_DoNotShareADisplayedRank()
        {
            var placements = RoundPlacementResolver.Resolve(
                new[]
                {
                    Survivor(1, 1),
                    Downed(2, 2, tick: 5, forfeited: true),
                    Downed(3, 3, tick: 5, forfeited: true),
                },
                3);

            Assert.That(placements.Select(p => p.Placement), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(placements.Where(p => p.ScoredAsLastPlace).Select(p => p.PlayerId),
                Is.EqualTo(new[] { 2, 3 }));
        }
    }
}
