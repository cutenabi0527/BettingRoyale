using BettingRoyale.Data.Training;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 좌우 이동 표적의 순수 규칙 (S-GD-VWW8YK, 임시 수치는 S-GD-8Q3ZN9).
    /// Unity 없이 도는 계산이므로 "표적이 서로 겹친다 / 사격장을 벗어난다" 를 씬 없이 잡는다.
    /// </summary>
    public class TrainingRangeMotionTests
    {
        private const float Tolerance = 0.0005f;

        [Test]
        public void Track_StartsAtCentreAndReachesEachEndOnce()
        {
            var track = new LateralTargetTrack(2f, 4f, 0f);

            Assert.That(track.IsMoving, Is.True);
            Assert.That(track.CycleSeconds, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(track.OffsetAt(0f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(track.OffsetAt(0.5f), Is.EqualTo(2f).Within(Tolerance), "1/4 주기에 +쪽 끝");
            Assert.That(track.OffsetAt(1.0f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(track.OffsetAt(1.5f), Is.EqualTo(-2f).Within(Tolerance), "3/4 주기에 -쪽 끝");
            Assert.That(track.OffsetAt(2.0f), Is.EqualTo(0f).Within(Tolerance), "한 주기 뒤에 원위치");
        }

        [Test]
        public void Track_NeverLeavesItsTravelWidth()
        {
            var track = new LateralTargetTrack(1.5f, 2.4f, 0.37f);

            for (int step = 0; step <= 2000; step++)
            {
                float offset = track.OffsetAt(step * 0.01f);
                Assert.That(offset, Is.InRange(-1.5f - Tolerance, 1.5f + Tolerance));
            }
        }

        [Test]
        public void Track_MovesAtTheRequestedSpeed()
        {
            var track = new LateralTargetTrack(2f, 4f, 0f);

            // 방향이 바뀌지 않는 구간에서 두 시각의 차이는 속도 * 시간이다.
            float travelled = track.OffsetAt(0.4f) - track.OffsetAt(0.1f);
            Assert.That(travelled, Is.EqualTo(4f * 0.3f).Within(Tolerance));
            Assert.That(track.DirectionAt(0.1f), Is.EqualTo(1f));
            Assert.That(track.DirectionAt(0.9f), Is.EqualTo(-1f));
        }

        [Test]
        public void Track_PhaseSeparatesTargetsThatShareTheSameSpeed()
        {
            var first = new LateralTargetTrack(2f, 4f, 0f);
            var second = new LateralTargetTrack(2f, 4f, 1f / 3f);
            var third = new LateralTargetTrack(2f, 4f, 2f / 3f);

            Assert.That(second.OffsetAt(0f), Is.Not.EqualTo(first.OffsetAt(0f)).Within(0.05f));
            Assert.That(third.OffsetAt(0f), Is.Not.EqualTo(first.OffsetAt(0f)).Within(0.05f));
            Assert.That(third.OffsetAt(0f), Is.Not.EqualTo(second.OffsetAt(0f)).Within(0.05f));
        }

        [Test]
        public void Track_PhaseWrapsAndIsPeriodic()
        {
            var wrapped = new LateralTargetTrack(2f, 4f, 3.25f);
            var direct = new LateralTargetTrack(2f, 4f, 0.25f);

            Assert.That(wrapped.Phase, Is.EqualTo(0.25f).Within(Tolerance));
            Assert.That(wrapped.OffsetAt(0.31f), Is.EqualTo(direct.OffsetAt(0.31f)).Within(Tolerance));
            Assert.That(direct.OffsetAt(0.31f), Is.EqualTo(direct.OffsetAt(0.31f + 2f)).Within(Tolerance));
        }

        [Test]
        public void Track_DegenerateConfigurationStandsStillInsteadOfJittering()
        {
            var noTravel = new LateralTargetTrack(0f, 4f, 0f);
            var noSpeed = new LateralTargetTrack(2f, 0f, 0f);
            var negative = new LateralTargetTrack(-2f, -4f, 0f);
            var invalid = new LateralTargetTrack(float.NaN, float.PositiveInfinity, float.NaN);

            foreach (LateralTargetTrack track in new[] { noTravel, noSpeed, negative, invalid })
            {
                Assert.That(track.IsMoving, Is.False);
                Assert.That(track.CycleSeconds, Is.EqualTo(0f));
                Assert.That(track.OffsetAt(1.23f), Is.EqualTo(0f));
                Assert.That(track.DirectionAt(1.23f), Is.EqualTo(0f));
            }
        }

        [Test]
        public void Track_IgnoresNonFiniteElapsedTime()
        {
            var track = new LateralTargetTrack(2f, 4f, 0f);

            Assert.That(track.OffsetAt(float.NaN), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(track.OffsetAt(float.NegativeInfinity), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(track.OffsetAt(-5f), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Layout_SpreadsLanesEvenlyAndSymmetrically()
        {
            // 표적 폭 1.6 m 는 실제 카트(CarriageDeck)의 전체 폭이다. 과녁면만 재면 1.1 m 지만,
            // 좌우로 휩쓰는 것은 카트 전체이므로 넓은 쪽을 넣어야 겹침 판정이 실제와 맞는다.
            var layout = new TrainingRangeLaneLayout(-43f, -28f, 3, 1.6f, 1.5f);

            Assert.That(layout.LaneSpacingMetres, Is.EqualTo(5f).Within(Tolerance));
            Assert.That(layout.LaneCentreX(0), Is.EqualTo(-40.5f).Within(Tolerance));
            Assert.That(layout.LaneCentreX(1), Is.EqualTo(-35.5f).Within(Tolerance));
            Assert.That(layout.LaneCentreX(2), Is.EqualTo(-30.5f).Within(Tolerance));
            Assert.That(layout.SweptWidthMetres, Is.EqualTo(4.6f).Within(Tolerance));
        }

        [Test]
        public void Layout_AcceptsTheFieldUsedByTheRangeBuilder()
        {
            var layout = new TrainingRangeLaneLayout(-43f, -28f, 3, 1.6f, 1.5f);

            Assert.That(layout.HasLaneOverlap, Is.False);
            Assert.That(layout.LargestOverlapMetres, Is.EqualTo(0f));
            Assert.That(layout.FitsInsideField, Is.True);
            Assert.That(layout.IsValid, Is.True);

            // 이웃 사이 0.4 m, 사격장 양끝까지 0.2 m 가 남는다.
            Assert.That(layout.LaneSpacingMetres - layout.SweptWidthMetres, Is.EqualTo(0.4f).Within(Tolerance));
            Assert.That(layout.SweptMinX(0) - layout.FieldMinX, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(layout.FieldMaxX - layout.SweptMaxX(2), Is.EqualTo(0.2f).Within(Tolerance));
        }

        [Test]
        public void Layout_DetectsTargetsThatWouldSweepThroughEachOther()
        {
            // 이것이 1차 구현에서 실제로 있었던 실수다: 과녁면 폭 1.1 m 로만 계산해 왕복 반폭을
            // 1.9 m 로 잡았지만, 실제로 움직이는 카트는 1.6 m 라 이웃 레인과 0.4 m 겹쳤다.
            var tooWide = new TrainingRangeLaneLayout(-43f, -28f, 3, 1.6f, 1.9f);

            Assert.That(tooWide.HasLaneOverlap, Is.True);
            Assert.That(tooWide.LargestOverlapMetres, Is.EqualTo(0.4f).Within(Tolerance));
            Assert.That(tooWide.IsValid, Is.False);
        }

        [Test]
        public void Layout_DetectsTargetsThatWouldLeaveTheField()
        {
            // 레인 하나면 겹칠 이웃이 아예 없다. 그래도 왕복 폭이 사격장보다 넓으면 밖으로 나간다.
            // 즉 "겹치지 않는다" 가 "쓸 수 있다" 를 뜻하지 않는다는 것이 이 테스트의 요점이다.
            var overshooting = new TrainingRangeLaneLayout(-43f, -28f, 1, 1.6f, 8f);

            Assert.That(overshooting.HasLaneOverlap, Is.False, "이웃 레인이 없으면 겹침도 없다.");
            Assert.That(overshooting.LargestOverlapMetres, Is.EqualTo(0f));
            Assert.That(overshooting.FitsInsideField, Is.False);
            Assert.That(overshooting.IsValid, Is.False);
        }

        [Test]
        public void Layout_NormalisesReversedAndDegenerateInput()
        {
            var reversed = new TrainingRangeLaneLayout(-28f, -43f, 3, 1.6f, 1.5f);
            var empty = new TrainingRangeLaneLayout(-43f, -28f, 0, 1.6f, 1.5f);
            var negative = new TrainingRangeLaneLayout(-43f, -28f, 3, -1.6f, -1.5f);

            Assert.That(reversed.FieldMinX, Is.EqualTo(-43f).Within(Tolerance));
            Assert.That(reversed.FieldMaxX, Is.EqualTo(-28f).Within(Tolerance));
            Assert.That(reversed.LaneCentreX(0), Is.EqualTo(-40.5f).Within(Tolerance));

            Assert.That(empty.LaneCount, Is.EqualTo(0));
            Assert.That(empty.IsValid, Is.False);
            Assert.That(empty.LaneSpacingMetres, Is.EqualTo(0f));

            Assert.That(negative.TargetWidthMetres, Is.EqualTo(0f));
            Assert.That(negative.HalfTravelMetres, Is.EqualTo(0f));
            Assert.That(negative.IsValid, Is.True, "폭이 0 이면 겹치지도 벗어나지도 않는다.");
        }

        [Test]
        public void Layout_RejectsAnOutOfRangeLaneIndex()
        {
            var layout = new TrainingRangeLaneLayout(-43f, -28f, 3, 1.6f, 1.5f);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => layout.LaneCentreX(3));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => layout.LaneCentreX(-1));
        }
    }
}
