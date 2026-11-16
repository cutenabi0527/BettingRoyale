using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// 준비 콜로세움의 기능 구역 전부 (S-GD-66GTWA).
    ///
    /// <para>
    /// <b>구역끼리 겹치면 만들 때 던진다.</b> 겹치면 한 걸음 옮길 때마다 상점이 닫히고 룰렛이
    /// 열리는 자리가 생기고, 그 경계는 사람이 예측할 수 없다. 겹치지 않게 두는 것이 규칙이고
    /// 이 생성자가 그 규칙을 강제한다 — <b>수치가 데이터로 들어오는 이상(D-041) 값 하나만 잘못
    /// 넣어도 겹칠 수 있다.</b>
    /// </para>
    ///
    /// <para>
    /// 겹치지 않으므로 <see cref="Resolve"/> 는 "가장 가까운 것" 을 고를 필요가 없다 —
    /// 어느 좌표든 들어가는 구역은 많아야 하나다. 그래도 <b>가장 가까운 것을 고르게 짜 뒀다</b>:
    /// 나중에 겹침을 허용하기로 하면 이 함수는 그대로 두고 생성자 검사만 풀면 된다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class PreparationFacilityMap
    {
        private readonly PreparationFacilityArea[] areas;

        public PreparationFacilityMap(IReadOnlyList<PreparationFacilityArea> areas)
            : this(areas, allowOverlap: false)
        {
        }

        /// <param name="allowOverlap">
        /// 구역이 겹치는 것을 허용하는가 (constants.FACILITY_ALLOW_OVERLAP, S-GD-3DEQ2M 룰렛 8m).
        /// 허용하면 겹친 자리에서는 <see cref="Resolve"/> 가 <b>중심이 가까운 시설</b>을 고른다 — 같은 좌표에서는
        /// 늘 같은 답이라 판정은 결정적이고, 안내(UI)와 Host 승인이 같은 함수를 쓰므로 어긋나지 않는다.
        /// 같은 시설이 두 번 들어오는 것은 여전히 거부한다.
        /// </param>
        public PreparationFacilityMap(IReadOnlyList<PreparationFacilityArea> areas, bool allowOverlap)
        {
            if (areas == null)
            {
                throw new ArgumentNullException(nameof(areas));
            }

            AllowOverlap = allowOverlap;

            this.areas = new PreparationFacilityArea[areas.Count];

            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];

                for (int j = 0; j < i; j++)
                {
                    var other = this.areas[j];

                    if (other.Facility == area.Facility)
                    {
                        throw new ArgumentException(
                            $"같은 구역이 두 번 들어 있습니다: {area.Facility}.", nameof(areas));
                    }

                    if (allowOverlap)
                    {
                        continue;
                    }

                    float reach = area.Radius + other.Radius;

                    // ⚠️ 딱 맞닿는 것(거리 == 반경 합)도 겹침으로 본다.
                    //    Contains 가 경계를 포함하므로, 두 원이 한 점에서 만나면 그 점에 선 사람은
                    //    두 구역 안이다. 판정은 결정적이지만 "구역은 겹치지 않는다" 는 규칙이 깨진다.
                    if (other.SquaredPlanarDistanceTo(area.Center) <= reach * reach)
                    {
                        throw new ArgumentException(
                            $"구역이 겹칩니다: {area} 와 {other}. " +
                            "겹치면 경계에서 어느 시설이 열릴지 사람이 예측할 수 없습니다.",
                            nameof(areas));
                    }
                }

                this.areas[i] = area;
            }
        }

        public IReadOnlyList<PreparationFacilityArea> Areas => areas;

        /// <summary>겹침 허용 정책. 겹친 자리는 중심이 가까운 시설이 이긴다.</summary>
        public bool AllowOverlap { get; }

        public int Count => areas.Length;

        /// <summary>
        /// 이 좌표에서 쓸 수 있는 시설. 어느 구역에도 없으면 <see cref="PreparationFacility.None"/>.
        /// </summary>
        public PreparationFacility Resolve(in WorldPosition position)
        {
            var nearest = PreparationFacility.None;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < areas.Length; i++)
            {
                var area = areas[i];

                if (!area.Contains(position))
                {
                    continue;
                }

                float distance = area.SquaredPlanarDistanceTo(position);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = area.Facility;
                }
            }

            return nearest;
        }

        public bool TryGet(PreparationFacility facility, out PreparationFacilityArea area)
        {
            for (int i = 0; i < areas.Length; i++)
            {
                if (areas[i].Facility == facility)
                {
                    area = areas[i];
                    return true;
                }
            }

            area = default;
            return false;
        }

        public override string ToString() => $"기능 구역 {areas.Length}곳";
    }
}
