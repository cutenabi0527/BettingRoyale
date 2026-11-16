using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 제품 Host 정산 경로를 <b>실제 계약으로</b> 통과시킨다 —
    /// 관측 런타임(<see cref="MatchRoundObservationRuntime"/>) → 사실 기록기
    /// (<see cref="CombatRoundFactLedger"/>) → 순위(<see cref="RoundSettlementPlanner"/>) →
    /// 경제 반영(<see cref="MatchEconomyState.TryApplyRoundResults"/>) →
    /// 최종 순위(<see cref="MatchEconomyState.BuildFinalStandings(ScoringConfig, IReadOnlyDictionary{int, int})"/>).
    ///
    /// <para>
    /// 대역을 쓰지 않는다. Unity·Fusion 만 빠져 있고 판정 코드는 제품과 같은 것이다.
    /// 스펙: S-GD-7G8ZTA(같은 틱 유일 순위) · S-GD-657ZN5(시간 만료 없음) · S-GD-QJA6NY(이탈 배점).
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class ProductRoundSettlementTests
    {
        private const double AssistWindowSeconds = 10d;

        /// <summary>제품과 같은 규칙 — 전투원 식별자는 PlayerId 에서 파생되며 값이 다르다.</summary>
        private static CombatantId CombatantOf(int playerId) => new CombatantId(10_000 + playerId);

        private static MatchRoundObservationRuntime CreateArmedRuntime(
            int round, params (int PlayerId, int JoinOrder)[] members)
        {
            MatchRoundObservationRuntime runtime = null;
            var recorder = new CombatRoundFactLedger(
                AssistWindowSeconds,
                (attacker, target) => runtime != null && runtime.AreEnemies(attacker, target));
            recorder.EnableStrictBatchMode();
            runtime = new MatchRoundObservationRuntime(recorder);

            var roster = members
                .Select(m => new CombatRosterMember(CombatantOf(m.PlayerId), m.PlayerId, m.JoinOrder))
                .ToArray();
            Assert.That(runtime.Arm("match-1", round, roster), Is.True, "명부 무장에 실패했다.");
            return runtime;
        }

        /// <summary>
        /// 제품 기록기(<see cref="CombatRoundFactLedger"/>)를 그대로 감싸고 호출 횟수만 센다.
        /// 판정은 하나도 대신하지 않는다 — "봉인 뒤 기록기를 다시 부르지 않는가" 만 본다.
        /// </summary>
        private sealed class CallCountingRecorder : ICombatRoundFactRecorder
        {
            private readonly ICombatRoundFactRecorder inner;

            public CallCountingRecorder(ICombatRoundFactRecorder inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public int SettleCallCount { get; private set; }
            public int DamageCallCount { get; private set; }
            public int DownedCallCount { get; private set; }
            public int BeginTickCallCount { get; private set; }
            public int EndTickCallCount { get; private set; }

            public bool IsTickOpen => inner.IsTickOpen;
            public int? OpenTick => inner.OpenTick;
            public int? LastCompletedTick => inner.LastCompletedTick;
            public bool CanFinalize => inner.CanFinalize;

            public void BeginRound(int roundNumber, IEnumerable<CombatantId> participants) =>
                inner.BeginRound(roundNumber, participants);

            public bool BeginAuthorityTick(int tick)
            {
                BeginTickCallCount++;
                return inner.BeginAuthorityTick(tick);
            }

            public bool EndAuthorityTick()
            {
                EndTickCallCount++;
                return inner.EndAuthorityTick();
            }

            public bool RecordDamageFact(
                CombatantId attacker,
                CombatantId target,
                WeaponType weapon,
                float amount,
                CombatDamageSource source,
                double occurredAt,
                int? authorityTick)
            {
                DamageCallCount++;
                return inner.RecordDamageFact(
                    attacker, target, weapon, amount, source, occurredAt, authorityTick);
            }

            public bool RecordShieldFact(
                CombatantId attacker,
                CombatantId target,
                CombatDamageSource source,
                double occurredAt,
                int? authorityTick)
            {
                DamageCallCount++;
                return inner.RecordShieldFact(attacker, target, source, occurredAt, authorityTick);
            }

            public bool RecordDownedFact(
                CombatantId combatant,
                CombatantId attacker,
                CombatDamageSource source,
                double occurredAt,
                int? authorityTick)
            {
                DownedCallCount++;
                return inner.RecordDownedFact(combatant, attacker, source, occurredAt, authorityTick);
            }

            public bool TrySettle(double occurredAt, out CombatRoundResultsReady settled)
            {
                SettleCallCount++;
                return inner.TrySettle(occurredAt, out settled);
            }

            public bool TryCopyLiveFacts(out CombatantRoundResult[] facts)
            {
                LiveFactsCallCount++;
                return inner.TryCopyLiveFacts(out facts);
            }

            /// <summary>중간 순위표가 기록기를 읽기만 하는지 보는 계수기. 정산 계수와 따로 센다.</summary>
            public int LiveFactsCallCount { get; private set; }
        }

        /// <summary>팀전 진영 해석기. 표에 없는 전투원은 개인전처럼 자기 자신이 한 진영이다.</summary>
        private sealed class TeamSideResolver : ICombatSideResolver
        {
            private readonly IReadOnlyDictionary<int, int> sideByCombatantId;

            public TeamSideResolver(IReadOnlyDictionary<int, int> sideByCombatantId)
            {
                this.sideByCombatantId = sideByCombatantId;
            }

            public int ResolveSideId(CombatantId combatantId) =>
                sideByCombatantId.TryGetValue(combatantId.Value, out int sideId)
                    ? sideId
                    : combatantId.Value;
        }

        /// <summary>팀 번호는 전투원 식별자(10_000+)와 겹치지 않는 작은 값을 쓴다.</summary>
        private const int TeamA = 1;
        private const int TeamB = 2;

        private static TeamSideResolver TeamsOf(params (int PlayerId, int Team)[] assignments) =>
            new TeamSideResolver(assignments.ToDictionary(a => CombatantOf(a.PlayerId).Value, a => a.Team));

        /// <summary>
        /// 위 <c>CreateArmedRuntime</c> 과 같은 제품 배선에 진영 해석기 주입과 기록기 호출 계수기를 얹는다.
        /// </summary>
        private static MatchRoundObservationRuntime CreateArmedRuntime(
            ICombatSideResolver sideResolver,
            out CallCountingRecorder recorder,
            int round,
            params (int PlayerId, int JoinOrder)[] members)
        {
            MatchRoundObservationRuntime runtime = null;
            var ledger = new CombatRoundFactLedger(
                AssistWindowSeconds,
                (attacker, target) => runtime != null && runtime.AreEnemies(attacker, target));
            ledger.EnableStrictBatchMode();
            var counting = new CallCountingRecorder(ledger);
            runtime = new MatchRoundObservationRuntime(counting, sideResolver);

            var roster = members
                .Select(m => new CombatRosterMember(CombatantOf(m.PlayerId), m.PlayerId, m.JoinOrder))
                .ToArray();
            Assert.That(runtime.Arm("match-1", round, roster), Is.True, "명부 무장에 실패했다.");
            recorder = counting;
            return runtime;
        }

        /// <summary>두 스냅샷이 <b>같은 사실</b>인지 명부 전원에 대해 확인한다.</summary>
        private static void AssertSameFacts(
            MatchRoundObservationRuntime runtime,
            CombatRoundResultsReady expected,
            CombatRoundResultsReady actual)
        {
            Assert.That(actual.Round, Is.EqualTo(expected.Round));
            Assert.That(actual.SettledTick, Is.EqualTo(expected.SettledTick));
            Assert.That(actual.ResultCount, Is.EqualTo(expected.ResultCount));

            foreach (CombatRosterMember member in runtime.Roster)
            {
                Assert.That(
                    expected.TryGetResult(member.Combatant, out CombatantRoundResult before), Is.True);
                Assert.That(
                    actual.TryGetResult(member.Combatant, out CombatantRoundResult after), Is.True);
                Assert.That(after.Kills, Is.EqualTo(before.Kills), $"{member} 킬이 달라졌다.");
                Assert.That(after.Assists, Is.EqualTo(before.Assists), $"{member} 어시스트가 달라졌다.");
                Assert.That(after.Survived, Is.EqualTo(before.Survived), $"{member} 생존이 달라졌다.");
                Assert.That(
                    after.EliminationOrder, Is.EqualTo(before.EliminationOrder),
                    $"{member} 빈사 순번이 달라졌다.");
                Assert.That(
                    after.EliminationTick, Is.EqualTo(before.EliminationTick),
                    $"{member} 빈사 틱이 달라졌다.");
                Assert.That(
                    after.EnemyDamageDealt, Is.EqualTo(before.EnemyDamageDealt),
                    $"{member} 실제 적 피해가 달라졌다.");
            }
        }

        private static RoundOutcome[] BuildOutcomes(
            MatchRoundObservationRuntime runtime, int round, CombatRoundResultsReady results)
        {
            var inputs = new List<RoundSettlementInput>();
            foreach (CombatRosterMember member in runtime.Roster)
            {
                results.TryGetResult(member.Combatant, out CombatantRoundResult result);
                bool forfeited = runtime.IsForfeited(member.PlayerId);
                bool survived = result.Survived && !forfeited;
                inputs.Add(new RoundSettlementInput(
                    member.PlayerId,
                    member.LobbyJoinOrder,
                    survived,
                    survived ? (int?)null : result.EliminationTick ?? runtime.LastCompletedTick ?? 0,
                    result.Kills,
                    result.Assists,
                    rangedAssists: 0,
                    result.EnemyDamageDealt,
                    forfeited));
            }

            Assert.That(
                RoundSettlementPlanner.TryBuild(
                    round, inputs, runtime.Roster.Count,
                    out RoundOutcome[] outcomes,
                    out RoundSettlementFailure failure,
                    out RoundPlacementFailure placementFailure),
                Is.True,
                $"정산 계획 실패: {failure}/{placementFailure}");
            return outcomes;
        }

        private static int PlacementOf(IEnumerable<RoundOutcome> outcomes, int playerId) =>
            outcomes.First(o => o.PlayerId == playerId).Placement;

        // ── 같은 마지막 자기장 틱의 동시 탈락 ─────────────────────────────

        [Test]
        public void 같은_최종_틱_전원_빈사도_킬_어시_실제피해_입장순서로_유일_순위가_난다()
        {
            // 입장 순서를 PlayerId 와 일부러 어긋나게 둔다 — PlayerId 로 가르면 결과가 달라진다.
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(
                1, (10, 3), (20, 1), (30, 2));

            // 틱 100: 서로 실제 피해를 주고받는다.
            Assert.That(runtime.BeginAuthorityTick(100), Is.True);
            // P30 이 먼저, P10 이 마지막으로 때린다 — 자기장 빈사의 킬은 마지막 유효 공격자다(S-GD-87YH9R).
            runtime.ObserveDamage(CombatantOf(30), CombatantOf(20), WeaponType.Bow, 10f, CombatDamageSource.Weapon, 100d);
            runtime.ObserveDamage(CombatantOf(10), CombatantOf(20), WeaponType.Sword, 30f, CombatDamageSource.Weapon, 100.5d);
            runtime.EndAuthorityTick();
            Assert.That(runtime.ShouldSettle, Is.False, "아무도 죽지 않았는데 정산 조건이 참이다.");

            // 틱 101: 마지막 자기장 틱에 세 명이 함께 빈사한다.
            Assert.That(runtime.BeginAuthorityTick(101), Is.True);
            runtime.ObserveDowned(CombatantOf(20), CombatantOf(10), CombatDamageSource.Zone, 101d);
            Assert.That(
                runtime.ShouldSettle, Is.False,
                "틱이 열려 있는 동안에는 조기 정산 조건이 참이 되면 안 된다.");
            runtime.ObserveDowned(CombatantOf(10), CombatantOf(10), CombatDamageSource.Zone, 101d);
            runtime.ObserveDowned(CombatantOf(30), CombatantOf(30), CombatDamageSource.Zone, 101d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.SurvivingSideCount, Is.Zero);
            Assert.That(runtime.ShouldSettle, Is.True, "전원 빈사면 정산해야 한다.");
            Assert.That(runtime.TrySettle(101d, out CombatRoundResultsReady results), Is.True);

            RoundOutcome[] outcomes = BuildOutcomes(runtime, 1, results);

            // P10 은 자기장 킬(마지막 유효 공격자)을 가져가 1등, P30 은 어시스트로 2등,
            // P20 은 아무 기여가 없어 3등이다. 공동 순위는 없다.
            Assert.That(outcomes.Select(o => o.Placement).OrderBy(p => p), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(PlacementOf(outcomes, 10), Is.EqualTo(1));
            Assert.That(PlacementOf(outcomes, 30), Is.EqualTo(2));
            Assert.That(PlacementOf(outcomes, 20), Is.EqualTo(3));
        }

        [Test]
        public void 이벤트_도착_순서를_뒤집어도_같은_사실이면_같은_순위다()
        {
            int[] Ranks(bool reversed)
            {
                MatchRoundObservationRuntime runtime = CreateArmedRuntime(1, (1, 1), (2, 2), (3, 3));
                Assert.That(runtime.BeginAuthorityTick(7), Is.True);

                Action first = () => runtime.ObserveDamage(CombatantOf(1), CombatantOf(3), WeaponType.Sword, 40f, CombatDamageSource.Weapon, 7d);
                Action second = () => runtime.ObserveDamage(CombatantOf(2), CombatantOf(3), WeaponType.Bow, 15f, CombatDamageSource.Weapon, 7d);

                if (reversed)
                {
                    second();
                    first();
                }
                else
                {
                    first();
                    second();
                }

                runtime.ObserveDowned(CombatantOf(3), CombatantOf(1), CombatDamageSource.Weapon, 7d);
                runtime.ObserveDowned(CombatantOf(1), CombatantOf(1), CombatDamageSource.Zone, 7d);
                runtime.ObserveDowned(CombatantOf(2), CombatantOf(2), CombatDamageSource.Zone, 7d);
                runtime.EndAuthorityTick();

                Assert.That(runtime.TrySettle(7d, out CombatRoundResultsReady results), Is.True);
                RoundOutcome[] outcomes = BuildOutcomes(runtime, 1, results);
                return new[]
                {
                    PlacementOf(outcomes, 1), PlacementOf(outcomes, 2), PlacementOf(outcomes, 3),
                };
            }

            Assert.That(Ranks(reversed: true), Is.EqualTo(Ranks(reversed: false)));
        }

        [Test]
        public void 명부_밖_전투원의_사실은_집계에도_생존_판정에도_들어가지_않는다()
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(1, (1, 1), (2, 2));
            var otherSceneDummy = new CombatantId(900054);

            Assert.That(runtime.BeginAuthorityTick(3), Is.True);
            Assert.That(
                runtime.ObserveDamage(CombatantOf(1), otherSceneDummy, WeaponType.Sword, 50f, CombatDamageSource.Weapon, 3d),
                Is.False,
                "허수아비 예상 피해가 실제 적 피해로 집계됐다.");
            Assert.That(
                runtime.ObserveDowned(otherSceneDummy, CombatantOf(1), CombatDamageSource.Weapon, 3d),
                Is.False);
            runtime.ObserveDowned(CombatantOf(2), CombatantOf(1), CombatDamageSource.Weapon, 3d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(1));
            Assert.That(runtime.TrySettle(3d, out CombatRoundResultsReady results), Is.True);
            Assert.That(results.ResultCount, Is.EqualTo(2), "명부 밖 전투원이 결과에 들어갔다.");
            Assert.That(results.TryGetResult(CombatantOf(1), out CombatantRoundResult winner), Is.True);
            Assert.That(winner.EnemyDamageDealt, Is.EqualTo(0f), "허수아비 피해가 실제 적 피해에 섞였다.");
            Assert.That(winner.Kills, Is.EqualTo(1));
        }

        [Test]
        public void 전역화가_지나도_생존자가_둘이면_정산하지_않는다()
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(1, (1, 1), (2, 2));

            // 79초·80초를 훨씬 지난 시각의 자기장 틱을 계속 처리해도 시간 만료 정산은 없다.
            for (var tick = 4_800; tick < 4_810; tick++)
            {
                Assert.That(runtime.BeginAuthorityTick(tick), Is.True);
                runtime.ObserveDamage(
                    CombatantOf(1), CombatantOf(2), WeaponType.Sword, 1f, CombatDamageSource.Weapon, tick);
                runtime.EndAuthorityTick();
                Assert.That(runtime.ShouldSettle, Is.False, "시간 만료로 정산이 열렸다 (S-GD-657ZN5 위반).");
            }

            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(2));
        }

        [Test]
        public void 중도_이탈자도_유일한_표시_순위를_받고_배점만_꼴등이다()
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(1, (1, 1), (2, 2), (3, 3));
            Assert.That(runtime.MarkForfeited(2), Is.True);
            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(2), "이탈자는 생존 진영이 아니다.");

            Assert.That(runtime.BeginAuthorityTick(11), Is.True);
            runtime.ObserveDamage(
                CombatantOf(1), CombatantOf(3), WeaponType.Sword, 60f, CombatDamageSource.Weapon, 11d);
            runtime.ObserveDowned(CombatantOf(3), CombatantOf(1), CombatDamageSource.Weapon, 11d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.ShouldSettle, Is.True);
            Assert.That(runtime.TrySettle(11d, out CombatRoundResultsReady results), Is.True);
            RoundOutcome[] outcomes = BuildOutcomes(runtime, 1, results);

            // 시작 인원 N=3 이 유지된다. 이탈자도 한 줄로 들어가고 순위는 여전히 1..3 유일이다.
            Assert.That(outcomes.Length, Is.EqualTo(3));
            Assert.That(outcomes.Select(o => o.Placement).OrderBy(p => p), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(outcomes.Single(o => o.PlayerId == 2).Forfeited, Is.True);
            Assert.That(outcomes.Single(o => o.PlayerId == 1).Forfeited, Is.False);
        }

        [Test]
        public void 대기실_입장_순서는_PlayerId_정렬과_독립이다()
        {
            // 같은 틱·같은 전투 통계라 마지막 단계인 입장 순서에서만 갈린다.
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(1, (7, 2), (3, 1));

            Assert.That(runtime.BeginAuthorityTick(5), Is.True);
            runtime.ObserveDowned(CombatantOf(7), CombatantOf(7), CombatDamageSource.Zone, 5d);
            runtime.ObserveDowned(CombatantOf(3), CombatantOf(3), CombatDamageSource.Zone, 5d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.TrySettle(5d, out CombatRoundResultsReady results), Is.True);
            RoundOutcome[] outcomes = BuildOutcomes(runtime, 1, results);

            // 입장 순서 1번인 P3 이 앞선다 — PlayerId 오름차순이었다면 P3 이 앞선 것이 우연이므로
            // 순서를 뒤집은 아래 검증이 본체다.
            Assert.That(PlacementOf(outcomes, 3), Is.EqualTo(1));

            MatchRoundObservationRuntime swapped = CreateArmedRuntime(1, (7, 1), (3, 2));
            Assert.That(swapped.BeginAuthorityTick(5), Is.True);
            swapped.ObserveDowned(CombatantOf(7), CombatantOf(7), CombatDamageSource.Zone, 5d);
            swapped.ObserveDowned(CombatantOf(3), CombatantOf(3), CombatDamageSource.Zone, 5d);
            swapped.EndAuthorityTick();
            Assert.That(swapped.TrySettle(5d, out CombatRoundResultsReady swappedResults), Is.True);
            RoundOutcome[] swappedOutcomes = BuildOutcomes(swapped, 1, swappedResults);

            Assert.That(
                PlacementOf(swappedOutcomes, 7), Is.EqualTo(1),
                "입장 순서가 아니라 PlayerId 로 갈랐다 (S-GD-7G8ZTA 위반).");
        }

        [Test]
        public void 입장_순서가_없는_명부로는_무장하지_못한다()
        {
            Assert.That(
                () => new CombatRosterMember(CombatantOf(1), 1, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>(),
                "입장 순서 미확정을 그대로 받아들이면 PlayerId 로 가르게 된다.");
        }

        [Test]
        public void 해제한_관측은_아무_사실도_받지_않는다()
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(1, (1, 1), (2, 2));
            runtime.Disarm();

            Assert.That(runtime.IsArmed, Is.False);
            Assert.That(runtime.BeginAuthorityTick(1), Is.False);
            Assert.That(
                runtime.ObserveDamage(CombatantOf(1), CombatantOf(2), WeaponType.Sword, 5f, CombatDamageSource.Weapon, 1d),
                Is.False);
            Assert.That(runtime.TrySettle(1d, out _), Is.False);
        }


        // ── 마지막 생존 진영 관찰 상태 (S15P21A606-170) ────────────────────

        /// <summary>
        /// 세 명 중 둘이 같은 틱에 빈사해 <b>마지막 생존자 하나</b>가 남은 라운드.
        /// 틱은 닫혀 있고 아직 정산하지 않았다.
        /// </summary>
        private static MatchRoundObservationRuntime LastSurvivorRound(out CallCountingRecorder recorder)
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(
                null, out recorder, 1, (1, 1), (2, 2), (3, 3));

            Assert.That(runtime.BeginAuthorityTick(50), Is.True);
            runtime.ObserveDamage(
                CombatantOf(1), CombatantOf(2), WeaponType.Sword, 30f, CombatDamageSource.Weapon, 50d);
            runtime.ObserveDamage(
                CombatantOf(3), CombatantOf(2), WeaponType.Bow, 10f, CombatDamageSource.Weapon, 50.1d);
            runtime.ObserveDowned(CombatantOf(2), CombatantOf(1), CombatDamageSource.Weapon, 50.2d);
            runtime.ObserveDowned(CombatantOf(3), CombatantOf(1), CombatDamageSource.Weapon, 50.3d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(1));
            return runtime;
        }

        [Test]
        public void 마지막_생존_진영이_남으면_정산_경계가_아니라_관찰로_들어간다()
        {
            MatchRoundObservationRuntime runtime = LastSurvivorRound(out CallCountingRecorder recorder);

            // 라운드가 끝났다는 **사실**은 참이지만, 그 자체가 "지금 요약으로 가라" 는 아니다.
            Assert.That(runtime.ShouldSettle, Is.True);
            Assert.That(runtime.HasLastSurvivingSide, Is.True);
            Assert.That(runtime.RequiresLastSurvivorObservation, Is.True);
            Assert.That(
                runtime.IsSettlementBoundaryReached, Is.False,
                "마지막 생존자가 확정되는 순간 정산 경계가 열려 관찰이 사라졌다.");

            Assert.That(runtime.TryBeginLastSurvivorObservation(), Is.True);
            Assert.That(runtime.TryBeginLastSurvivorObservation(), Is.False, "관찰 진입은 멱등이어야 한다.");
            Assert.That(runtime.IsObservingLastSurvivor, Is.True);
            Assert.That(runtime.ObservationEnteredTick, Is.EqualTo(50));
            Assert.That(runtime.RequiresLastSurvivorObservation, Is.False);
            Assert.That(runtime.CanApplyCombatDamage, Is.False);

            // 창이 다 차기 전에는 경계가 아니다.
            Assert.That(runtime.AdvanceObservation(runtime.ObservationWindowSeconds - 0.5f), Is.False);
            Assert.That(runtime.IsSettlementBoundaryReached, Is.False);
            Assert.That(runtime.ObservationRemainingSeconds, Is.EqualTo(0.5f).Within(1e-4f));

            // 0 이하·NaN·무한대 delta 는 창을 움직이지 않는다.
            Assert.That(runtime.AdvanceObservation(0f), Is.False);
            Assert.That(runtime.AdvanceObservation(float.NaN), Is.False);
            Assert.That(runtime.AdvanceObservation(-1f), Is.False);
            Assert.That(runtime.ObservationRemainingSeconds, Is.EqualTo(0.5f).Within(1e-4f));

            Assert.That(runtime.AdvanceObservation(0.5f), Is.True, "창이 끝난 호출만 true 여야 한다.");
            Assert.That(runtime.AdvanceObservation(1f), Is.False, "창이 끝난 뒤 또 true 가 나오면 두 번 정산한다.");
            Assert.That(runtime.IsObservationComplete, Is.True);
            Assert.That(runtime.IsSettlementBoundaryReached, Is.True);

            Assert.That(runtime.TrySettle(60d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(recorder.SettleCallCount, Is.EqualTo(1), "정산은 경계에서 한 번뿐이다.");
            Assert.That(settled.TryGetResult(CombatantOf(1), out CombatantRoundResult winner), Is.True);
            Assert.That(winner.Survived, Is.True);
            Assert.That(winner.Kills, Is.EqualTo(2));
        }

        [Test]
        public void 관찰_중에_들어온_공격과_빈사는_확정된_사실을_바꾸지_못한다()
        {
            MatchRoundObservationRuntime runtime = LastSurvivorRound(out _);
            Assert.That(runtime.TryBeginLastSurvivorObservation(), Is.True);

            // 관찰 중에도 틱은 계속 돈다 — 그래도 사실은 더 받지 않는다.
            Assert.That(runtime.BeginAuthorityTick(51), Is.True);
            Assert.That(
                runtime.ObserveDamage(
                    CombatantOf(1), CombatantOf(2), WeaponType.Sword, 40f, CombatDamageSource.Weapon, 51d),
                Is.False,
                "관찰 중의 피해가 사실로 들어갔다.");
            Assert.That(
                runtime.ObserveDowned(CombatantOf(1), CombatantOf(2), CombatDamageSource.Weapon, 51.1d),
                Is.False,
                "관찰 중에 마지막 생존자가 죽어 승자가 뒤바뀌었다.");
            runtime.EndAuthorityTick();

            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(1));
            runtime.AdvanceObservation(runtime.ObservationWindowSeconds);
            Assert.That(runtime.TrySettle(52d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(settled.TryGetResult(CombatantOf(1), out CombatantRoundResult winner), Is.True);
            Assert.That(winner.Survived, Is.True, "관찰 중 입력이 확정 생존 사실을 바꿨다.");
            // 관찰 전에 실제로 넣은 30 만 남는다. 관찰 중의 40 은 사실이 아니다.
            Assert.That(winner.EnemyDamageDealt, Is.EqualTo(30f), "관찰 중 피해가 실제 적 피해에 더해졌다.");
        }

        [Test]
        public void 같은_틱_전멸은_관찰하지_않고_곧바로_정산_경계다()
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(1, (1, 1), (2, 2));

            Assert.That(runtime.BeginAuthorityTick(9), Is.True);
            runtime.ObserveDowned(CombatantOf(1), CombatantOf(1), CombatDamageSource.Zone, 9d);
            runtime.ObserveDowned(CombatantOf(2), CombatantOf(2), CombatDamageSource.Zone, 9d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.SurvivingSideCount, Is.Zero);
            Assert.That(runtime.RequiresLastSurvivorObservation, Is.False, "관찰할 대상이 없는데 창을 열었다.");
            Assert.That(runtime.TryBeginLastSurvivorObservation(), Is.False);
            Assert.That(runtime.IsSettlementBoundaryReached, Is.True);
        }

        [Test]
        public void 중간_순위표는_Host_사실만_담고_원장을_봉인하지_않는다()
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(
                null, out CallCountingRecorder recorder, 1, (1, 1), (2, 2), (3, 3));
            Assert.That(runtime.MarkForfeited(3), Is.True);

            Assert.That(runtime.BeginAuthorityTick(4), Is.True);
            runtime.ObserveDamage(
                CombatantOf(2), CombatantOf(1), WeaponType.Bow, 10f, CombatDamageSource.Weapon, 4d);
            runtime.ObserveDowned(CombatantOf(1), CombatantOf(2), CombatDamageSource.Weapon, 4.1d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.TryBuildLiveStandings(out CombatRoundLiveStanding[] live), Is.True);
            Assert.That(live.Length, Is.EqualTo(3), "명부 전원이 중간 순위표에 있어야 한다.");
            Assert.That(recorder.LiveFactsCallCount, Is.EqualTo(1));
            Assert.That(recorder.SettleCallCount, Is.Zero, "중간 순위표가 원장을 정산했다.");

            CombatRoundLiveStanding downed = live.Single(row => row.PlayerId == 1);
            CombatRoundLiveStanding killer = live.Single(row => row.PlayerId == 2);
            CombatRoundLiveStanding forfeited = live.Single(row => row.PlayerId == 3);
            Assert.That(downed.IsAlive, Is.False);
            Assert.That(killer.IsAlive, Is.True);
            Assert.That(killer.Kills, Is.EqualTo(1));
            Assert.That(killer.Assists, Is.Zero);
            Assert.That(forfeited.IsForfeited, Is.True);
            Assert.That(forfeited.IsAlive, Is.False, "이탈자를 살아 있다고 표시했다.");

            // 원장은 그대로 열려 있다 — 중간 표시는 사실을 읽기만 한다.
            Assert.That(runtime.IsSettled, Is.False);
            Assert.That(runtime.BeginAuthorityTick(5), Is.True);
            Assert.That(
                runtime.ObserveDamage(
                    CombatantOf(2), CombatantOf(3), WeaponType.Bow, 7f, CombatDamageSource.Weapon, 5d),
                Is.True,
                "중간 순위표를 만든 뒤 라운드가 닫혀 버렸다.");
            runtime.EndAuthorityTick();

            Assert.That(runtime.TryBuildLiveStandings(out CombatRoundLiveStanding[] later), Is.True);
            Assert.That(later.Single(row => row.PlayerId == 2).Kills, Is.EqualTo(1));
        }

        [Test]
        public void 해제하면_관찰_상태도_다음_라운드로_새지_않는다()
        {
            MatchRoundObservationRuntime runtime = LastSurvivorRound(out _);
            Assert.That(runtime.TryBeginLastSurvivorObservation(), Is.True);

            runtime.Disarm();
            Assert.That(runtime.IsObservingLastSurvivor, Is.False);
            Assert.That(runtime.ObservationEnteredTick, Is.Null);
            Assert.That(runtime.ObservationElapsedSeconds, Is.Zero);

            var roster = new[]
            {
                new CombatRosterMember(CombatantOf(1), 1, 1),
                new CombatRosterMember(CombatantOf(2), 2, 2),
            };
            Assert.That(runtime.Arm("match-2", 2, roster), Is.True);
            Assert.That(
                runtime.CanApplyCombatDamage, Is.True,
                "다음 라운드가 공격이 막힌 채 시작했다.");
        }

        [Test]
        public void 명부가_1명뿐인_단독_세션은_자동_종료하지_않는다()
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(1, (1, 1));

            Assert.That(runtime.BeginAuthorityTick(1), Is.True);
            runtime.EndAuthorityTick();

            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(1));
            Assert.That(
                runtime.ShouldSettle, Is.False,
                "혼자 있는 검증 세션이 시작하자마자 라운드를 끝냈다.");
        }

        // ── 봉인된 스냅샷의 재생 ──────────────────────────────────────────

        /// <summary>봉인까지 밀어붙인 한 라운드. 세 명 중 둘이 같은 틱에 빈사한다.</summary>
        private static MatchRoundObservationRuntime SettleSampleRound(
            out CallCountingRecorder recorder, out CombatRoundResultsReady settled)
        {
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(
                null, out recorder, 1, (1, 1), (2, 2), (3, 3));

            Assert.That(runtime.BeginAuthorityTick(42), Is.True);
            runtime.ObserveDamage(
                CombatantOf(1), CombatantOf(2), WeaponType.Sword, 30f, CombatDamageSource.Weapon, 42d);
            runtime.ObserveDamage(
                CombatantOf(1), CombatantOf(3), WeaponType.Bow, 20f, CombatDamageSource.Weapon, 42.1d);
            runtime.ObserveDowned(CombatantOf(2), CombatantOf(1), CombatDamageSource.Weapon, 42.2d);
            runtime.ObserveDowned(CombatantOf(3), CombatantOf(1), CombatDamageSource.Weapon, 42.3d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.ShouldSettle, Is.True);
            Assert.That(runtime.TrySettle(42.4d, out settled), Is.True);
            Assert.That(runtime.IsSettled, Is.True);
            Assert.That(recorder.SettleCallCount, Is.EqualTo(1));
            return runtime;
        }

        [Test]
        public void 하류_반영이_거절돼_다시_정산해도_기록기를_다시_부르지_않고_같은_스냅샷이_나온다()
        {
            MatchRoundObservationRuntime runtime =
                SettleSampleRound(out CallCountingRecorder recorder, out CombatRoundResultsReady first);

            // 경제 반영이 거절됐다고 치고 그대로 재시도한다 — 다른 시각으로 불러도 사실은 그대로다.
            Assert.That(
                runtime.ShouldSettle, Is.True,
                "봉인된 스냅샷이 남아 있는데 재시도 고리가 끊겼다.");
            Assert.That(runtime.TrySettle(999d, out CombatRoundResultsReady replay), Is.True);
            Assert.That(
                recorder.SettleCallCount, Is.EqualTo(1),
                "기록기를 다시 불러 사실을 다시 계산했다 (S-GD-7G8ZTA 중복 집계).");

            AssertSameFacts(runtime, first, replay);
            Assert.That(runtime.SettledTick, Is.EqualTo(first.SettledTick));
            Assert.That(runtime.TryPeekSettled(out CombatRoundResultsReady peeked), Is.True);
            AssertSameFacts(runtime, first, peeked);

            // 같은 스냅샷이니 순위도 같다.
            Assert.That(
                BuildOutcomes(runtime, 1, replay).Select(o => (o.PlayerId, o.Placement)),
                Is.EqualTo(BuildOutcomes(runtime, 1, first).Select(o => (o.PlayerId, o.Placement))));
        }

        [Test]
        public void 봉인된_스냅샷은_받아간_쪽이_배열을_건드려도_다음_재생이_달라지지_않는다()
        {
            MatchRoundObservationRuntime runtime =
                SettleSampleRound(out _, out CombatRoundResultsReady first);

            CombatantRoundResult[] copy = first.CopyResults();
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = new CombatantRoundResult(
                    copy[i].Combatant, kills: 99, assists: 99, survived: true);
            }

            Assert.That(runtime.TrySettle(50d, out CombatRoundResultsReady replay), Is.True);
            AssertSameFacts(runtime, first, replay);
        }

        [Test]
        public void 봉인_뒤에는_틱도_사실도_이탈도_더_받지_않는다()
        {
            MatchRoundObservationRuntime runtime =
                SettleSampleRound(out CallCountingRecorder recorder, out CombatRoundResultsReady first);

            int damageCalls = recorder.DamageCallCount;
            int downedCalls = recorder.DownedCallCount;
            int beginTickCalls = recorder.BeginTickCallCount;

            Assert.That(runtime.BeginAuthorityTick(43), Is.False, "봉인 뒤에 새 권위 틱이 열렸다.");
            Assert.That(runtime.EndAuthorityTick(), Is.False);
            Assert.That(
                runtime.ObserveDamage(
                    CombatantOf(1), CombatantOf(2), WeaponType.Sword, 10f,
                    CombatDamageSource.Weapon, 43d),
                Is.False,
                "봉인 뒤 늦게 도착한 피해가 집계됐다.");
            Assert.That(
                runtime.ObserveDowned(CombatantOf(1), CombatantOf(1), CombatDamageSource.Zone, 43d),
                Is.False);
            Assert.That(
                runtime.MarkForfeited(1), Is.False,
                "확정된 스냅샷과 어긋나는 이탈이 뒤늦게 들어갔다.");

            Assert.That(recorder.DamageCallCount, Is.EqualTo(damageCalls), "기록기까지 흘러갔다.");
            Assert.That(recorder.DownedCallCount, Is.EqualTo(downedCalls));
            Assert.That(recorder.BeginTickCallCount, Is.EqualTo(beginTickCalls));
            Assert.That(runtime.IsForfeited(1), Is.False);

            Assert.That(runtime.TrySettle(43d, out CombatRoundResultsReady replay), Is.True);
            AssertSameFacts(runtime, first, replay);
        }

        [Test]
        public void 해제하면_봉인된_스냅샷도_앞_라운드_명부도_남지_않는다()
        {
            MatchRoundObservationRuntime runtime = SettleSampleRound(out _, out _);

            runtime.Disarm();

            Assert.That(runtime.IsSettled, Is.False);
            Assert.That(runtime.SettledTick, Is.Null);
            Assert.That(runtime.TryPeekSettled(out _), Is.False, "해제 뒤에도 앞 라운드 결과가 남았다.");
            Assert.That(runtime.TrySettle(50d, out _), Is.False);
            Assert.That(runtime.RosterSize, Is.Zero);
            Assert.That(runtime.ShouldSettle, Is.False);
        }

        [Test]
        public void 새_매치로_다시_무장하면_앞_매치의_봉인과_사실이_새지_않는다()
        {
            MatchRoundObservationRuntime runtime = SettleSampleRound(out _, out _);
            runtime.Disarm();

            var next = new[]
            {
                new CombatRosterMember(CombatantOf(5), 5, 1),
                new CombatRosterMember(CombatantOf(6), 6, 2),
            };
            Assert.That(runtime.Arm("match-2", 2, next), Is.True);

            Assert.That(runtime.MatchId, Is.EqualTo("match-2"));
            Assert.That(runtime.Round, Is.EqualTo(2));
            Assert.That(runtime.RosterSize, Is.EqualTo(2));
            Assert.That(runtime.IsSettled, Is.False, "앞 라운드 봉인이 새 라운드로 넘어왔다.");
            Assert.That(runtime.TryPeekSettled(out _), Is.False);
            Assert.That(
                runtime.TryGetPlayerId(CombatantOf(1), out _), Is.False,
                "앞 매치 명부가 남아 있다.");
            Assert.That(
                runtime.SurvivingSideCount, Is.EqualTo(2),
                "앞 라운드 빈사가 새 라운드 생존 판정에 남았다.");
            Assert.That(runtime.IsForfeited(1), Is.False);
            Assert.That(runtime.ShouldSettle, Is.False);
        }

        // ── 진영 해석기 주입 ──────────────────────────────────────────────

        [Test]
        public void 팀_해석기를_넣으면_아군_피해는_실제_적_피해가_아니고_생존은_진영으로_센다()
        {
            TeamSideResolver teams = TeamsOf((1, TeamA), (2, TeamA), (3, TeamB), (4, TeamB));
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(
                teams, out _, 1, (1, 1), (2, 2), (3, 3), (4, 4));

            Assert.That(runtime.AreEnemies(CombatantOf(1), CombatantOf(2)), Is.False, "같은 팀이 적이 됐다.");
            Assert.That(runtime.AreEnemies(CombatantOf(1), CombatantOf(3)), Is.True);
            Assert.That(runtime.ConfiguredSideCount, Is.EqualTo(2));
            Assert.That(
                runtime.SurvivingSideCount, Is.EqualTo(2),
                "사람 수가 아니라 서로 다른 진영 수를 세야 한다.");

            Assert.That(runtime.BeginAuthorityTick(20), Is.True);
            // 아군 오사 — 실제 적 피해에 섞이면 안 된다.
            runtime.ObserveDamage(
                CombatantOf(1), CombatantOf(2), WeaponType.Sword, 35f, CombatDamageSource.Weapon, 20d);
            runtime.ObserveDamage(
                CombatantOf(1), CombatantOf(3), WeaponType.Sword, 40f, CombatDamageSource.Weapon, 20.1d);
            runtime.ObserveDowned(CombatantOf(3), CombatantOf(1), CombatDamageSource.Weapon, 20.2d);
            runtime.EndAuthorityTick();

            Assert.That(
                runtime.SurvivingSideCount, Is.EqualTo(2),
                "B 팀에 아직 P4 가 남았는데 진영이 하나로 줄었다.");
            Assert.That(
                runtime.ShouldSettle, Is.False,
                "한 진영이 통째로 지기 전에 라운드가 끝났다 (S-GD-657ZN5).");

            Assert.That(runtime.BeginAuthorityTick(21), Is.True);
            runtime.ObserveDamage(
                CombatantOf(2), CombatantOf(4), WeaponType.Bow, 40f, CombatDamageSource.Weapon, 21d);
            runtime.ObserveDowned(CombatantOf(4), CombatantOf(2), CombatDamageSource.Weapon, 21.1d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(1), "마지막 생존 진영은 A 팀 하나다.");
            Assert.That(runtime.ShouldSettle, Is.True);
            Assert.That(runtime.TrySettle(21.2d, out CombatRoundResultsReady results), Is.True);

            Assert.That(results.TryGetResult(CombatantOf(1), out CombatantRoundResult p1), Is.True);
            Assert.That(
                p1.EnemyDamageDealt, Is.EqualTo(40f),
                "아군 오사가 실제 적 피해로 집계됐다 (S-GD-7G8ZTA).");
            Assert.That(results.TryGetResult(CombatantOf(2), out CombatantRoundResult p2), Is.True);
            Assert.That(p2.EnemyDamageDealt, Is.EqualTo(40f));
            Assert.That(p1.Survived, Is.True);
            Assert.That(p2.Survived, Is.True);
        }

        [Test]
        public void 전원이_한_진영이면_생존_진영이_하나여도_자동_종료하지_않는다()
        {
            TeamSideResolver oneTeam = TeamsOf((1, TeamA), (2, TeamA));
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(
                oneTeam, out _, 1, (1, 1), (2, 2));

            Assert.That(runtime.ConfiguredSideCount, Is.EqualTo(1));

            Assert.That(runtime.BeginAuthorityTick(9), Is.True);
            runtime.ObserveDowned(CombatantOf(2), CombatantOf(2), CombatDamageSource.Zone, 9d);
            runtime.EndAuthorityTick();

            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(1));
            Assert.That(
                runtime.ShouldSettle, Is.False,
                "대립 진영이 없는 단독 검증 세션이 스스로 라운드를 끝냈다.");
        }

        [Test]
        public void 팀원_하나가_이탈해도_남은_팀원이_있으면_그_진영은_살아_있다()
        {
            TeamSideResolver teams = TeamsOf((1, TeamA), (2, TeamA), (3, TeamB), (4, TeamB));
            MatchRoundObservationRuntime runtime = CreateArmedRuntime(
                teams, out _, 1, (1, 1), (2, 2), (3, 3), (4, 4));

            Assert.That(runtime.MarkForfeited(3), Is.True);
            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(2), "P4 가 남은 B 팀이 사라졌다.");
            Assert.That(
                runtime.ConfiguredSideCount, Is.EqualTo(2),
                "이탈로 시작 진영 수가 줄었다 (S-GD-QJA6NY 위반).");
            Assert.That(runtime.RosterSize, Is.EqualTo(4), "이탈로 시작 인원 N 이 줄었다.");

            Assert.That(runtime.MarkForfeited(4), Is.True);
            Assert.That(runtime.SurvivingSideCount, Is.EqualTo(1), "B 팀 전원이 빠졌는데 진영이 남았다.");
            Assert.That(runtime.ConfiguredSideCount, Is.EqualTo(2));
        }

        // ── 명부 검증 ─────────────────────────────────────────────────────

        [Test]
        public void 기본값_명부_한_줄이_섞이면_무장하지_않는다()
        {
            MatchRoundObservationRuntime runtime = null;
            var ledger = new CombatRoundFactLedger(
                AssistWindowSeconds,
                (attacker, target) => runtime != null && runtime.AreEnemies(attacker, target));
            runtime = new MatchRoundObservationRuntime(ledger);

            Assert.That(
                default(CombatRosterMember).IsValid, Is.False,
                "기본값 명부 줄이 생성자 검증을 건너뛰고 확정값 행세를 한다.");

            var roster = new[]
            {
                new CombatRosterMember(CombatantOf(1), 1, 1),
                default(CombatRosterMember),
            };

            Assert.That(
                runtime.Arm("match-1", 1, roster), Is.False,
                "입장 순서 0 인 기본값 줄로 무장했다 (S-GD-7G8ZTA 위반).");
            Assert.That(runtime.IsArmed, Is.False);
            Assert.That(runtime.RosterSize, Is.Zero, "실패한 무장이 반쪽 명부를 남겼다.");
            Assert.That(runtime.ShouldSettle, Is.False);
        }

        // ── 경제 반영 경계 ────────────────────────────────────────────────

        /// <summary>등급마다 후보를 채울 수 있는 최소 축복 풀. 다른 경제 테스트와 같은 방식이다.</summary>
        private static IReadOnlyList<BlessingDefinition> BlessingPool()
        {
            var pool = new List<BlessingDefinition>();
            foreach (BlessingTier tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (var i = 0; i < 6; i++)
                {
                    pool.Add(new BlessingDefinition($"{tier}-{i}", $"{tier} {i}", WeaponTag.Common, tier));
                }
            }

            return pool;
        }

        private static MatchEconomyState CreateEconomy(params int[] playerIds)
        {
            var economy = new MatchEconomyState(
                EconomyConfig.Default, BlessingConfig.Default, BlessingPool());
            economy.BeginMatch(
                playerIds.Select(id => new MatchParticipant(id, WeaponTag.Sword)).ToArray(), seed: 42);
            economy.BeginPreparation();
            economy.EndPreparation();
            return economy;
        }

        [Test]
        public void 같은_라운드_결과가_다시_와도_집계와_초기화가_반복되지_않는다()
        {
            MatchEconomyState economy = CreateEconomy(1, 2);
            var outcomes = new[]
            {
                new RoundOutcome(1, kills: 2, assists: 0, rangedAssists: 0, survived: true, placement: 1),
                new RoundOutcome(2, kills: 0, assists: 1, rangedAssists: 0, survived: false, placement: 2),
            };

            Assert.That(economy.TryApplyRoundResults(1, outcomes), Is.True);
            Assert.That(economy.TryApplyRoundResults(1, outcomes), Is.False, "재전송이 두 번 집계됐다.");

            Assert.That(economy.Tally.GetEntry(1).Kills, Is.EqualTo(2));
            Assert.That(economy.Tally.GetEntry(1).CountedRounds, Is.EqualTo(1));
            Assert.That(economy.Tally.CountedRounds.Count, Is.EqualTo(1));
        }

        [Test]
        public void 최종_순위는_실제_입장_순서로_동점을_가른다()
        {
            MatchEconomyState economy = CreateEconomy(5, 6);

            // 점수·킬·어시·골드가 모두 같게 만든다 — 9단계 마지막(입장 순서)까지 내려간다.
            var outcomes = new[]
            {
                new RoundOutcome(5, 1, 0, 0, survived: true, placement: 1),
                new RoundOutcome(6, 1, 0, 0, survived: true, placement: 1),
            };
            Assert.That(economy.TryApplyRoundResults(1, outcomes), Is.True);

            FinalStanding[] byJoinOrder = economy.BuildFinalStandings(
                null, new Dictionary<int, int> { { 5, 2 }, { 6, 1 } });

            Assert.That(byJoinOrder[0].PlayerId, Is.EqualTo(6), "먼저 입장한 사람이 앞서야 한다.");
            Assert.That(byJoinOrder[0].Rank, Is.EqualTo(1));
            Assert.That(byJoinOrder[1].Rank, Is.EqualTo(2));
            Assert.That(byJoinOrder.Select(s => s.Rank), Is.Unique);
        }

        [Test]
        public void 같은_인스턴스로_재대결하면_앞_매치_집계가_남지_않는다()
        {
            MatchEconomyState economy = CreateEconomy(1, 2);
            Assert.That(
                economy.TryApplyRoundResults(
                    1,
                    new[]
                    {
                        new RoundOutcome(1, 3, 0, 0, survived: true, placement: 1),
                        new RoundOutcome(2, 0, 0, 0, survived: false, placement: 2),
                    }),
                Is.True);

            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(1, WeaponTag.Sword), new MatchParticipant(2, WeaponTag.Sword),
                },
                seed: 7);

            Assert.That(economy.Tally.CountedRounds, Is.Empty);
            Assert.That(economy.Tally.GetEntry(1).Kills, Is.Zero);
        }

        [Test]
        public void 정산_계획이_실패하면_결과를_만들지_않는다()
        {
            var inputs = new[]
            {
                // 탈락자인데 권위 틱이 없다 — 시점을 모른 채 순위를 지어내지 않는다.
                new RoundSettlementInput(1, 1, survived: false, eliminationTick: null,
                    kills: 0, assists: 0, rangedAssists: 0, enemyDamageDealt: 0f),
                new RoundSettlementInput(2, 2, survived: true, eliminationTick: null,
                    kills: 0, assists: 0, rangedAssists: 0, enemyDamageDealt: 0f),
            };

            Assert.That(
                RoundSettlementPlanner.TryBuild(
                    1, inputs, 2, out RoundOutcome[] outcomes,
                    out RoundSettlementFailure failure, out RoundPlacementFailure placementFailure),
                Is.False);
            Assert.That(outcomes, Is.Empty);
            Assert.That(failure, Is.EqualTo(RoundSettlementFailure.PlacementUnresolved));
            Assert.That(placementFailure, Is.EqualTo(RoundPlacementFailure.InvalidEliminationTick));
        }

        [Test]
        public void 시작_인원보다_적은_사실로는_순위를_만들지_않는다()
        {
            var inputs = new[]
            {
                new RoundSettlementInput(1, 1, survived: true, eliminationTick: null,
                    kills: 0, assists: 0, rangedAssists: 0, enemyDamageDealt: 0f),
            };

            Assert.That(
                RoundSettlementPlanner.TryBuild(
                    1, inputs, rosterSize: 3, out _, out RoundSettlementFailure failure, out _),
                Is.False);
            Assert.That(failure, Is.EqualTo(RoundSettlementFailure.PlacementUnresolved));
        }

        // ── 완결 라운드 커밋 경계 (MatchRoundTally.TryApplyCompleteRound) ──

        /// <summary>세 명 명부의 완결된 한 라운드. 순위는 1..3 유일이다.</summary>
        private static RoundOutcome[] CompleteBatch() => new[]
        {
            new RoundOutcome(1, kills: 2, assists: 1, rangedAssists: 1, survived: true, placement: 1),
            new RoundOutcome(2, kills: 1, assists: 0, rangedAssists: 0, survived: false, placement: 2),
            new RoundOutcome(3, kills: 0, assists: 2, rangedAssists: 0, survived: false, placement: 3,
                forfeited: true),
        };

        private static readonly int[] CompleteRoster = { 1, 2, 3 };

        /// <summary>커밋된 라운드 하나가 그대로 남아 있는가 — 거절이 상태를 건드리지 않았는지 본다.</summary>
        private static void AssertFirstCommitIntact(MatchRoundTally tally)
        {
            Assert.That(tally.CountedRounds, Is.EquivalentTo(new[] { 1 }), "반영된 라운드 목록이 변했다.");
            Assert.That(tally.PlayerCount, Is.EqualTo(3), "집계에 없던 사람이 생겼거나 사라졌다.");

            MatchTallyEntry first = tally.GetEntry(1);
            Assert.That(first.Kills, Is.EqualTo(2), "킬이 두 번 더해졌다.");
            Assert.That(first.Assists, Is.EqualTo(1));
            Assert.That(first.RangedAssists, Is.EqualTo(1));
            Assert.That(first.SurvivedRounds, Is.EqualTo(1));
            Assert.That(first.CountedRounds, Is.EqualTo(1));
            Assert.That(first.Placements.Select(p => (p.Round, p.Placement)), Is.EqualTo(new[] { (1, 1) }));

            MatchTallyEntry third = tally.GetEntry(3);
            Assert.That(third.Assists, Is.EqualTo(2));
            Assert.That(third.CountedRounds, Is.EqualTo(1));
            Assert.That(
                third.Placements.Single().ScoredAsLastPlace, Is.True,
                "이탈 배점 표시가 사라졌다 (S-GD-QJA6NY).");
        }

        [Test]
        public void 완결_배치는_한_번_커밋되고_같은_사실의_재전송은_두_번_집계되지_않는다()
        {
            var tally = new MatchRoundTally();

            Assert.That(
                tally.TryApplyCompleteRound(1, CompleteBatch(), CompleteRoster), Is.True,
                "명부 전원·유일 순위를 갖춘 완결 배치가 거절됐다.");
            AssertFirstCommitIntact(tally);

            // 줄 순서를 뒤집고 명부 순서도 바꿔 다시 보낸다 — 같은 사실이니 통과하되 더하지는 않는다.
            RoundOutcome[] reordered = CompleteBatch().Reverse().ToArray();
            Assert.That(
                tally.TryApplyCompleteRound(1, reordered, new[] { 3, 1, 2 }), Is.True,
                "순서만 다른 재전송이 거절됐다 (S-GD-JRKHQA 완결 발행은 한 번).");

            AssertFirstCommitIntact(tally);
        }

        [Test]
        public void 명부와_어긋난_배치는_커밋되지_않고_누적을_건드리지_않는다()
        {
            var tally = new MatchRoundTally();

            // 한 명이 빠졌다 — 시작 인원 N 이 3인데 두 줄뿐이다.
            RoundOutcome[] missing = CompleteBatch().Where(o => o.PlayerId != 3).ToArray();
            Assert.That(tally.TryApplyCompleteRound(1, missing, CompleteRoster), Is.False);

            // 명부 밖 사람이 끼었다 (줄 수는 맞다).
            var intruder = new[]
            {
                new RoundOutcome(1, 0, 0, 0, survived: true, placement: 1),
                new RoundOutcome(2, 0, 0, 0, survived: false, placement: 2),
                new RoundOutcome(99, 0, 0, 0, survived: false, placement: 3),
            };
            Assert.That(tally.TryApplyCompleteRound(1, intruder, CompleteRoster), Is.False);

            // 같은 사람이 두 줄이다.
            var duplicated = new[]
            {
                new RoundOutcome(1, 0, 0, 0, survived: true, placement: 1),
                new RoundOutcome(1, 0, 0, 0, survived: false, placement: 2),
                new RoundOutcome(2, 0, 0, 0, survived: false, placement: 3),
            };
            Assert.That(tally.TryApplyCompleteRound(1, duplicated, CompleteRoster), Is.False);

            // 명부 자체에 같은 사람이 두 번 있으면 N 이 몇인지부터 말이 안 된다.
            Assert.That(
                tally.TryApplyCompleteRound(1, CompleteBatch(), new[] { 1, 2, 3, 3 }), Is.False,
                "중복된 명부로 커밋됐다.");

            Assert.That(tally.CountedRounds, Is.Empty, "거절된 배치가 라운드를 반영으로 남겼다.");
            Assert.That(tally.PlayerCount, Is.Zero, "거절된 배치가 누적을 만들었다.");
            Assert.That(tally.GetEntry(1).CountedRounds, Is.Zero);
        }

        [Test]
        public void 순위가_유일하지_않거나_범위를_벗어난_배치는_커밋되지_않는다()
        {
            var tally = new MatchRoundTally();

            var tied = new[]
            {
                new RoundOutcome(1, 0, 0, 0, survived: true, placement: 1),
                new RoundOutcome(2, 0, 0, 0, survived: false, placement: 2),
                new RoundOutcome(3, 0, 0, 0, survived: false, placement: 2),
            };
            Assert.That(
                tally.TryApplyCompleteRound(1, tied, CompleteRoster), Is.False,
                "공동 순위가 커밋됐다 (S-GD-7G8ZTA 위반).");

            var outOfRange = new[]
            {
                new RoundOutcome(1, 0, 0, 0, survived: true, placement: 1),
                new RoundOutcome(2, 0, 0, 0, survived: false, placement: 2),
                new RoundOutcome(3, 0, 0, 0, survived: false, placement: 4),
            };
            Assert.That(
                tally.TryApplyCompleteRound(1, outOfRange, CompleteRoster), Is.False,
                "1..N 밖 순위가 커밋됐다.");

            var unknownPlacement = new[]
            {
                new RoundOutcome(1, 0, 0, 0, survived: true, placement: 1),
                new RoundOutcome(2, 0, 0, 0, survived: false, placement: 2),
                new RoundOutcome(3, 0, 0, 0, survived: false, placement: RoundOutcome.UnknownPlacement),
            };
            Assert.That(
                tally.TryApplyCompleteRound(1, unknownPlacement, CompleteRoster), Is.False,
                "순위를 모르는 줄이 완결 배치로 통과했다.");

            Assert.That(tally.CountedRounds, Is.Empty);
            Assert.That(tally.PlayerCount, Is.Zero);
        }

        [Test]
        public void 빈_명부나_기본값_사실로는_커밋하지_않는다()
        {
            var tally = new MatchRoundTally();

            Assert.That(
                tally.TryApplyCompleteRound(1, Array.Empty<RoundOutcome>(), Array.Empty<int>()), Is.False,
                "빈 명부로 라운드를 완결 처리했다.");
            Assert.That(
                tally.TryApplyCompleteRound(1, CompleteBatch(), Array.Empty<int>()), Is.False);
            Assert.That(
                tally.TryApplyCompleteRound(1, Array.Empty<RoundOutcome>(), CompleteRoster), Is.False);

            // 기본값 구조체는 생성자 검증을 거치지 않은 줄이다 — PlayerId 0, 순위 0.
            var defaults = new[] { default(RoundOutcome), default(RoundOutcome), default(RoundOutcome) };
            Assert.That(
                tally.TryApplyCompleteRound(1, defaults, CompleteRoster), Is.False,
                "기본값 줄이 확정된 사실 행세를 했다.");
            Assert.That(
                tally.TryApplyCompleteRound(1, defaults, new[] { 0 }), Is.False);

            Assert.That(tally.TryApplyCompleteRound(1, null, CompleteRoster), Is.False);
            Assert.That(tally.TryApplyCompleteRound(1, CompleteBatch(), null), Is.False);
            Assert.That(tally.TryApplyCompleteRound(0, CompleteBatch(), CompleteRoster), Is.False);

            Assert.That(tally.CountedRounds, Is.Empty);
            Assert.That(tally.PlayerCount, Is.Zero);
        }

        [Test]
        public void 한_필드라도_다른_재전송은_거절되고_앞_커밋이_그대로_남는다()
        {
            var tally = new MatchRoundTally();
            Assert.That(tally.TryApplyCompleteRound(1, CompleteBatch(), CompleteRoster), Is.True);

            // 킬만 다르다.
            RoundOutcome[] moreKills = CompleteBatch();
            moreKills[0] = new RoundOutcome(1, kills: 3, assists: 1, rangedAssists: 1, survived: true,
                placement: 1);
            Assert.That(tally.TryApplyCompleteRound(1, moreKills, CompleteRoster), Is.False);

            // 순위만 서로 바뀌었다.
            RoundOutcome[] swapped = CompleteBatch();
            swapped[1] = new RoundOutcome(2, 1, 0, 0, survived: false, placement: 3);
            swapped[2] = new RoundOutcome(3, 0, 2, 0, survived: false, placement: 2, forfeited: true);
            Assert.That(tally.TryApplyCompleteRound(1, swapped, CompleteRoster), Is.False);

            // 이탈 표시만 사라졌다 — 표시 순위는 같아도 배점 정책이 다른 사실이다 (S-GD-QJA6NY).
            RoundOutcome[] withoutForfeit = CompleteBatch();
            withoutForfeit[2] = new RoundOutcome(3, 0, 2, 0, survived: false, placement: 3);
            Assert.That(
                tally.TryApplyCompleteRound(1, withoutForfeit, CompleteRoster), Is.False,
                "이탈 표시가 다른 배치가 같은 사실로 통과했다.");

            // 생존 표시만 다르다.
            RoundOutcome[] survivedDiff = CompleteBatch();
            survivedDiff[1] = new RoundOutcome(2, 1, 0, 0, survived: true, placement: 2);
            Assert.That(tally.TryApplyCompleteRound(1, survivedDiff, CompleteRoster), Is.False);

            // 원거리 어시스트 분류만 다르다.
            RoundOutcome[] rangedDiff = CompleteBatch();
            rangedDiff[0] = new RoundOutcome(1, 2, 1, rangedAssists: 0, survived: true, placement: 1);
            Assert.That(tally.TryApplyCompleteRound(1, rangedDiff, CompleteRoster), Is.False);

            AssertFirstCommitIntact(tally);
        }

        [Test]
        public void 커밋한_뒤_호출자가_배열을_고쳐도_재전송_판정은_달라지지_않는다()
        {
            var tally = new MatchRoundTally();
            RoundOutcome[] batch = CompleteBatch();
            Assert.That(tally.TryApplyCompleteRound(1, batch, CompleteRoster), Is.True);

            // 넘긴 배열을 그대로 들고 있다가 고친다 — 내부 영수증이 같은 배열이면 여기서 오염된다.
            batch[0] = new RoundOutcome(1, kills: 99, assists: 99, rangedAssists: 99, survived: false,
                placement: 3);
            batch[2] = new RoundOutcome(3, kills: 99, assists: 0, rangedAssists: 0, survived: true,
                placement: 1);

            Assert.That(
                tally.TryApplyCompleteRound(1, batch, CompleteRoster), Is.False,
                "호출자가 고친 배열이 원래 커밋과 같은 사실로 취급됐다.");
            Assert.That(
                tally.TryApplyCompleteRound(1, CompleteBatch(), CompleteRoster), Is.True,
                "처음 커밋한 사실이 호출자의 배열 수정으로 오염됐다.");

            AssertFirstCommitIntact(tally);
        }

        [Test]
        public void 옛_경로로_반영된_라운드는_완결_커밋으로_승인되지_않는다()
        {
            var tally = new MatchRoundTally();

            // 옛 경계는 순위를 모르는 반쪽 배치도 그대로 더한다 — 그 계약은 그대로 둔다.
            Assert.That(
                tally.TryApplyRound(1, new[] { new RoundOutcome(1, 2, 1, false) }), Is.True);
            Assert.That(tally.TryApplyRound(1, new[] { new RoundOutcome(1, 2, 1, false) }), Is.False);

            Assert.That(
                tally.TryApplyCompleteRound(1, CompleteBatch(), CompleteRoster), Is.False,
                "무엇이 반영됐는지 모르는 라운드를 완결 커밋이 승인했다.");

            // 거절이므로 옛 경로의 누적만 남아 있다.
            Assert.That(tally.PlayerCount, Is.EqualTo(1));
            Assert.That(tally.GetEntry(1).Kills, Is.EqualTo(2));
            Assert.That(tally.GetEntry(1).CountedRounds, Is.EqualTo(1));
            Assert.That(tally.GetEntry(1).Placements, Is.Empty);
            Assert.That(tally.GetEntry(3).CountedRounds, Is.Zero);
            Assert.That(tally.CountedRounds, Is.EquivalentTo(new[] { 1 }));

            // 반대 방향도 같다 — 완결 커밋된 라운드를 옛 경로가 다시 더하지 않는다.
            Assert.That(tally.TryApplyCompleteRound(2, CompleteBatch(), CompleteRoster), Is.True);
            Assert.That(
                tally.TryApplyRound(2, CompleteBatch()), Is.False,
                "완결 커밋된 라운드가 옛 경로로 한 번 더 집계됐다.");
            Assert.That(tally.GetEntry(2).Kills, Is.EqualTo(1));
            Assert.That(tally.GetEntry(2).CountedRounds, Is.EqualTo(1));
        }

        [Test]
        public void 새_매치_초기화는_완결_커밋_증거까지_지운다()
        {
            var tally = new MatchRoundTally();
            Assert.That(tally.TryApplyCompleteRound(1, CompleteBatch(), CompleteRoster), Is.True);

            tally.ResetForNewMatch();

            Assert.That(tally.CountedRounds, Is.Empty);
            Assert.That(tally.PlayerCount, Is.Zero);
            Assert.That(tally.GetEntry(1).CountedRounds, Is.Zero);

            // 앞 매치와 사실이 다른 1라운드가 새 매치에서 정상 커밋돼야 한다.
            var next = new[]
            {
                new RoundOutcome(1, kills: 0, assists: 0, rangedAssists: 0, survived: false, placement: 3),
                new RoundOutcome(2, kills: 5, assists: 0, rangedAssists: 0, survived: true, placement: 1),
                new RoundOutcome(3, kills: 1, assists: 0, rangedAssists: 0, survived: false, placement: 2),
            };
            Assert.That(
                tally.TryApplyCompleteRound(1, next, CompleteRoster), Is.True,
                "앞 매치의 영수증이 남아 새 매치의 라운드를 거절했다.");
            Assert.That(tally.GetEntry(2).Kills, Is.EqualTo(5));
            Assert.That(tally.GetEntry(1).Kills, Is.Zero, "앞 매치 누적이 남았다.");

            // 새 매치의 재전송 판정도 새 사실을 기준으로 돈다.
            Assert.That(tally.TryApplyCompleteRound(1, next.Reverse().ToArray(), CompleteRoster), Is.True);
            Assert.That(tally.GetEntry(2).CountedRounds, Is.EqualTo(1));
            Assert.That(
                tally.TryApplyCompleteRound(1, CompleteBatch(), CompleteRoster), Is.False,
                "앞 매치의 사실이 새 매치의 같은 라운드로 통과했다.");
        }
        [Test]
        public void 제품_경제는_불완전_결과를_거절한_뒤_동일_결과만_재시도한다()
        {
            MatchEconomyState economy = CreateEconomy(1, 2, 3);
            Assert.That(economy.TryApplyCompleteRoundResults(1, CompleteBatch().Take(2).ToArray()), Is.False);
            Assert.That(economy.Tally.CountedRounds, Is.Empty);
            Assert.That(economy.Phase, Is.EqualTo(MatchEconomyPhase.Combat));
            Assert.That(economy.TryApplyCompleteRoundResults(1, CompleteBatch()), Is.True);
            Assert.That(economy.TryApplyCompleteRoundResults(1, CompleteBatch().Reverse().ToArray()), Is.True);
            RoundOutcome[] conflict = CompleteBatch();
            conflict[0] = new RoundOutcome(1, 99, 1, 1, true, 1);
            Assert.That(economy.TryApplyCompleteRoundResults(1, conflict), Is.False);
            AssertFirstCommitIntact(economy.Tally);
            Assert.That(economy.TryApplyCompleteRoundResults(2, CompleteBatch()), Is.False);
        }

        /// <summary>
        /// <b>강화 초기화 경계는 "다음 준비 시작 전" 이다</b>
        /// (S-GD-75ZAS9 · S-GD-A2YDDJ, Jira S15P21A606-140).
        ///
        /// <para>
        /// <b>정산에서는 되돌리지 않고, 다음 준비를 열 때 되돌린다.</b> 사용자가 2026-09-14 에
        /// *"끝나면 = 다음 라운드 준비단계의 시작 전(증강 나오기 전)"* 으로 확정했다.
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b>양쪽을 함께 단정한다.</b> 정산 시점에 초기화를 넣으면 강화로 늘어난 최대 체력이
        /// 요약 화면이 뜨는 순간 깎여, 방금 끝난 전투의 몸을 보는 자리에서 체력 막대가 떨어진다
        /// (<c>ApplyMaxHealthChange</c> 가 현재 체력을 새 최대치로 자른다). 실제로 이 티켓에서
        /// 정산 초기화를 한 번 넣었다가 그 부작용 때문에 되돌렸다 — <b>다시 넣으면 여기서 잡힌다.</b>
        /// </para>
        ///
        /// <para>
        /// 장착 무기는 초기화를 넘어 <b>유지</b>된다. <c>ResetForNextRound</c> 은 단계만 0 으로 하고
        /// <c>EquippedWeapon</c> 은 건드리지 않는다 — 지우면 다음 라운드에 강화 표를 고를 수 없다.
        /// </para>
        /// </summary>
        [Test]
        public void 강화_초기화는_정산이_아니라_다음_준비_시작에서_일어난다()
        {
            MatchEconomyState economy = CreateEconomy(1, 2);
            WeaponType equipped = economy.GetEquippedWeaponType(1);

            // ⚠️ TryUpgradeSlot 은 단계도 위치도 보지 않는다(게이트의 몫). 여기서 보는 것은
            //    초기화 경계뿐이므로 게이트를 흉내내지 않고 도메인을 직접 올린다.
            economy.Earn(1, 10_000);
            Assert.That(
                economy.TryUpgradeSlot(1, UpgradeSlot.Weapon), Is.EqualTo(UpgradeAttemptResult.Success));
            Assert.That(
                economy.TryUpgradeSlot(1, UpgradeSlot.Armor), Is.EqualTo(UpgradeAttemptResult.Success));
            Assert.That(economy.GetUpgradeSetLevel(1), Is.EqualTo(1), "세트 단계가 안 올랐다 — 전제가 깨졌다.");

            var outcomes = new[]
            {
                new RoundOutcome(1, kills: 0, assists: 0, rangedAssists: 0, survived: false, placement: 2),
                new RoundOutcome(2, kills: 1, assists: 0, rangedAssists: 0, survived: true, placement: 1),
            };
            Assert.That(economy.TryApplyCompleteRoundResults(1, outcomes), Is.True);

            // ① 정산은 강화를 건드리지 않는다 — 요약 화면이 지난 라운드의 몸을 그대로 보여준다.
            Assert.That(
                economy.GetUpgradeLevel(1, UpgradeSlot.Weapon), Is.EqualTo(1),
                "정산이 강화를 되돌렸다. 최대 체력이 요약 화면에서 깎여 체력 막대가 떨어진다 (S-GD-75ZAS9).");
            Assert.That(economy.GetUpgradeLevel(1, UpgradeSlot.Armor), Is.EqualTo(1));

            // ② 다음 준비를 열 때 0강이 된다 — 증강 후보를 고르는 판단은 0강 기준이다.
            Assert.That(economy.BeginPreparation(), Is.EqualTo(2), "다음 라운드를 열지 못했다 — 전제가 깨졌다.");

            Assert.That(
                economy.GetUpgradeLevel(1, UpgradeSlot.Weapon), Is.Zero,
                "다음 준비가 열렸는데 무기 강화가 남았다 (S-GD-A2YDDJ).");
            Assert.That(
                economy.GetUpgradeLevel(1, UpgradeSlot.Armor), Is.Zero,
                "다음 준비가 열렸는데 방어구 강화가 남았다.");
            Assert.That(economy.GetUpgradeSetLevel(1), Is.Zero, "세트 단계가 남았다.");
            Assert.That(
                economy.GetEquippedWeaponType(1), Is.EqualTo(equipped),
                "초기화가 장착 무기까지 지웠다 — 다음 라운드에 강화 표를 고를 수 없다.");
        }

    }
}
