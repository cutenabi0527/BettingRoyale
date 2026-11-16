using System;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// 사격장 이동 표적 레인의 순수 배치 규칙이다 (S-GD-VWW8YK / S-GD-8Q3ZN9).
    /// 사격 방향(깊이)이 아니라 <b>사격자 기준 좌우 축</b> 하나만 다룬다. 좌우 축의 최소·최대,
    /// 표적 개수, 표적 폭, 왕복 반폭이 주어지면 레인 중심과 실제 휩쓰는 구간을 계산한다.
    ///
    /// <para>
    /// 이 계산이 따로 있는 이유는 "표적이 서로 겹쳐 지나간다", "표적이 사격장 밖으로 나간다" 를
    /// 씬을 열지 않고 검증하기 위해서다. 에디터 배치 코드와 런타임 이동이 같은 값을 쓴다.
    /// </para>
    /// </summary>
    public readonly struct TrainingRangeLaneLayout
    {
        private readonly float fieldMinX;
        private readonly float fieldMaxX;
        private readonly int laneCount;
        private readonly float targetWidthMetres;
        private readonly float halfTravelMetres;

        public TrainingRangeLaneLayout(
            float fieldMinX,
            float fieldMaxX,
            int laneCount,
            float targetWidthMetres,
            float halfTravelMetres)
        {
            float min = Sanitize(fieldMinX);
            float max = Sanitize(fieldMaxX);
            this.fieldMinX = Math.Min(min, max);
            this.fieldMaxX = Math.Max(min, max);
            this.laneCount = laneCount > 0 ? laneCount : 0;
            this.targetWidthMetres = NonNegative(targetWidthMetres);
            this.halfTravelMetres = NonNegative(halfTravelMetres);
        }

        public float FieldMinX => fieldMinX;
        public float FieldMaxX => fieldMaxX;
        public float FieldWidthMetres => fieldMaxX - fieldMinX;
        public int LaneCount => laneCount;
        public float TargetWidthMetres => targetWidthMetres;
        public float HalfTravelMetres => halfTravelMetres;

        /// <summary>레인 하나에 배정된 폭.</summary>
        public float LaneSpacingMetres => laneCount > 0 ? FieldWidthMetres / laneCount : 0f;

        /// <summary>한 표적이 실제로 차지하는 좌우 폭. 왕복 폭 + 표적 자체 폭이다.</summary>
        public float SweptWidthMetres => 2f * halfTravelMetres + targetWidthMetres;

        public float LaneCentreX(int index)
        {
            RequireLane(index);
            return fieldMinX + LaneSpacingMetres * (index + 0.5f);
        }

        public float SweptMinX(int index) => LaneCentreX(index) - halfTravelMetres - targetWidthMetres * 0.5f;

        public float SweptMaxX(int index) => LaneCentreX(index) + halfTravelMetres + targetWidthMetres * 0.5f;

        /// <summary>이웃 레인의 휩쓰는 구간이 겹치는가. 겹치면 표적끼리 서로를 통과한다.</summary>
        public bool HasLaneOverlap => laneCount > 1 && SweptWidthMetres > LaneSpacingMetres;

        /// <summary>가장 큰 겹침 폭. 겹치지 않으면 0.</summary>
        public float LargestOverlapMetres
        {
            get
            {
                if (!HasLaneOverlap)
                {
                    return 0f;
                }

                return SweptWidthMetres - LaneSpacingMetres;
            }
        }

        /// <summary>모든 표적이 왕복 끝에서도 사격장 안에 남는가.</summary>
        public bool FitsInsideField
        {
            get
            {
                if (laneCount <= 0)
                {
                    return false;
                }

                for (int index = 0; index < laneCount; index++)
                {
                    if (SweptMinX(index) < fieldMinX || SweptMaxX(index) > fieldMaxX)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>배치가 그대로 쓸 수 있는 상태인가.</summary>
        public bool IsValid => laneCount > 0 && FieldWidthMetres > 0f && !HasLaneOverlap && FitsInsideField;

        /// <summary>로그·보고에 그대로 쓰는 한 줄 요약.</summary>
        public string Describe()
        {
            return "lanes=" + laneCount
                + " field=[" + fieldMinX.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", " + fieldMaxX.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"
                + " spacing=" + LaneSpacingMetres.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " swept=" + SweptWidthMetres.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " overlap=" + LargestOverlapMetres.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void RequireLane(int index)
        {
            if (index < 0 || index >= laneCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, "레인 인덱스는 0 이상 " + laneCount + " 미만이어야 합니다.");
            }
        }

        private static float Sanitize(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

        private static float NonNegative(float value)
        {
            float sanitized = Sanitize(value);
            return sanitized < 0f ? 0f : sanitized;
        }
    }
}
