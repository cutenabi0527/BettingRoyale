using System;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// 기능 구역 하나 — 중심과 반경 (S-GD-66GTWA).
    ///
    /// <para>
    /// <b>높이를 보지 않는다.</b> 바닥에 그린 원이고 위아래는 무시한다. 준비 콜로세움은 평지라
    /// 지금은 차이가 없고, 계단이 생기면 그때 다시 정한다 — <b>지금 3차원으로 만들어두면
    /// 검증할 수 없는 규칙이 하나 늘 뿐이다.</b>
    /// </para>
    ///
    /// <para>
    /// <b>경계는 포함이다.</b> 정확히 반경만큼 떨어진 자리는 구역 안이다. 어느 쪽으로 정하든
    /// 한쪽을 골라야 하는데, 포함 쪽이 "가장자리에서 안 눌린다" 는 신고를 덜 만든다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public readonly struct PreparationFacilityArea
    {
        public readonly PreparationFacility Facility;

        /// <summary>구역 중심. <b>Y 는 쓰지 않는다</b> — 바닥 평면의 원이다.</summary>
        public readonly WorldPosition Center;

        public readonly float Radius;

        public PreparationFacilityArea(PreparationFacility facility, WorldPosition center, float radius)
        {
            if (facility == PreparationFacility.None)
            {
                // None 은 "구역 없음" 을 뜻하는 값이다. 그것에 반경을 주면 맵 어딘가가
                // "아무 구역도 아닌 구역" 이 되어 판정이 뒤집힌다.
                throw new ArgumentOutOfRangeException(
                    nameof(facility), facility, "None 은 구역이 아닙니다.");
            }

            if (!(radius > 0f))
            {
                // NaN 도 여기서 걸린다 — NaN 은 어떤 비교에도 false 라 조용히 "아무도 못 들어가는 구역" 이 된다.
                throw new ArgumentOutOfRangeException(
                    nameof(radius), radius, "구역 반경은 0 보다 커야 합니다.");
            }

            Facility = facility;
            Center = center;
            Radius = radius;
        }

        /// <summary>이 좌표가 구역 안인가. 바닥 평면(XZ)에서만 잰다.</summary>
        public bool Contains(in WorldPosition position) =>
            SquaredPlanarDistanceTo(position) <= Radius * Radius;

        /// <summary>
        /// 중심까지의 거리 제곱. <b>제곱근을 쓰지 않는 이유</b>는 매 프레임 도는 판정이라
        /// 굳이 정확한 거리를 만들 필요가 없고, 비교만으로 같은 답이 나오기 때문이다.
        /// </summary>
        public float SquaredPlanarDistanceTo(in WorldPosition position)
        {
            float dx = position.X - Center.X;
            float dz = position.Z - Center.Z;
            return (dx * dx) + (dz * dz);
        }

        public override string ToString() => $"{Facility} @{Center} r{Radius:F1}";
    }
}
