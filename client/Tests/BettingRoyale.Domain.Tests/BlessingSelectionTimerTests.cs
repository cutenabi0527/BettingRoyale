using System;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.LocalPlay;
using BettingRoyale.Data.Online;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복 선택 모달의 남은 시간 표시 계약 (S-GD-DP2DVC 최대 30초, S-GD-NK8ZJ7 0초 자동 선택).
    /// 시계를 새로 만들지 않고 두 스냅샷의 값을 그대로 쓰는지 여기서 고정한다.
    /// </summary>
    public class BlessingSelectionTimerTests
    {
        [Test]
        public void OnlineSnapshotDrivesTheModalTimerWithoutASecondClock()
        {
            OnlineCoreLoopRules rules = OnlineCoreLoopRules.Default;
            var snapshot = new OnlineCoreLoopSnapshot(
                rules,
                OnlineCoreLoopPhase.Preparation,
                currentRound: 2,
                remainingSeconds: 76.4f,
                Array.Empty<OnlineCoreLoopStanding>());

            BlessingSelectionTimer timer = BlessingSelectionTimer.FromSeconds(
                snapshot.BlessingSelectionRemainingSeconds, rules.BlessingSelectionSeconds);

            Assert.That(timer.HasLimit, Is.True);
            Assert.That(timer.LimitSeconds, Is.EqualTo(30f));
            Assert.That(timer.RemainingSeconds, Is.EqualTo(16.4f).Within(0.001f));
            // 올림이므로 16.4초는 아직 17 이다 — 내림이면 마지막 1초가 두 번 보인다.
            Assert.That(timer.DisplaySeconds, Is.EqualTo(17));
            Assert.That(timer.Progress, Is.EqualTo(16.4f / 30f).Within(0.001f));
            Assert.That(timer.IsExpired, Is.False);
        }

        [Test]
        public void LocalPlaySnapshotDrivesTheSameTimerContract()
        {
            LocalPlayMatchRules rules = LocalPlayMatchRules.Default;
            BlessingSelectionTimer timer = BlessingSelectionTimer.FromSeconds(
                remainingSeconds: 12f, limitSeconds: rules.AugmentSelectionSeconds);

            Assert.That(rules.AugmentSelectionSeconds, Is.EqualTo(30f));
            Assert.That(timer.LimitSeconds, Is.EqualTo(30f));
            Assert.That(timer.DisplaySeconds, Is.EqualTo(12));
            Assert.That(timer.Progress, Is.EqualTo(0.4f).Within(0.001f));
        }

        [Test]
        public void DisplayedLimitNeverExceedsTheConfirmedThirtySeconds()
        {
            BlessingSelectionTimer overLimit = BlessingSelectionTimer.FromSeconds(
                remainingSeconds: 45f, limitSeconds: 45f);

            Assert.That(BlessingSelectionTimer.MaxLimitSeconds, Is.EqualTo(30f));
            Assert.That(overLimit.LimitSeconds, Is.EqualTo(30f));
            Assert.That(overLimit.RemainingSeconds, Is.EqualTo(30f));
            Assert.That(overLimit.DisplaySeconds, Is.EqualTo(30));
            Assert.That(overLimit.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void ShortPreparationKeepsItsSmallerLimitInsteadOfPaddingToThirty()
        {
            // 준비가 짧으면 규칙이 30초보다 작은 한도를 준다. 그 값을 그대로 그린다.
            OnlineCoreLoopRules quick = OnlineCoreLoopRules.QuickPlaytest;
            var snapshot = new OnlineCoreLoopSnapshot(
                quick,
                OnlineCoreLoopPhase.Preparation,
                currentRound: 1,
                remainingSeconds: quick.PreparationSeconds,
                Array.Empty<OnlineCoreLoopStanding>());

            BlessingSelectionTimer timer = BlessingSelectionTimer.FromSeconds(
                snapshot.BlessingSelectionRemainingSeconds, quick.BlessingSelectionSeconds);

            Assert.That(quick.BlessingSelectionSeconds, Is.EqualTo(3));
            Assert.That(timer.LimitSeconds, Is.EqualTo(3f));
            Assert.That(timer.DisplaySeconds, Is.EqualTo(3));
            Assert.That(timer.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void ExpiredSelectionShowsZeroAndAnEmptyDial()
        {
            BlessingSelectionTimer timer = BlessingSelectionTimer.FromSeconds(
                remainingSeconds: 0f, limitSeconds: 30f);

            Assert.That(timer.HasLimit, Is.True);
            Assert.That(timer.DisplaySeconds, Is.Zero);
            Assert.That(timer.Progress, Is.Zero);
            Assert.That(timer.IsExpired, Is.True);
        }

        [Test]
        public void UnknownLimitIsNotDrawnAsZeroSeconds()
        {
            foreach (float limit in new[] { 0f, -5f, float.NaN, float.PositiveInfinity })
            {
                BlessingSelectionTimer timer = BlessingSelectionTimer.FromSeconds(10f, limit);

                Assert.That(timer.HasLimit, Is.False, limit.ToString());
                Assert.That(timer.IsExpired, Is.False, limit.ToString());
                Assert.That(timer.Progress, Is.Zero, limit.ToString());
            }
        }

        [Test]
        public void LateOrNegativeSnapshotsAreClampedIntoTheDialRange()
        {
            Assert.That(BlessingSelectionTimer.FromSeconds(-4f, 30f).RemainingSeconds, Is.Zero);
            Assert.That(BlessingSelectionTimer.FromSeconds(float.NaN, 30f).RemainingSeconds, Is.Zero);

            BlessingSelectionTimer stale = BlessingSelectionTimer.FromSeconds(28f, 10f);
            Assert.That(stale.RemainingSeconds, Is.EqualTo(10f));
            Assert.That(stale.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void SubSecondRemainderStillReadsAsOneSecond()
        {
            Assert.That(BlessingSelectionTimer.FromSeconds(0.2f, 30f).DisplaySeconds, Is.EqualTo(1));
            Assert.That(BlessingSelectionTimer.FromSeconds(0.2f, 30f).IsExpired, Is.False);
        }
    }
}
