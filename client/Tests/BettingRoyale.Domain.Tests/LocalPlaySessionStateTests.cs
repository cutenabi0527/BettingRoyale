using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.LocalPlay;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public class LocalPlaySessionStateTests
    {
        [Test]
        public void DefaultSettings_UseConfirmedMvpRosterAndRoundContract()
        {
            LocalPlaySettings settings = LocalPlaySettings.Default;
            LocalPlayMatchRules rules = LocalPlayMatchRules.Default;

            Assert.That(settings.BotCount, Is.EqualTo(3));
            Assert.That(settings.ParticipantCount, Is.EqualTo(4));
            Assert.That(settings.HumanWeapon, Is.EqualTo(WeaponType.Sword));
            Assert.That(settings.GetBotWeapon(0), Is.EqualTo(WeaponType.Sword));
            Assert.That(settings.GetBotWeapon(1), Is.EqualTo(WeaponType.Bow));
            Assert.That(rules.TotalRounds, Is.EqualTo(3));
            Assert.That(rules.PreparationSeconds, Is.EqualTo(90f));
            Assert.That(rules.AugmentSelectionSeconds, Is.EqualTo(30f));
            Assert.That(rules.CombatCountdownSeconds, Is.EqualTo(3f));
            Assert.That(rules.CombatSeconds, Is.EqualTo(82f));
            Assert.That(rules.CombatSummarySeconds, Is.EqualTo(10f));
        }

        [Test]
        public void Settings_RejectOutOfRangeBotsAndUnimplementedWeapon()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LocalPlaySettings(-1, WeaponType.Sword, LocalPlayBotWeaponPreset.Alternating));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LocalPlaySettings(8, WeaponType.Sword, LocalPlayBotWeaponPreset.Alternating));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LocalPlaySettings(3, WeaponType.Mace, LocalPlayBotWeaponPreset.Alternating));
        }

        [Test]
        public void Match_UsesSeparateMapLoadingCountdownAndSummaryBeforeNextRound()
        {
            var state = new LocalPlaySessionState(LocalPlaySettings.Default);

            Assert.That(state.TryStartMatch(out var transition), Is.True);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.PreparationMapLoadingStarted));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.PreparationLoading));
            Assert.That(state.ActiveMap, Is.EqualTo(LocalPlayMap.Preparation));
            Assert.That(state.IsSimulationPaused, Is.True);

            Assert.That(state.TryCompleteMapLoading(out transition), Is.True);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.PreparationStarted));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Preparation));
            Assert.That(state.IsAugmentSelectionOpen, Is.True);
            Assert.That(state.CanLocalPlayerControl, Is.False);
            Assert.That(state.IsSimulationPaused, Is.False);

            Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out transition), Is.True);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.PreparationControlUnlocked));
            Assert.That(state.CanLocalPlayerControl, Is.True);

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.CombatLoading));
            Assert.That(state.ActiveMap, Is.EqualTo(LocalPlayMap.Combat));
            Assert.That(state.IsSimulationPaused, Is.True);

            Assert.That(state.TryCompleteMapLoading(out transition), Is.True);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.CombatCountdownStarted));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.CombatCountdown));
            Assert.That(state.RemainingSeconds, Is.EqualTo(3f));
            Assert.That(state.IsSimulationPaused, Is.True);

            Assert.That(state.Tick(3f), Is.EqualTo(LocalPlayTransition.CombatStarted));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Combat));
            Assert.That(state.RemainingSeconds, Is.EqualTo(82f));
            Assert.That(state.CanLocalPlayerControl, Is.True);
            Assert.That(state.IsSimulationPaused, Is.False);

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.CombatSummary));
            Assert.That(state.RemainingSeconds, Is.EqualTo(10f));
            Assert.That(state.IsSimulationPaused, Is.True);

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.PreparationMapLoadingStarted));
            Assert.That(state.CurrentRound, Is.EqualTo(2));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.PreparationLoading));
            Assert.That(state.ActiveMap, Is.EqualTo(LocalPlayMap.Preparation));
        }

        [Test]
        public void AugmentSelection_UsesFirstThirtySecondsOfPreparationAndUnlocksImmediately()
        {
            LocalPlaySessionState state = StartLoadedPreparation();

            Assert.That(state.Tick(12f), Is.EqualTo(LocalPlayTransition.None));
            Assert.That(state.RemainingSeconds, Is.EqualTo(78f));
            Assert.That(state.AugmentSelectionRemainingSeconds, Is.EqualTo(18f));
            Assert.That(state.CanLocalPlayerControl, Is.False);

            Assert.That(
                state.TrySelectAugment(state.AugmentOffers.First.Id, out var transition),
                Is.True);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.PreparationControlUnlocked));
            Assert.That(state.IsAugmentSelectionOpen, Is.False);
            Assert.That(state.CanLocalPlayerControl, Is.True);
            Assert.That(state.RemainingSeconds, Is.EqualTo(78f));
        }

        [Test]
        public void AugmentSelection_TimeoutUnlocksControlWithoutExtendingPreparation()
        {
            LocalPlaySessionState state = StartLoadedPreparation();

            Assert.That(state.Tick(30f), Is.EqualTo(LocalPlayTransition.PreparationControlUnlocked));
            Assert.That(state.IsAugmentSelectionOpen, Is.False);
            Assert.That(state.CanLocalPlayerControl, Is.True);
            Assert.That(state.RemainingSeconds, Is.EqualTo(60f));
            Assert.That(state.AugmentSelectionRemainingSeconds, Is.Zero);
        }

        [Test]
        public void AugmentOffers_AreStateOwnedRerollableAndSelectedByStableId()
        {
            LocalPlaySessionState state = StartLoadedPreparation();
            LocalPlaySessionSnapshot firstSnapshot = state.Capture(aliveParticipants: 4);
            string firstCandidate = firstSnapshot.AugmentOffers.First.Id;

            Assert.That(firstSnapshot.AugmentOffers.RerollCount, Is.Zero);
            Assert.That(firstSnapshot.AugmentOffers.First.DisplayName, Is.Not.Empty);
            Assert.That(firstSnapshot.AugmentOffers.Gold, Is.EqualTo(1_000));
            Assert.That(firstSnapshot.AugmentOffers.CurrentRerollCost, Is.EqualTo(100));
            Assert.That(state.TryRerollAugmentOffers(), Is.True);
            Assert.That(state.AugmentOffers.RerollCount, Is.EqualTo(1));
            Assert.That(state.AugmentOffers.Gold, Is.EqualTo(900));
            Assert.That(state.TrySelectAugment("not-a-current-offer", out var transition), Is.False);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.None));

            var selected = state.AugmentOffers.Second;
            Assert.That(state.TrySelectAugment(selected.Id, out transition), Is.True);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.PreparationControlUnlocked));
            Assert.That(state.SelectedAugment, Is.EqualTo(selected));
            Assert.That(state.Capture(4).SelectedAugment, Is.EqualTo(selected));
            Assert.That(state.TryRerollAugmentOffers(), Is.False);
            Assert.That(firstCandidate, Is.Not.Empty);
        }

        [Test]
        public void AugmentOffers_ReuseSharedTierWeaponAndAcquiredBlessingRules()
        {
            var settings = new LocalPlaySettings(
                botCount: 3,
                humanWeapon: WeaponType.Bow,
                botWeaponPreset: LocalPlayBotWeaponPreset.Alternating);
            var state = new LocalPlaySessionState(settings);

            state.TryStartMatch(out _);
            state.TryCompleteMapLoading(out _);
            for (int i = 0; i < LocalPlayBlessingDraftSnapshot.Count; i++)
            {
                BlessingDefinition candidate = state.AugmentOffers.GetAt(i);
                Assert.That(candidate.Tier, Is.EqualTo(BlessingTier.Silver));
                Assert.That(
                    candidate.IsCompatibleWith(BettingRoyale.Data.Blessing.WeaponTag.Bow),
                    Is.True);
            }

            string acquiredId = state.AugmentOffers.First.Id;
            Assert.That(state.TrySelectAugment(acquiredId, out _), Is.True);
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatStarted));
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.PreparationMapLoadingStarted));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);

            Assert.That(state.CurrentRound, Is.EqualTo(2));
            for (int i = 0; i < LocalPlayBlessingDraftSnapshot.Count; i++)
            {
                BlessingDefinition candidate = state.AugmentOffers.GetAt(i);
                Assert.That(candidate.Tier, Is.EqualTo(BlessingTier.Gold));
                Assert.That(candidate.Id, Is.Not.EqualTo(acquiredId));
                Assert.That(
                    candidate.IsCompatibleWith(BettingRoyale.Data.Blessing.WeaponTag.Bow),
                    Is.True);
            }
        }

        [Test]
        public void CustomMatchRules_DriveStateSnapshotAndEveryTimedPhase()
        {
            var rules = new LocalPlayMatchRules(
                totalRounds: 2,
                preparationSeconds: 45f,
                augmentSelectionSeconds: 15f,
                combatCountdownSeconds: 2f,
                combatSeconds: 60f,
                combatSummarySeconds: 5f);
            var state = new LocalPlaySessionState(LocalPlaySettings.Default, rules);

            Assert.That(state.Capture(4).Rules, Is.EqualTo(rules));
            state.TryStartMatch(out _);
            state.TryCompleteMapLoading(out _);
            Assert.That(state.RemainingSeconds, Is.EqualTo(45f));
            Assert.That(state.AugmentSelectionRemainingSeconds, Is.EqualTo(15f));

            state.TrySelectAugment(state.AugmentOffers.First.Id, out _);
            state.SkipCurrentPhase();
            state.TryCompleteMapLoading(out _);
            Assert.That(state.RemainingSeconds, Is.EqualTo(2f));
            state.SkipCurrentPhase();
            Assert.That(state.RemainingSeconds, Is.EqualTo(60f));
            state.SkipCurrentPhase();
            Assert.That(state.RemainingSeconds, Is.EqualTo(5f));
        }

        [Test]
        public void MatchRules_RejectUndefinedBlessingRoundsAndNonFiniteDurations()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LocalPlayMatchRules(
                totalRounds: 4,
                preparationSeconds: 90f,
                augmentSelectionSeconds: 30f,
                combatCountdownSeconds: 3f,
                combatSeconds: 90f,
                combatSummarySeconds: 10f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LocalPlayMatchRules(
                totalRounds: 3,
                preparationSeconds: float.NaN,
                augmentSelectionSeconds: 30f,
                combatCountdownSeconds: 3f,
                combatSeconds: 90f,
                combatSummarySeconds: 10f));
        }

        [Test]
        public void RuntimePhasePolicy_ProjectsInputDamageAimBotsAndPauseFromState()
        {
            LocalPlayRuntimePhasePolicy lobby = LocalPlayRuntimePhasePolicy.Resolve(
                LocalPlayPhase.Lobby,
                canLocalPlayerControl: false);
            Assert.That(lobby.IsSimulationPaused, Is.False);
            Assert.That(lobby.ParticipantDamageEnabled, Is.False);
            Assert.That(lobby.HumanControlEnabled, Is.False);
            Assert.That(lobby.BotCombatEnabled, Is.False);

            LocalPlayRuntimePhasePolicy selecting = LocalPlayRuntimePhasePolicy.Resolve(
                LocalPlayPhase.Preparation,
                canLocalPlayerControl: false);
            Assert.That(selecting.IsSimulationPaused, Is.False);
            Assert.That(selecting.HumanControlEnabled, Is.False);
            Assert.That(selecting.HumanAimEnabled, Is.False);

            LocalPlayRuntimePhasePolicy preparing = LocalPlayRuntimePhasePolicy.Resolve(
                LocalPlayPhase.Preparation,
                canLocalPlayerControl: true);
            Assert.That(preparing.ParticipantDamageEnabled, Is.False);
            Assert.That(preparing.HumanControlEnabled, Is.True);
            Assert.That(preparing.HumanAimEnabled, Is.True);
            Assert.That(preparing.BotCombatEnabled, Is.False);

            LocalPlayRuntimePhasePolicy combat = LocalPlayRuntimePhasePolicy.Resolve(
                LocalPlayPhase.Combat,
                canLocalPlayerControl: true);
            Assert.That(combat.IsSimulationPaused, Is.False);
            Assert.That(combat.ParticipantDamageEnabled, Is.True);
            Assert.That(combat.HumanControlEnabled, Is.True);
            Assert.That(combat.BotCombatEnabled, Is.True);
            Assert.That(combat.HumanAimEnabled, Is.True);
            Assert.That(combat.BotAimEnabled, Is.True);

            LocalPlayRuntimePhasePolicy summary = LocalPlayRuntimePhasePolicy.Resolve(
                LocalPlayPhase.CombatSummary,
                canLocalPlayerControl: false);
            Assert.That(summary.IsSimulationPaused, Is.True);
            Assert.That(summary.ParticipantDamageEnabled, Is.False);
            Assert.That(summary.HumanControlEnabled, Is.False);
            Assert.That(summary.BotCombatEnabled, Is.False);
        }

        [Test]
        public void StateAndSnapshot_UseTheSameRuntimePolicyAcrossEveryPhase()
        {
            var state = new LocalPlaySessionState(LocalPlaySettings.Default);
            AssertRuntimePolicyConsistency(state);

            Assert.That(state.TryStartMatch(out _), Is.True);
            AssertRuntimePolicyConsistency(state);

            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.IsAugmentSelectionOpen, Is.True);
            AssertRuntimePolicyConsistency(state);

            Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);
            Assert.That(state.IsAugmentSelectionOpen, Is.False);
            AssertRuntimePolicyConsistency(state);

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            AssertRuntimePolicyConsistency(state);

            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            AssertRuntimePolicyConsistency(state);

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatStarted));
            AssertRuntimePolicyConsistency(state);

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));
            AssertRuntimePolicyConsistency(state);

            Assert.That(state.TryAbortMatch(), Is.True);
            AssertRuntimePolicyConsistency(state);
        }

        [Test]
        public void Combat_LoadWaitsThenCountsDownAndShowsTenSecondSummary()
        {
            LocalPlaySessionState state = StartLoadedPreparation();
            state.TrySelectAugment(state.AugmentOffers.First.Id, out _);

            Assert.That(state.Tick(90f), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.Tick(100f), Is.EqualTo(LocalPlayTransition.None));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.CombatLoading));

            Assert.That(state.TryCompleteMapLoading(out var transition), Is.True);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.CombatCountdownStarted));
            Assert.That(state.Tick(2f), Is.EqualTo(LocalPlayTransition.None));
            Assert.That(state.RemainingSeconds, Is.EqualTo(1f));
            Assert.That(state.Tick(1f), Is.EqualTo(LocalPlayTransition.CombatStarted));

            Assert.That(state.Tick(90f), Is.EqualTo(LocalPlayTransition.None));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Combat));
            Assert.That(state.RemainingSeconds, Is.Zero);
            Assert.That(state.Tick(90f), Is.EqualTo(LocalPlayTransition.None));
            Assert.That(state.TryCompleteCombat(out _), Is.True);
            Assert.That(state.Tick(9f), Is.EqualTo(LocalPlayTransition.None));
            Assert.That(state.RemainingSeconds, Is.EqualTo(1f));
            Assert.That(state.Tick(1f), Is.EqualTo(LocalPlayTransition.PreparationMapLoadingStarted));
            Assert.That(state.CurrentRound, Is.EqualTo(2));
        }

        [Test]
        public void ThirdCombatSummary_ReturnsToSameLobbySettings()
        {
            var settings = new LocalPlaySettings(7, WeaponType.Bow, LocalPlayBotWeaponPreset.Sword);
            var state = new LocalPlaySessionState(settings);

            Assert.That(state.TryStartMatch(out _), Is.True);
            for (int round = 1; round <= state.Rules.TotalRounds; round++)
            {
                Assert.That(state.TryCompleteMapLoading(out _), Is.True);
                Assert.That(
                    state.TrySelectAugment(state.AugmentOffers.First.Id, out _),
                    Is.True);
                Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
                Assert.That(state.TryCompleteMapLoading(out _), Is.True);
                Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatStarted));
                Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));

                LocalPlayTransition expected = round == state.Rules.TotalRounds
                    ? LocalPlayTransition.MatchCompleted
                    : LocalPlayTransition.PreparationMapLoadingStarted;
                Assert.That(state.SkipCurrentPhase(), Is.EqualTo(expected));
            }

            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Lobby));
            Assert.That(state.ActiveMap, Is.EqualTo(LocalPlayMap.Preparation));
            Assert.That(state.CurrentRound, Is.Zero);
            Assert.That(state.HasCompletedMatch, Is.True);
            Assert.That(state.Settings, Is.EqualTo(settings));
            Assert.That(state.Settings.ParticipantCount, Is.EqualTo(8));
        }

        [Test]
        public void Combat_CanEndImmediatelyWhenSurvivingSideRuleIsSatisfied()
        {
            LocalPlaySessionState state = StartLoadedPreparation();
            state.TrySelectAugment(state.AugmentOffers.First.Id, out _);
            state.SkipCurrentPhase();
            state.TryCompleteMapLoading(out _);
            state.SkipCurrentPhase();

            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Combat));
            Assert.That(state.TryCompleteCombat(out var transition), Is.True);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.CombatSummary));
            Assert.That(state.RemainingSeconds, Is.EqualTo(state.Rules.CombatSummarySeconds));
            Assert.That(state.IsSimulationPaused, Is.True);
        }

        [Test]
        public void LastSurvivorObservation_KeepsCombatPhaseAndOnlyLocksDamage()
        {
            LocalPlaySessionState state = StartLoadedPreparation();
            state.TrySelectAugment(state.AugmentOffers.First.Id, out _);
            state.SkipCurrentPhase();
            state.TryCompleteMapLoading(out _);
            state.SkipCurrentPhase();
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Combat));
            Assert.That(state.IsObservingLastSurvivor, Is.False);

            Assert.That(state.TryBeginLastSurvivorObservation(), Is.True);
            Assert.That(state.TryBeginLastSurvivorObservation(), Is.False, "관찰 진입은 멱등이어야 한다.");

            // 단계는 전투 그대로다 — 요약으로 넘기지도, 시뮬레이션을 멈추지도 않는다.
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Combat));
            LocalPlayRuntimePhasePolicy policy = state.RuntimePhasePolicy;
            Assert.That(policy.IsSimulationPaused, Is.False);
            Assert.That(policy.HumanControlEnabled, Is.True, "관찰 중에 이동이 막히면 관전이 불가능하다.");
            Assert.That(policy.HumanAimEnabled, Is.True);
            Assert.That(policy.ParticipantDamageEnabled, Is.False);
            Assert.That(policy.BotCombatEnabled, Is.False);
            Assert.That(policy.HumanAttackEnabled, Is.False, "관찰 중 사람 공격은 이동·조준과 분리해 잠근다.");

            Assert.That(
                state.LastSurvivorObservationRemainingSeconds,
                Is.EqualTo(CombatRoundObservationWindow.DefaultSeconds));
            Assert.That(state.IsLastSurvivorObservationComplete, Is.False);

            Assert.That(
                state.Tick(CombatRoundObservationWindow.DefaultSeconds - 1f),
                Is.EqualTo(LocalPlayTransition.None));
            Assert.That(state.IsLastSurvivorObservationComplete, Is.False);
            Assert.That(state.Tick(1f), Is.EqualTo(LocalPlayTransition.None), "관찰 창이 스스로 요약을 열었다.");
            Assert.That(state.IsLastSurvivorObservationComplete, Is.True);
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Combat));

            LocalPlaySessionSnapshot snapshot = state.Capture(aliveParticipants: 1);
            Assert.That(snapshot.IsObservingLastSurvivor, Is.True);
            Assert.That(snapshot.LastSurvivorObservationRemainingSeconds, Is.Zero);

            // 요약으로 넘어가면 관찰은 사라진다 — 다음 라운드가 피해가 막힌 채 시작하지 않는다.
            Assert.That(state.TryCompleteCombat(out _), Is.True);
            Assert.That(state.IsObservingLastSurvivor, Is.False);
            Assert.That(state.LastSurvivorObservationRemainingSeconds, Is.Zero);
        }

        [Test]
        public void LastSurvivorObservation_IsRejectedOutsideCombatAndCanBeClosedExplicitly()
        {
            var lobby = new LocalPlaySessionState(LocalPlaySettings.Default);
            Assert.That(lobby.TryBeginLastSurvivorObservation(), Is.False);

            LocalPlaySessionState state = StartLoadedPreparation();
            Assert.That(state.TryBeginLastSurvivorObservation(), Is.False, "준비 단계에서 관찰이 열렸다.");
            state.TrySelectAugment(state.AugmentOffers.First.Id, out _);
            state.SkipCurrentPhase();
            state.TryCompleteMapLoading(out _);
            state.SkipCurrentPhase();

            Assert.That(state.CompleteLastSurvivorObservationNow(), Is.False, "관찰이 없는데 닫혔다.");
            Assert.That(state.TryBeginLastSurvivorObservation(), Is.True);
            Assert.That(state.CompleteLastSurvivorObservationNow(), Is.True);
            Assert.That(state.IsLastSurvivorObservationComplete, Is.True);
            Assert.That(state.CompleteLastSurvivorObservationNow(), Is.False);
        }

        [Test]
        public void LiveRoundFacts_AreCopiedForLocalHudWithoutScoreOrRankFields()
        {
            LocalPlaySessionState state = StartLoadedPreparation();
            state.TrySelectAugment(state.AugmentOffers.First.Id, out _);
            state.SkipCurrentPhase();
            state.TryCompleteMapLoading(out _);
            state.SkipCurrentPhase();

            var source = new[]
            {
                new CombatRoundLiveStanding(0, new CombatantId(101), true, false, 2, 1),
                new CombatRoundLiveStanding(1, new CombatantId(102), false, false, 0, 3),
            };
            LocalPlaySessionSnapshot snapshot = state.Capture(1, source);

            Assert.That(snapshot.HasLiveRoundFacts, Is.True);
            Assert.That(snapshot.TryCountLiveParticipants(out int alive, out int total), Is.True);
            Assert.That(alive, Is.EqualTo(1));
            Assert.That(total, Is.EqualTo(2));
            Assert.That(snapshot.CopyLiveStandings()[0].Kills, Is.EqualTo(2));
            Assert.That(snapshot.CopyLiveStandings()[1].IsAlive, Is.False);
            Assert.That(snapshot.LiveStandings, Is.Not.SameAs(source));
        }

        [Test]
        public void CombatCompletion_IsRejectedOutsideCombat()
        {
            var state = new LocalPlaySessionState(LocalPlaySettings.Default);

            Assert.That(state.ActiveMap, Is.EqualTo(LocalPlayMap.Preparation));
            Assert.That(state.TryCompleteCombat(out var transition), Is.False);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.None));
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Lobby));
        }

        [Test]
        public void Settings_CanChangeOnlyInLobbyAndPersistIntoNextMatch()
        {
            var state = new LocalPlaySessionState(LocalPlaySettings.Default);
            var lobbySettings = new LocalPlaySettings(5, WeaponType.Bow, LocalPlayBotWeaponPreset.Bow);

            Assert.That(state.TryUpdateSettings(lobbySettings), Is.True);
            Assert.That(state.TryStartMatch(out _), Is.True);
            Assert.That(state.TryUpdateSettings(LocalPlaySettings.Default), Is.False);
            Assert.That(state.Settings, Is.EqualTo(lobbySettings));
        }

        [Test]
        public void LoadingCompletion_IsRejectedOutsideLoadingPhases()
        {
            var state = new LocalPlaySessionState(LocalPlaySettings.Default);

            Assert.That(state.TryCompleteMapLoading(out var transition), Is.False);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.None));
            state.TryStartMatch(out _);
            state.TryCompleteMapLoading(out _);
            Assert.That(state.TryCompleteMapLoading(out transition), Is.False);
            Assert.That(transition, Is.EqualTo(LocalPlayTransition.None));
        }

        [Test]
        public void AbortMatch_ReturnsAnyInProgressPhaseToAnUnpausedLobby()
        {
            LocalPlaySessionState state = StartLoadedPreparation();
            state.TrySelectAugment(state.AugmentOffers.First.Id, out _);
            state.SkipCurrentPhase();

            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.CombatLoading));
            Assert.That(state.IsSimulationPaused, Is.True);

            Assert.That(state.TryAbortMatch(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Lobby));
            Assert.That(state.ActiveMap, Is.EqualTo(LocalPlayMap.Preparation));
            Assert.That(state.CurrentRound, Is.Zero);
            Assert.That(state.RemainingSeconds, Is.Zero);
            Assert.That(state.AugmentSelectionRemainingSeconds, Is.Zero);
            Assert.That(state.IsAugmentSelectionOpen, Is.False);
            Assert.That(state.IsSimulationPaused, Is.False);
            Assert.That(state.CanLocalPlayerControl, Is.False);
            Assert.That(state.HasCompletedMatch, Is.False);
            Assert.That(state.TryAbortMatch(), Is.False);
        }

        [Test]
        public void AugmentLocks_StopAtTwoAndFreeTheSlotWhenToggledOff()
        {
            LocalPlaySessionState state = StartLoadedPreparation();

            Assert.That(state.AugmentOffers.RemainingLockCount, Is.EqualTo(2));
            Assert.That(state.AugmentOffers.IsLocked(0), Is.False);

            Assert.That(state.TryToggleAugmentLock(0), Is.True);
            Assert.That(state.TryToggleAugmentLock(1), Is.True);
            Assert.That(state.AugmentOffers.LockedCount, Is.EqualTo(2));
            Assert.That(state.AugmentOffers.RemainingLockCount, Is.Zero);

            // 세 번째 잠금은 상한(S-GD-S46S28)에 걸린다. 상태는 그대로다.
            Assert.That(state.TryToggleAugmentLock(2), Is.False);
            Assert.That(state.AugmentOffers.IsLocked(2), Is.False);
            Assert.That(state.AugmentOffers.LockedCount, Is.EqualTo(2));

            Assert.That(state.TryToggleAugmentLock(0), Is.True);
            Assert.That(state.AugmentOffers.IsLocked(0), Is.False);
            Assert.That(state.AugmentOffers.RemainingLockCount, Is.EqualTo(1));
            Assert.That(state.TryToggleAugmentLock(2), Is.True);
            Assert.That(state.AugmentOffers.IsLocked(2), Is.True);
        }

        [Test]
        public void AugmentLockToggle_IsRejectedWithoutThrowingOutsideOpenSelection()
        {
            var lobby = new LocalPlaySessionState(LocalPlaySettings.Default);
            Assert.That(lobby.TryToggleAugmentLock(0), Is.False);

            LocalPlaySessionState state = StartLoadedPreparation();
            Assert.That(state.TryToggleAugmentLock(-1), Is.False);
            Assert.That(state.TryToggleAugmentLock(LocalPlayBlessingDraftSnapshot.Count), Is.False);
            Assert.That(state.TryToggleAugmentLock(int.MaxValue), Is.False);
            Assert.That(state.AugmentOffers.LockedCount, Is.Zero);

            Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);
            Assert.That(state.IsAugmentSelectionOpen, Is.False);
            Assert.That(state.TryToggleAugmentLock(0), Is.False);
        }

        [Test]
        public void Reroll_KeepsTheLockedCandidateAndItsLockState()
        {
            LocalPlaySessionState state = StartLoadedPreparation();

            Assert.That(state.TryToggleAugmentLock(1), Is.True);
            string lockedId = state.AugmentOffers.Second.Id;

            Assert.That(state.TryRerollAugmentOffers(), Is.True);
            Assert.That(state.AugmentOffers.Second.Id, Is.EqualTo(lockedId));
            Assert.That(state.AugmentOffers.IsLocked(1), Is.True);
            Assert.That(state.AugmentOffers.RemainingLockCount, Is.EqualTo(1));
            Assert.That(state.Capture(4).AugmentOffers.IsLocked(1), Is.True);
        }

        [Test]
        public void SelectionTimeout_AutomaticallyTakesALockedCandidateExactlyOnce()
        {
            LocalPlaySessionState state = StartLoadedPreparation();

            Assert.That(state.TryToggleAugmentLock(2), Is.True);
            string lockedId = state.AugmentOffers.Third.Id;

            Assert.That(state.Tick(30f), Is.EqualTo(LocalPlayTransition.PreparationControlUnlocked));
            Assert.That(state.IsAugmentSelectionOpen, Is.False);
            Assert.That(state.SelectedAugment, Is.Not.Null);
            Assert.That(state.SelectedAugment.Id, Is.EqualTo(lockedId));
            Assert.That(state.AcquiredAugments.Count, Is.EqualTo(1));
            Assert.That(state.AugmentOffers.IsLocked(2), Is.True);

            // 남은 준비 시간이 끝나며 전투 맵 로딩으로 넘어가도 두 번째 획득은 없다.
            Assert.That(state.Tick(60f), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.AcquiredAugments.Count, Is.EqualTo(1));
            Assert.That(state.AcquiredAugments[0].Id, Is.EqualTo(lockedId));
        }

        [Test]
        public void LeavingPreparationWithoutSelecting_StillGrantsExactlyOneAugment()
        {
            LocalPlaySessionState skipped = StartLoadedPreparation();
            Assert.That(skipped.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(skipped.SelectedAugment, Is.Not.Null);
            Assert.That(skipped.AcquiredAugments.Count, Is.EqualTo(1));

            LocalPlaySessionState expired = StartLoadedPreparation();
            Assert.That(expired.Tick(90f), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(expired.AcquiredAugments.Count, Is.EqualTo(1));

            LocalPlaySessionState chosen = StartLoadedPreparation();
            BlessingDefinition picked = chosen.AugmentOffers.Second;
            Assert.That(chosen.TrySelectAugment(picked.Id, out _), Is.True);
            Assert.That(chosen.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(chosen.AcquiredAugments.Count, Is.EqualTo(1));
            Assert.That(chosen.AcquiredAugments[0], Is.EqualTo(picked));
        }

        [Test]
        public void AcquiredAugments_AccumulateAcrossRoundsAndClearOnNewMatchAndAbort()
        {
            var state = new LocalPlaySessionState(LocalPlaySettings.Default);
            Assert.That(state.AcquiredAugments, Is.Empty);

            Assert.That(state.TryStartMatch(out _), Is.True);
            for (int round = 1; round <= state.Rules.TotalRounds; round++)
            {
                Assert.That(state.TryCompleteMapLoading(out _), Is.True);
                Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);
                Assert.That(state.AcquiredAugments.Count, Is.EqualTo(round));

                Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
                Assert.That(state.TryCompleteMapLoading(out _), Is.True);
                Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatStarted));
                Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));
                state.SkipCurrentPhase();

                // 라운드 경계를 넘어도 유지된다 (S-GD-RFXF8X).
                Assert.That(state.AcquiredAugments.Count, Is.EqualTo(round));
            }

            Assert.That(state.Phase, Is.EqualTo(LocalPlayPhase.Lobby));
            Assert.That(state.AcquiredAugments.Count, Is.EqualTo(3));

            Assert.That(state.TryStartMatch(out _), Is.True);
            Assert.That(state.AcquiredAugments, Is.Empty);

            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);
            Assert.That(state.AcquiredAugments.Count, Is.EqualTo(1));

            Assert.That(state.TryAbortMatch(), Is.True);
            Assert.That(state.AcquiredAugments, Is.Empty);
            Assert.That(state.Capture(4).AcquiredAugments, Is.Empty);
        }

        [Test]
        public void AcquiredSnapshot_IsOwnedImmutableAndNeverChangesAfterCapture()
        {
            LocalPlaySessionState state = StartLoadedPreparation();
            LocalPlaySessionSnapshot beforeSelection = state.Capture(4);
            Assert.That(beforeSelection.AcquiredAugments, Is.Empty);

            BlessingDefinition first = state.AugmentOffers.First;
            Assert.That(state.TrySelectAugment(first.Id, out _), Is.True);
            LocalPlaySessionSnapshot afterFirstRound = state.Capture(4);

            Assert.That(beforeSelection.AcquiredAugments, Is.Empty);
            Assert.That(afterFirstRound.AcquiredAugments.Count, Is.EqualTo(1));
            Assert.That(afterFirstRound.AcquiredAugments[0], Is.EqualTo(first));

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            state.SkipCurrentPhase();
            state.SkipCurrentPhase();
            state.SkipCurrentPhase();
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.CurrentRound, Is.EqualTo(2));
            Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);

            // 이미 찍힌 스냅샷은 소급해 바뀌지 않는다.
            Assert.That(beforeSelection.AcquiredAugments, Is.Empty);
            Assert.That(afterFirstRound.AcquiredAugments.Count, Is.EqualTo(1));
            Assert.That(state.Capture(4).AcquiredAugments.Count, Is.EqualTo(2));

            Assert.Throws<NotSupportedException>(() =>
                ((IList<BlessingDefinition>)afterFirstRound.AcquiredAugments).Clear());
            Assert.Throws<NotSupportedException>(() =>
                ((IList<BlessingDefinition>)state.AcquiredAugments).Add(first));
            Assert.That(state.AcquiredAugments.Count, Is.EqualTo(2));
        }

        private static LocalPlaySessionState StartLoadedPreparation()
        {
            var state = new LocalPlaySessionState(LocalPlaySettings.Default);
            state.TryStartMatch(out _);
            state.TryCompleteMapLoading(out _);
            return state;
        }

        private static void AssertRuntimePolicyConsistency(LocalPlaySessionState state)
        {
            LocalPlayRuntimePhasePolicy policy = state.RuntimePhasePolicy;
            LocalPlaySessionSnapshot snapshot = state.Capture(state.Settings.ParticipantCount);

            Assert.That(state.IsSimulationPaused, Is.EqualTo(policy.IsSimulationPaused));
            Assert.That(state.CanLocalPlayerControl, Is.EqualTo(policy.HumanControlEnabled));
            Assert.That(snapshot.IsSimulationPaused, Is.EqualTo(policy.IsSimulationPaused));
            Assert.That(snapshot.CanLocalPlayerControl, Is.EqualTo(policy.HumanControlEnabled));
        }
    }
}
