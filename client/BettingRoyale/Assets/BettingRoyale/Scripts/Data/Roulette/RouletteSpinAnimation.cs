using System;

namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// <b>회전판이 적중 칸에서 멈추는 표현의 계산</b> (S-GD-49WM7S, S-CLI-B2TGQ8).
    ///
    /// <para>
    /// <b>⚠️ 회전은 결과를 정하지 않는다.</b> 적중 칸은 회전이 시작될 때 이미
    /// <see cref="RouletteTable"/> 이 뽑아 두었고(<see cref="RouletteTable.WinningSlot"/>), 여기서 하는 일은
    /// <b>이미 정해진 칸으로 가는 각도를 만드는 것</b>뿐이다. 이 파일이 결과를 바꾸는 일은 없다.
    /// </para>
    ///
    /// <para>
    /// <b>각도 규약</b> — 포인터는 회전판 <b>상단 0°</b> 에 고정돼 있고 회전하지 않는다
    /// (S-CLI-B2TGQ8). 칸 번호는 상단에서 <b>시계 방향</b>으로 증가하므로, 회전각 0 일 때 칸
    /// <c>k</c> 는 상단에서 시계 방향 <c>k × (360/칸수)</c> 에 있다. 25칸이면 한 칸 <b>14.4°</b> 다.
    /// 따라서 칸 <c>k</c> 를 포인터 아래로 데려오려면 <b>반시계로 <c>k × 14.4°</c></b> 만큼,
    /// 즉 이 파일의 부호 규약(시계 방향이 +)으로 <c>-k × 14.4°</c> 만큼 돌려야 한다.
    /// </para>
    ///
    /// <para>
    /// <b>화면이 멈춘 자리와 정산이 어긋날 수 없다.</b> 칸 배치가 곧 확률표이고
    /// (<see cref="PlaceholderRouletteWheel"/>) 그 배열 순서를 3D 에셋과 공유하기 때문이다.
    /// <see cref="SlotUnderPointer"/> 는 그 역함수라 테스트가 왕복을 그대로 확인한다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>가속·감속 곡선과 바퀴 수는 확정 스펙이 없다.</b> 아래 기본값은 구현자가 정한
    /// 무난한 값이며, 바꿔도 <b>멈추는 칸은 달라지지 않는다</b> — 곡선은 도착점이 아니라
    /// 가는 길만 정한다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class RouletteSpinAnimation
    {
        /// <summary>
        /// 전체 회전 시간 중 <b>가속에 쓰는 비율</b>. ⚠️ 임의값 — 확정 스펙 없음.
        /// 나머지 구간은 감속이다.
        /// </summary>
        public const float DefaultAccelerationFraction = 0.18f;

        /// <summary>
        /// 감속 곡선의 세기. 클수록 <b>끝에서 더 천천히</b> 들어온다. ⚠️ 임의값 — 확정 스펙 없음.
        /// </summary>
        public const int DefaultDecelerationExponent = 4;

        /// <summary>
        /// 초당 바퀴 수. 총 바퀴 수를 <b>고정값이 아니라 속도로</b> 두는 이유는, 회전 시간이
        /// 바뀌거나 <b>늦게 들어와 남은 시간만 아는 피어</b>에서도 같은 인상이 나오게 하기 위해서다.
        /// ⚠️ 임의값 — 확정 스펙 없음.
        /// </summary>
        public const float DefaultRevolutionsPerSecond = 0.9f;

        /// <summary>한 칸이 차지하는 각도. 25칸이면 14.4° 다.</summary>
        public static float SlotAngleDegrees(int slotCount)
        {
            RequireSlotCount(slotCount);
            return 360f / slotCount;
        }

        /// <summary>
        /// 칸 <paramref name="winningSlot"/> 이 <b>상단 포인터 아래에 오는 회전각</b>. 항상 <c>[0, 360)</c> 이다.
        /// </summary>
        public static float RestAngleDegrees(int winningSlot, int slotCount)
        {
            RequireSlot(winningSlot, slotCount);
            return Normalize(-winningSlot * SlotAngleDegrees(slotCount));
        }

        /// <summary>
        /// 이 회전각에서 <b>포인터가 가리키는 칸</b>. <see cref="RestAngleDegrees"/> 의 역함수다.
        ///
        /// <para>
        /// <b>테스트가 이 함수로 도착점을 확인한다.</b> 각도만 보고 "그럴듯하다" 고 넘어가면
        /// 부호 하나가 뒤집혀도 통과한다 — 그 경우 화면은 <b>거울상 칸</b>에 멈춘다.
        /// </para>
        /// </summary>
        public static int SlotUnderPointer(float angleDegrees, int slotCount)
        {
            RequireSlotCount(slotCount);

            // 반올림은 Math.Round 를 쓰지 않는다 — 기본이 은행가 반올림이라 정확히 반 칸일 때
            // 짝수 칸으로 쏠린다. 경계는 임의로 정해도 되지만 한 방향으로 일관돼야 한다.
            double steps = Math.Floor((-angleDegrees / SlotAngleDegrees(slotCount)) + 0.5d);
            int slot = (int)(steps % slotCount);
            return slot < 0 ? slot + slotCount : slot;
        }

        /// <summary>각도를 <c>[0, 360)</c> 으로 접는다.</summary>
        public static float Normalize(float degrees)
        {
            float folded = degrees % 360f;
            return folded < 0f ? folded + 360f : folded;
        }

        /// <summary>
        /// 회전 시간에서 <b>총 바퀴 수</b>를 구한다. <b>최소 한 바퀴</b>는 돈다 —
        /// 0 바퀴면 회전이 아니라 칸 하나 만큼 툭 움직이는 것으로 보인다.
        /// </summary>
        public static int TurnsFor(float durationSeconds, float revolutionsPerSecond)
        {
            if (durationSeconds <= 0f || revolutionsPerSecond <= 0f)
            {
                return 1;
            }

            int turns = (int)Math.Floor((durationSeconds * revolutionsPerSecond) + 0.5d);
            return turns < 1 ? 1 : turns;
        }

        /// <summary>
        /// 진행도 <paramref name="progress"/>(0~1) 에서의 <b>가속·감속 보간값</b>(0~1).
        ///
        /// <para>
        /// 앞 <paramref name="accelerationFraction"/> 구간은 2차 가속, 나머지는 지수
        /// <paramref name="decelerationExponent"/> 의 감속이다. <b>이음매에서 속도가 이어지도록</b>
        /// 가속 구간이 가져갈 몫을 계산해서 쓴다 — 그러지 않으면 가속이 끝나는 순간
        /// 바퀴가 눈에 띄게 덜컥한다.
        /// </para>
        ///
        /// <para><b>끝에서 속도가 0 이 된다</b> — 그래야 적중 칸에 미끄러지듯 멈춘다.</para>
        /// </summary>
        public static float Ease(float progress, float accelerationFraction, int decelerationExponent)
        {
            float p = Clamp01(progress);
            float a = accelerationFraction;
            int n = decelerationExponent < 1 ? 1 : decelerationExponent;

            if (a <= 0f)
            {
                a = 0f;
            }
            else if (a > 0.9f)
            {
                // 감속 구간이 남아 있어야 "멈춤" 이 보인다.
                a = 0.9f;
            }

            // 가속 구간이 가져가는 거리 비율. 이음매에서 속도가 같아지는 값이다:
            //   가속 끝 속도 2·k/a  ==  감속 시작 속도 (1-k)·n/(1-a)
            float k = a <= 0f ? 0f : (n * a) / ((2f * (1f - a)) + (n * a));

            if (p < a)
            {
                float rise = p / a;
                return k * rise * rise;
            }

            float fall = a >= 1f ? 1f : (p - a) / (1f - a);
            return k + ((1f - k) * (1f - Pow(1f - fall, n)));
        }

        /// <summary>
        /// 진행도 <paramref name="progress"/> 에서의 <b>회전각</b>(시계 방향 +, 누적값이라 360 을 넘는다).
        ///
        /// <para>
        /// <paramref name="progress"/> 가 1 이면 <see cref="RestAngleDegrees"/> 와
        /// <b>360의 배수 차이</b>다 — 즉 적중 칸이 정확히 포인터 아래에 온다.
        /// </para>
        /// </summary>
        /// <param name="startAngleDegrees">회전을 시작할 때의 각도. 지난 판이 멈춘 자리다.</param>
        /// <param name="turns">도착 전에 더 도는 바퀴 수. <see cref="TurnsFor"/> 가 준다.</param>
        public static float AngleDegreesAt(
            float progress,
            float startAngleDegrees,
            int winningSlot,
            int slotCount,
            int turns,
            float accelerationFraction,
            int decelerationExponent)
        {
            RequireSlot(winningSlot, slotCount);

            if (turns < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(turns), turns, "바퀴 수는 음수일 수 없습니다.");
            }

            float rest = RestAngleDegrees(winningSlot, slotCount);

            // 남은 거리는 항상 앞으로(시계 방향) 간다. 뒤로 돌아가면 포인터를 역주행한다.
            float delta = Normalize(rest - startAngleDegrees);
            float sweep = (turns * 360f) + delta;

            return startAngleDegrees + (sweep * Ease(progress, accelerationFraction, decelerationExponent));
        }

        private static float Pow(float value, int exponent)
        {
            float result = 1f;

            for (int i = 0; i < exponent; i++)
            {
                result *= value;
            }

            return result;
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f || float.IsNaN(value))
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }

        private static void RequireSlotCount(int slotCount)
        {
            if (slotCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slotCount), slotCount, "바퀴에 칸이 하나도 없습니다.");
            }
        }

        private static void RequireSlot(int slot, int slotCount)
        {
            RequireSlotCount(slotCount);

            if (slot < 0 || slot >= slotCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot), slot, $"칸 번호가 범위를 벗어났습니다. 칸 수 {slotCount}.");
            }
        }
    }
}
