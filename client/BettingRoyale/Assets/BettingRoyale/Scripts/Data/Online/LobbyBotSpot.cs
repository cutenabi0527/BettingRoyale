using System;

namespace BettingRoyale.Data.Online
{
    /// <summary>
    /// 구역 안에서 봇마다 다른 자리(S15P21A606-326, S-GD-VH8V2G). 모두 구역 중심 한 점을 향하면 줄지어 막혀서(2026-09-25 Preview),
    /// 방향은 봇 번호마다 황금각으로 나누고 거리는 구역 반지름의 일정 비율 안에서 봇마다 흔든다.
    /// </summary>
    public static class LobbyBotSpot
    {
        /// <summary>봇 번호마다 방향을 나누는 간격(도). 번호가 이어져도 방향이 고르게 흩어진다.</summary>
        public const float GoldenAngleDegrees = 137.508f;

        /// <summary>구역마다 방향을 돌리는 양(라디안). 한 봇이 모든 시설에서 같은 쪽에만 서지 않게 한다.</summary>
        public const float AreaAngleStep = 0.9f;

        /// <summary>
        /// 시설 원에서 서는 고리(반지름 비율). 중심의 바퀴·모루·가판대를 피하고, 도착 판정 여유(<see cref="ArriveRadius"/>)를 더해도
        /// 원 안에 머물러 시설을 쓸 수 있다 — 반지름 5m 면 3.25~4m 에 서고 멀어도 4.6m 에서 멈춘다.
        /// </summary>
        public const float FacilityMin = 0.65f, FacilityMax = 0.8f;

        /// <summary>집결 구역에서 서는 범위(반지름 비율).</summary>
        public const float RallyMin = 0.25f, RallyMax = 0.7f;

        /// <summary>자기 자리에 도착했다고 보는 거리(m). 좁혀야 자리에서 멈추고 옆 봇을 밀지 않는다.</summary>
        public const float ArriveRadius = 0.6f;

        /// <summary>구역 중심에서 이 봇의 자리까지의 수평 오프셋(X, Z). <paramref name="jitter"/> 는 0~1 이며 봇마다 고정한다.</summary>
        public static (float X, float Z) Offset(int botNumber, float jitter, int areaIndex, float areaRadius,
            float minFraction, float maxFraction)
        {
            double angle = botNumber * GoldenAngleDegrees * Math.PI / 180.0 + areaIndex * AreaAngleStep;
            float t = Math.Clamp(jitter, 0f, 1f);
            float distance = Math.Max(0f, areaRadius) * (minFraction + (maxFraction - minFraction) * t);
            return ((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
        }
    }
}
