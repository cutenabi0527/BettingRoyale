using BettingRoyale.Data.Audio;
using BettingRoyale.Data.Equipment;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public class SoundPresentationTests
    {
        [Test]
        public void Countdown_RepeatedSnapshotDoesNotRetrigger()
        {
            var state = new SoundCountdown();
            Assert.That(state.Observe(10), Is.True);
            Assert.That(state.Observe(9.9f), Is.False);
            Assert.That(state.Observe(9.1f), Is.False);
            Assert.That(state.Observe(9), Is.True);
        }
        [Test]
        public void Countdown_SkippedSecondsDoNotQueueCatchup()
        {
            var state = new SoundCountdown();
            state.Observe(10);
            Assert.That(state.Observe(3.5f), Is.True);
            Assert.That(state.Observe(3.4f), Is.False);
            Assert.That(state.Observe(0), Is.False);
        }
        [Test]
        public void Countdown_CancelAndReenterRestartsCue()
        {
            var state = new SoundCountdown();
            state.Observe(3);
            state.Observe(0);
            Assert.That(state.Observe(3), Is.True);
            state.Reset();
            Assert.That(state.Observe(3), Is.True);
        }
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        [TestCase(11f)]
        public void Countdown_InvalidAndOutOfWindowAreSilent(float value)
        {
            Assert.That(new SoundCountdown().Observe(value), Is.False);
        }

        private static EquipmentInventorySnapshot Inventory(int a, int b) =>
            new EquipmentInventorySnapshot(1, 0, new EquipmentInventorySlot(10, a),
                new EquipmentInventorySlot(10, b), default);
        [Test]
        public void Acquisition_ReorderingAndRemovalAreSilent()
        {
            Assert.That(SoundInventoryChange.HasAcquired(Inventory(3, 1), Inventory(1, 3)), Is.False);
            Assert.That(SoundInventoryChange.HasAcquired(Inventory(3, 1), Inventory(1, 1)), Is.False);
            Assert.That(SoundInventoryChange.HasAcquired(Inventory(3, 1), Inventory(3, 1)), Is.False);
        }
        [Test]
        public void Acquisition_StackIncreaseIsAudible()
        {
            Assert.That(SoundInventoryChange.HasAcquired(Inventory(3, 1), Inventory(3, 2)), Is.True);
        }
    }
}
