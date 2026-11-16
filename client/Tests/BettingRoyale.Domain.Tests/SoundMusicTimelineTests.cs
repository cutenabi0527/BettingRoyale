using BettingRoyale.Data.Audio;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public class SoundMusicTimelineTests
    {
        [TestCase(82.99f, SoundId.PreparationBgm)]
        [TestCase(83f, SoundId.None)]
        [TestCase(95f, SoundId.None)]
        public void PreparationChangesAt83Seconds(float elapsed, SoundId expected) =>
            Assert.That(SoundMusicTimeline.Resolve(SoundId.PreparationBgm, elapsed), Is.EqualTo(expected));

        [TestCase(60.99f, SoundId.CombatBgm)]
        [TestCase(61f, SoundId.CombatLateBgm)]
        [TestCase(90f, SoundId.None)]
        [TestCase(180f, SoundId.None)]
        public void CombatMusicDoesNotInventATimeout(float elapsed, SoundId expected) =>
            Assert.That(SoundMusicTimeline.Resolve(SoundId.CombatBgm, elapsed), Is.EqualTo(expected));

        [TestCase(1, SoundId.ResultBgm)]
        [TestCase(2, SoundId.ResultBgm)]
        [TestCase(3, SoundId.ThirdRoundResultBgm)]
        public void ResultsAreRoundSpecificOneShots(int round, SoundId expected)
        {
            Assert.That(SoundMusicTimeline.RoundResult(round), Is.EqualTo(expected));
            Assert.That(SoundMusicTimeline.IsOneShot(expected), Is.True);
            Assert.That(SoundMusicTimeline.Resolve(expected, 120f), Is.EqualTo(expected));
        }

        [Test]
        public void NewRoundStartsWithEarlyMusicAndTrainingNeverEscalates()
        {
            Assert.That(SoundMusicTimeline.Resolve(SoundId.CombatBgm, 0f), Is.EqualTo(SoundId.CombatBgm));
            Assert.That(SoundMusicTimeline.Resolve(SoundId.TrainingBgm, 180f), Is.EqualTo(SoundId.TrainingBgm));
            Assert.That(SoundMusicTimeline.IsOneShot(SoundId.FinalResultBgm), Is.True);
            Assert.That(SoundMusicTimeline.IsOneShot(SoundId.CountdownBgm), Is.False);
        }
    }
}
