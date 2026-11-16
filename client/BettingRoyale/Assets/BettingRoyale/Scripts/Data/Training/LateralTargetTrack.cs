using System;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// 사격 방향에 직교하는 좌우 왕복 궤도의 순수 계산이다 (S-GD-VWW8YK).
    /// 중심에서 출발해 +쪽 끝 → -쪽 끝 → 중심으로 돌아오는 등속 삼각파이며,
    /// 실제 수치(이동 폭·속도·위상)는 아직 확정 스펙이 아니다 (S-GD-8Q3ZN9).
    ///
    /// <para>
    /// ⚠️ <b>UnityEngine 을 참조하지 않는다</b> (D-060). 이동을 여기에서만 계산하고
    /// Gameplay 어댑터가 그 결과를 Transform 과 콜라이더에 함께 적용하기 때문에,
    /// "보이는 것만 움직이고 판정은 그대로" 인 상태를 코드 구조로 만들 수 없다.
    /// </para>
    /// </summary>
    public readonly struct LateralTargetTrack
    {
        /// <summary>이보다 짧은 이동 폭은 정지로 취급한다. 미터.</summary>
        public const float MinimumHalfTravelMetres = 0.05f;

        /// <summary>이보다 느린 속도는 정지로 취급한다. 초당 미터.</summary>
        public const float MinimumSpeedMetresPerSecond = 0.01f;

        private readonly float halfTravelMetres;
        private readonly float speedMetresPerSecond;
        private readonly float phase;

        /// <param name="halfTravelMetres">중심에서 한쪽 끝까지의 거리. 전체 왕복 폭은 이 값의 2배다.</param>
        /// <param name="speedMetresPerSecond">등속 이동 속도.</param>
        /// <param name="phase">0~1 로 감싸는 주기 내 시작 위상. 표적마다 다르게 주어 동시에 움직이지 않게 한다.</param>
        public LateralTargetTrack(float halfTravelMetres, float speedMetresPerSecond, float phase)
        {
            this.halfTravelMetres = Sanitize(halfTravelMetres);
            this.speedMetresPerSecond = Sanitize(speedMetresPerSecond);
            this.phase = Wrap01(phase);
        }

        public float HalfTravelMetres => halfTravelMetres;
        public float SpeedMetresPerSecond => speedMetresPerSecond;
        public float Phase => phase;

        /// <summary>실제로 움직이는 궤도인가. 정지 궤도는 항상 오프셋 0 을 돌려준다.</summary>
        public bool IsMoving =>
            halfTravelMetres >= MinimumHalfTravelMetres && speedMetresPerSecond >= MinimumSpeedMetresPerSecond;

        /// <summary>한 번의 완전한 왕복에 걸리는 시간. 정지 궤도는 0.</summary>
        public float CycleSeconds => IsMoving ? 4f * halfTravelMetres / speedMetresPerSecond : 0f;

        /// <summary>왕복 구간의 총 폭.</summary>
        public float TravelWidthMetres => IsMoving ? 2f * halfTravelMetres : 0f;

        /// <summary>경과 시간에서의 중심 기준 좌우 오프셋. 항상 [-HalfTravel, +HalfTravel] 안이다.</summary>
        public float OffsetAt(float elapsedSeconds)
        {
            if (!IsMoving)
            {
                return 0f;
            }

            float quarters = NormalizedQuarters(elapsedSeconds);
            if (quarters < 1f)
            {
                return halfTravelMetres * quarters;
            }
            if (quarters < 3f)
            {
                return halfTravelMetres * (2f - quarters);
            }

            return halfTravelMetres * (quarters - 4f);
        }

        /// <summary>경과 시간에서의 진행 방향. 정지 궤도는 0, 그 외에는 +1 또는 -1.</summary>
        public float DirectionAt(float elapsedSeconds)
        {
            if (!IsMoving)
            {
                return 0f;
            }

            float quarters = NormalizedQuarters(elapsedSeconds);
            return quarters < 1f || quarters >= 3f ? 1f : -1f;
        }

        /// <summary>주기 안의 위치를 0~4 사분면 단위로 돌려준다.</summary>
        private float NormalizedQuarters(float elapsedSeconds)
        {
            float seconds = Sanitize(elapsedSeconds);
            float cycle = CycleSeconds;
            return Wrap01(seconds / cycle + phase) * 4f;
        }

        /// <summary>음수·NaN·무한대는 궤도 값으로 취급하지 않는다.</summary>
        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                return 0f;
            }

            return value;
        }

        private static float Wrap01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            double wrapped = value - Math.Floor((double)value);
            return wrapped >= 1d ? 0f : (float)wrapped;
        }
    }
}
