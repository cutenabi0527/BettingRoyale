using System;
using BettingRoyale.Data.Online;
using BettingRoyale.Data.Scenes;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public class OnlineCoreLoopStateTests
    {
        [Test]
        public void DefaultRules_UseConfirmedThreeRoundNinetySecondContract()
        {
            OnlineCoreLoopRules rules = OnlineCoreLoopRules.Default;

            Assert.That(rules.TotalRounds, Is.EqualTo(3));
            Assert.That(rules.PreparationSeconds, Is.EqualTo(90));
            Assert.That(rules.BlessingSelectionSeconds, Is.EqualTo(30));
            Assert.That(rules.CombatSeconds, Is.EqualTo(90));
            Assert.That(rules.SummarySeconds, Is.EqualTo(10));
        }

        [Test]
        public void QuickPreset_OnlyShortensManualPlaytestDurations()
        {
            OnlineCoreLoopRules rules = OnlineCoreLoopRules.QuickPlaytest;

            Assert.That(rules.TotalRounds, Is.EqualTo(3));
            Assert.That(rules.PreparationSeconds, Is.EqualTo(10));
            Assert.That(rules.BlessingSelectionSeconds, Is.EqualTo(3));
            Assert.That(rules.CombatSeconds, Is.EqualTo(30));
            Assert.That(rules.SummarySeconds, Is.EqualTo(5));
        }

        [Test]
        public void Rules_ClampLobbyAdjustmentsAndRejectInvalidNetworkValues()
        {
            // ⚠️ WithCombatSeconds 는 S-GD-657ZN5 로 폐기 예정이지만 **클램프 계약은 그대로**다.
            //    호출자가 남아 있는 동안 회귀를 계속 잡는다.
#pragma warning disable CS0618
            OnlineCoreLoopRules rules = OnlineCoreLoopRules.Default
                .WithRounds(99)
                .WithPreparationSeconds(-10)
                .WithCombatSeconds(999);
#pragma warning restore CS0618

            Assert.That(rules.TotalRounds, Is.EqualTo(OnlineCoreLoopRules.MaxRounds));
            Assert.That(rules.PreparationSeconds, Is.EqualTo(OnlineCoreLoopRules.MinPhaseSeconds));
            Assert.That(rules.CombatSeconds, Is.EqualTo(OnlineCoreLoopRules.MaxPhaseSeconds));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OnlineCoreLoopRules(0, 90, 90, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OnlineCoreLoopRules(3, 0, 90, 10));
        }

        [TestCase(OnlineCoreLoopPhase.Lobby, false, false, false)]
        [TestCase(OnlineCoreLoopPhase.Loading, false, false, false)]
        [TestCase(OnlineCoreLoopPhase.Preparation, true, true, false)]
        [TestCase(OnlineCoreLoopPhase.Combat, true, true, true)]
        [TestCase(OnlineCoreLoopPhase.Summary, false, false, false)]
        [TestCase(OnlineCoreLoopPhase.Finished, false, false, false)]
        public void Snapshot_ProjectsPhaseIntoRuntimeGates(
            OnlineCoreLoopPhase phase,
            bool canMove,
            bool canUseCombatActions,
            bool canReceiveDamage)
        {
            var snapshot = new OnlineCoreLoopSnapshot(
                OnlineCoreLoopRules.Default,
                phase,
                currentRound: phase == OnlineCoreLoopPhase.Lobby ? 0 : 1,
                remainingSeconds: 15f,
                Array.Empty<OnlineCoreLoopStanding>());

            Assert.That(snapshot.CanMove, Is.EqualTo(canMove));
            Assert.That(snapshot.CanUseCombatActions, Is.EqualTo(canUseCombatActions));
            Assert.That(snapshot.CanReceiveCombatDamage, Is.EqualTo(canReceiveDamage));
            Assert.That(snapshot.PreparationMapKey, Is.EqualTo(MatchSceneCatalog.DefaultPreparation));
            Assert.That(snapshot.CombatMapKey, Is.EqualTo(MatchSceneCatalog.DefaultCombat));
        }

        [Test]
        public void Snapshot_PreservesHostSelectedMapKeysAndNormalizesUnknownPhases()
        {
            var selected = new OnlineCoreLoopSnapshot(
                OnlineCoreLoopRules.Default,
                OnlineCoreLoopPhase.Loading,
                1,
                0f,
                Array.Empty<OnlineCoreLoopStanding>(),
                "match",
                MatchSceneCatalog.DefaultPreparation01,
                MatchSceneCatalog.DefaultCombat01);

            Assert.That(selected.PreparationMapKey, Is.EqualTo(MatchSceneCatalog.DefaultPreparation01));
            Assert.That(selected.CombatMapKey, Is.EqualTo(MatchSceneCatalog.DefaultCombat01));

            var normalized = new OnlineCoreLoopSnapshot(
                OnlineCoreLoopRules.Default,
                OnlineCoreLoopPhase.Loading,
                1,
                0f,
                Array.Empty<OnlineCoreLoopStanding>(),
                "match",
                MatchSceneCatalog.DefaultCombat01,
                MatchSceneCatalog.DefaultPreparation01);
            Assert.That(normalized.PreparationMapKey, Is.EqualTo(MatchSceneCatalog.DefaultPreparation));
            Assert.That(normalized.CombatMapKey, Is.EqualTo(MatchSceneCatalog.DefaultCombat));
        }

        [Test]
        public void PreparationSnapshot_ExpiresBlessingSelectionBeforePreparationEnds()
        {
            OnlineCoreLoopRules rules = OnlineCoreLoopRules.Default;
            var selectionStarted = new OnlineCoreLoopSnapshot(
                rules,
                OnlineCoreLoopPhase.Preparation,
                currentRound: 1,
                remainingSeconds: 90f,
                Array.Empty<OnlineCoreLoopStanding>());
            var selectionExpired = new OnlineCoreLoopSnapshot(
                rules,
                OnlineCoreLoopPhase.Preparation,
                currentRound: 1,
                remainingSeconds: 60f,
                Array.Empty<OnlineCoreLoopStanding>());

            Assert.That(selectionStarted.BlessingSelectionRemainingSeconds, Is.EqualTo(30f));
            Assert.That(selectionExpired.BlessingSelectionRemainingSeconds, Is.Zero);
            Assert.That(selectionExpired.RemainingSeconds, Is.EqualTo(60f));
        }

        [Test]
        public void ObservationSnapshot_KeepsMovementAndLocksCombatWithoutLeavingCombatPhase()
        {
            var observing = new OnlineCoreLoopSnapshot(
                OnlineCoreLoopRules.Default,
                OnlineCoreLoopPhase.Combat,
                currentRound: 2,
                remainingSeconds: 12f,
                Array.Empty<OnlineCoreLoopStanding>(),
                "match",
                MatchSceneCatalog.DefaultPreparation,
                MatchSceneCatalog.DefaultCombat,
                isObservingLastSurvivor: true);

            // 단계는 그대로 Combat 이다 — 요약으로 넘기지도, 전역 정지를 걸지도 않는다.
            Assert.That(observing.Phase, Is.EqualTo(OnlineCoreLoopPhase.Combat));
            Assert.That(observing.IsObservingLastSurvivor, Is.True);
            Assert.That(observing.CanMove, Is.True, "관찰 중에 이동이 막히면 관전이 불가능하다.");
            Assert.That(observing.CanUseCombatActions, Is.False);
            Assert.That(observing.CanReceiveCombatDamage, Is.False);
        }

        [Test]
        public void ObservationFlag_DoesNotLeakOutsideCombat()
        {
            var preparation = new OnlineCoreLoopSnapshot(
                OnlineCoreLoopRules.Default,
                OnlineCoreLoopPhase.Preparation,
                1,
                90f,
                Array.Empty<OnlineCoreLoopStanding>(),
                "match",
                MatchSceneCatalog.DefaultPreparation,
                MatchSceneCatalog.DefaultCombat,
                isObservingLastSurvivor: true);

            Assert.That(preparation.IsObservingLastSurvivor, Is.False);
            Assert.That(
                preparation.CanUseCombatActions, Is.True,
                "관찰 플래그가 준비 단계의 공격까지 막았다.");
        }

        [Test]
        public void LiveRoundFacts_CarryHostFactsOnlyAndStayUnranked()
        {
            var standings = new[]
            {
                new OnlineCoreLoopStanding(
                    1, "Host", 5, 2, 1, 0, OnlineCoreLoopStanding.UnrankedRank,
                    new OnlineCoreLoopRoundFacts(2, isAlive: true, isForfeited: false, kills: 2, assists: 1)),
                new OnlineCoreLoopStanding(
                    2, "Client", 1, 0, 0, 0, OnlineCoreLoopStanding.UnrankedRank,
                    new OnlineCoreLoopRoundFacts(2, isAlive: false, isForfeited: false, kills: 0, assists: 1)),
                new OnlineCoreLoopStanding(
                    3, "Left", 0, 0, 0, 0, OnlineCoreLoopStanding.UnrankedRank,
                    new OnlineCoreLoopRoundFacts(2, isAlive: true, isForfeited: true, kills: 0, assists: 0)),
            };
            var snapshot = new OnlineCoreLoopSnapshot(
                OnlineCoreLoopRules.Default,
                OnlineCoreLoopPhase.Combat,
                2,
                30f,
                standings,
                "match");

            Assert.That(snapshot.HasLiveRoundFacts, Is.True);
            Assert.That(snapshot.TryCountLiveParticipants(out int alive, out int total), Is.True);
            Assert.That(total, Is.EqualTo(3));
            Assert.That(alive, Is.EqualTo(1), "이탈자를 살아 있다고 셌다.");

            OnlineCoreLoopStanding[] copied = snapshot.CopyStandings();
            Assert.That(copied[0].RoundFacts.Kills, Is.EqualTo(2));
            Assert.That(copied[1].RoundFacts.IsAlive, Is.False);
            Assert.That(copied[2].RoundFacts.IsForfeited, Is.True);
            foreach (OnlineCoreLoopStanding row in copied)
            {
                Assert.That(row.HasRank, Is.False, "정산 전에 등수가 실렸다 (S-GD-P7SC3Q).");
                Assert.That(row.Score, Is.Zero, "정산 전에 점수가 실렸다 (D-GD-7ZMMYN).");
            }
        }

        [Test]
        public void MissingRoundFacts_DoNotBecomeAnEstimate()
        {
            var snapshot = new OnlineCoreLoopSnapshot(
                OnlineCoreLoopRules.Default,
                OnlineCoreLoopPhase.Combat,
                1,
                30f,
                new[] { new OnlineCoreLoopStanding(1, "Host", 0, 0, 0) });

            Assert.That(snapshot.HasLiveRoundFacts, Is.False);
            Assert.That(snapshot.TryCountLiveParticipants(out int alive, out int total), Is.False);
            Assert.That(alive, Is.Zero);
            Assert.That(total, Is.Zero);
        }

        [Test]
        public void Snapshot_CopiesLeaderboardInputAndOutput()
        {
            var standings = new[]
            {
                new OnlineCoreLoopStanding(1, "Host", 2, 1, 3),
            };
            var snapshot = new OnlineCoreLoopSnapshot(
                OnlineCoreLoopRules.Default,
                OnlineCoreLoopPhase.Finished,
                3,
                0f,
                standings);

            standings[0] = new OnlineCoreLoopStanding(2, "Changed", 0, 0, 0);
            OnlineCoreLoopStanding[] copied = snapshot.CopyStandings();
            copied[0] = new OnlineCoreLoopStanding(3, "Changed Again", 0, 0, 0);

            Assert.That(snapshot.CopyStandings()[0].PlayerId, Is.EqualTo(1));
            Assert.That(snapshot.CopyStandings()[0].NickName, Is.EqualTo("Host"));
        }
    }
}
