using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    [TestFixture]
    public sealed class BlessingEffectTrackerTests
    {
        private BlessingEffectTracker tracker;

        [SetUp]
        public void SetUp() => tracker = new BlessingEffectTracker();

        [Test]
        public void Apply_keeps_activation_order_and_refresh_keeps_position()
        {
            tracker.Apply(0f, 1, "burn", BlessingEffectKind.Duration, BlessingEffectDuration.Of(10f));
            tracker.Apply(1f, 1, "chill", BlessingEffectKind.Duration, BlessingEffectDuration.Of(10f));
            tracker.Apply(2f, 1, "rally", BlessingEffectKind.Duration, BlessingEffectDuration.Of(10f));

            Assert.That(tracker.Refresh(4f, 1, "burn", BlessingEffectDuration.Of(10f)),
                Is.EqualTo(BlessingEffectChange.Refreshed));
            Assert.That(tracker.Snapshot.Select(x => x.EffectId),
                Is.EqualTo(new[] { "burn", "chill", "rally" }));
            Assert.That(tracker[0].StartTime, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(tracker[0].RemainingSeconds, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void Same_effect_from_different_sources_is_independent()
        {
            tracker.Apply(0f, 1, "burn", BlessingEffectKind.Duration, BlessingEffectDuration.Of(10f));
            tracker.Apply(0f, 2, "burn", BlessingEffectKind.Duration, BlessingEffectDuration.Of(4f));
            tracker.Refresh(3f, 2, "burn", BlessingEffectDuration.Of(4f));

            Assert.That(tracker.TryGet(1, "burn", out var first), Is.True);
            Assert.That(first.RemainingSeconds, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(tracker.TryGet(2, "burn", out var second), Is.True);
            Assert.That(second.RemainingSeconds, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void Stack_change_refreshes_one_shared_timer_without_reordering()
        {
            tracker.Apply(0f, 1, "rally", BlessingEffectKind.Stack, BlessingEffectDuration.Of(5f));
            tracker.Apply(0f, 1, "chill", BlessingEffectKind.Duration, BlessingEffectDuration.Of(5f));
            tracker.Tick(3f);

            Assert.That(tracker.AddStacks(3f, 1, "rally", 1), Is.EqualTo(BlessingEffectChange.StackChanged));
            Assert.That(tracker[0].Stacks, Is.EqualTo(2));
            Assert.That(tracker[0].RemainingSeconds, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(tracker.Snapshot.Select(x => x.EffectId), Is.EqualTo(new[] { "rally", "chill" }));
        }

        [Test]
        public void Expiration_removes_at_zero_and_next_activation_goes_to_tail()
        {
            tracker.Apply(0f, 1, "burn", BlessingEffectKind.Duration, BlessingEffectDuration.Of(3f));
            tracker.Apply(0f, 1, "chill", BlessingEffectKind.Duration, BlessingEffectDuration.Of(10f));

            Assert.That(tracker.Tick(3f), Is.EqualTo(1));
            tracker.Apply(4f, 1, "burn", BlessingEffectKind.Duration, BlessingEffectDuration.Of(3f));
            Assert.That(tracker.Snapshot.Select(x => x.EffectId), Is.EqualTo(new[] { "chill", "burn" }));
        }

        [Test]
        public void Unlimited_is_explicit_and_survives_ticks()
        {
            tracker.Apply(0f, 1, "counter", BlessingEffectKind.Empower, BlessingEffectDuration.Unlimited);
            tracker.Tick(10000f);

            Assert.That(tracker.TryGet(1, "counter", out var effect), Is.True);
            Assert.That(effect.IsUnlimited, Is.True);
            Assert.That(effect.RemainingSeconds, Is.Zero);
            Assert.That(effect.NormalizedRemaining, Is.EqualTo(1f));
            Assert.That(BlessingEffectDuration.Of(-1f).IsUnlimited, Is.False);
            Assert.That(BlessingEffectDuration.Of(-1f).IsExpired, Is.True);
        }

        [Test]
        public void Passive_does_not_create_a_hud_instance()
        {
            Assert.That(tracker.Apply(0f, 1, "armor", BlessingEffectKind.Passive,
                BlessingEffectDuration.Unlimited), Is.EqualTo(BlessingEffectChange.None));
            Assert.That(tracker.Count, Is.Zero);
        }

        [Test]
        public void Zero_stacks_removes_and_zero_duration_never_adds()
        {
            Assert.That(tracker.Apply(0f, 1, "burn", BlessingEffectKind.Duration,
                BlessingEffectDuration.Of(0f)), Is.EqualTo(BlessingEffectChange.None));
            tracker.Apply(0f, 1, "rally", BlessingEffectKind.Stack,
                BlessingEffectDuration.Of(5f), stacks: 2);
            Assert.That(tracker.SetStacks(1f, 1, "rally", 0), Is.EqualTo(BlessingEffectChange.Removed));
            Assert.That(tracker.Count, Is.Zero);
        }

        [Test]
        public void Clearing_display_copy_does_not_limit_gameplay_state()
        {
            for (int i = 0; i < 24; i++)
            {
                tracker.Apply(0f, 1, "effect-" + i, BlessingEffectKind.Duration,
                    BlessingEffectDuration.Of(10f));
            }

            var visible = new List<BlessingEffectInstance>();
            tracker.CopySnapshotTo(visible);
            visible.RemoveRange(6, visible.Count - 6);

            Assert.That(visible, Has.Count.EqualTo(6));
            Assert.That(tracker.Count, Is.EqualTo(24));
            Assert.That(tracker.TryGet(1, "effect-23", out _), Is.True);
        }

        [Test]
        public void Invalid_effect_id_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => tracker.Apply(0f, 1, " ", BlessingEffectKind.Duration,
                BlessingEffectDuration.Of(1f)));
            Assert.Throws<ArgumentException>(() => tracker.Remove(1, string.Empty));
        }

        [Test]
        public void Refresh_does_not_resurrect_expired_effect()
        {
            tracker.Apply(0f, 1, "burn", BlessingEffectKind.Duration, BlessingEffectDuration.Of(5f));
            Assert.That(tracker.Refresh(5f, 1, "burn", BlessingEffectDuration.Of(5f)),
                Is.EqualTo(BlessingEffectChange.None));
            Assert.That(tracker.Count, Is.Zero);
        }
    }
}
