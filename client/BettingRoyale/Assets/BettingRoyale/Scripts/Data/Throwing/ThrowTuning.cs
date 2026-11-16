namespace BettingRoyale.Data.Throwing
{
    /// <summary>
    /// 90_CombatLab 밸런스 실측용 투척 <b>조정 레이어</b> (S15P21A606-192).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>비어 있는 것이 정상 상태</b>다. 조정이 없으면 <see cref="Apply"/> 는 받은 정의를 그대로 돌려주므로
    /// 제품 경로 동작은 달라지지 않는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>마력 폭발병에는 "탄속"(m/s) 이라는 값이 없다.</b> 비행 시간이 거리에 따라 정해지고
    /// (<see cref="ThrowFlightTiming"/>: 최소 0.2초 ~ 최대 <c>FlightSeconds</c> 를 거리 비율로 보간),
    /// 최대 사거리에서 약 1초다 (S-GD-JFG6XM). 따라서 탄속을 빠르게 하려면 <see cref="MaxFlightSeconds"/> 를
    /// 줄인다. 최소 0.2초 바닥은 <see cref="ThrowFlightTiming"/> 안에 있어 이 레이어가 건드리지 않는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>저장되지 않는다.</b> 실측으로 찾은 값은 사람이 <c>RecommendedMagicBottleCatalog</c> 상수에
    /// 옮겨 적어야 한다.
    /// </para>
    /// </remarks>
    public static class ThrowTuning
    {
        /// <summary>최대 사거리에서의 비행 시간(초). 0 이하이면 조정하지 않는다.</summary>
        public static double MaxFlightSeconds { get; private set; }

        /// <summary>착지 후 폭발까지의 경고 시간(초). 0 이하이면 조정하지 않는다.</summary>
        public static double WarningSeconds { get; private set; }

        public static bool HasFlight => IsFinitePositive(MaxFlightSeconds);
        public static bool HasWarning => IsFinitePositive(WarningSeconds);
        public static bool HasOverrides => HasFlight || HasWarning;

        public static void SetMaxFlightSeconds(double seconds) =>
            MaxFlightSeconds = IsFinitePositive(seconds) ? seconds : 0d;

        public static void SetWarningSeconds(double seconds) =>
            WarningSeconds = IsFinitePositive(seconds) ? seconds : 0d;

        public static void Clear()
        {
            MaxFlightSeconds = 0d;
            WarningSeconds = 0d;
        }

        /// <summary>
        /// 조정값을 얹은 정의를 돌려준다. 조정이 없으면 <paramref name="definition"/> 을 그대로 돌려준다.
        /// </summary>
        /// <remarks>
        /// 착탄부터 발동 VFX 까지의 지연(<c>ActivationDelay</c>)은 경고 시간을 따라간다 —
        /// <c>RecommendedMagicBottleCatalog</c> 가 두 값을 같게 두고 만들기 때문에, 경고만 바꾸고
        /// 지연을 두면 폭발 연출이 경고보다 먼저 또는 나중에 뜬다.
        /// </remarks>
        public static ThrowingItemDefinition Apply(in ThrowingItemDefinition definition)
        {
            if (!HasOverrides)
            {
                return definition;
            }

            double flight = HasFlight ? MaxFlightSeconds : definition.FlightSeconds;
            double warning = HasWarning ? WarningSeconds : definition.WarningSeconds;
            double activationDelay = HasWarning ? WarningSeconds : definition.ActivationDelay;

            return new ThrowingItemDefinition(
                definition.ItemId,
                definition.DisplayName,
                definition.ThrowDistanceMeters,
                definition.Damage,
                definition.ExplosionRadiusMeters,
                definition.Price,
                definition.DropQuantity,
                definition.CarryLimit,
                flight,
                warning,
                definition.SharedCooldownSeconds,
                definition.GuideColor,
                definition.FlightGuideColor,
                definition.ImpactOverlayColor,
                definition.ActivationVFX,
                activationDelay,
                definition.ActivationDuration,
                definition.VisibilityPolicy);
        }

        private static bool IsFinitePositive(double value) =>
            value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
