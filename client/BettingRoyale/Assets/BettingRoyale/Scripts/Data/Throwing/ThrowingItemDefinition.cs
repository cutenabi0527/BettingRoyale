namespace BettingRoyale.Data.Throwing
{
    /// <summary>투척 가이드가 관찰자에게 보이는 시각 상태다.</summary>
    public enum ThrowGuideVisualState
    {
        None = 0,
        PreThrow = 1,
        InFlight = 2,
        Landed = 3,
        Activated = 4,
    }

    /// <summary>착탄 뒤 재생할 데이터 기반 발동 연출 종류.</summary>
    public enum ThrowActivationVfx
    {
        None = 0,
        MagicExplosion = 1,
        Smoke = 2,
    }

    /// <summary>투척 궤적 가이드의 관찰자 범위. 착탄·발동 효과는 별도 공개 규칙을 따른다.</summary>
    public enum ThrowVisibilityPolicy
    {
        OwnerAndAllies = 0,
        Everyone = 1,
    }

    /// <summary>
    /// UnityEngine 의존성을 갖지 않는 투척 시각 색상 값. 네트워크·순수 도메인 정의가 같은
    /// 색상 계약을 소비하고, Unity 어댑터가 0~1 색으로 변환한다.
    /// </summary>
    public readonly struct ThrowGuideColor
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public ThrowGuideColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public bool IsValid => A > 0;

        public static ThrowGuideColor DefaultGuide => new(48, 170, 255);

        public static ThrowGuideColor DefaultFlight => new(255, 64, 64);

        public static ThrowGuideColor DefaultImpact => new(255, 96, 32);
    }

    /// <summary>
    /// 투척 소비품 한 종류의 불변 정의. 수치는 초기 튜닝 값이며 확정 밸런스가 아니다 (S-GD-78REEH).
    /// </summary>
    /// <remarks>
    /// 시간 값은 모두 초 단위이며 도메인 밖에서 공급되는 절대 시각과 더해진다.
    /// 궤적·충돌·시야 판정은 Unity 어댑터의 책임이고 이 정의에는 물리 구현이 없다.
    /// </remarks>
    public readonly struct ThrowingItemDefinition
    {
        public readonly int ItemId;

        /// <summary>표시 이름. 초기 이름이며 확정 명칭이 아니다.</summary>
        public readonly string DisplayName;

        /// <summary>조준 가능한 최대 투척 거리(m).</summary>
        public readonly float ThrowDistanceMeters;

        /// <summary>폭발 중심에서 적용되는 피해량. 거리 감쇠는 코어 정의에 없다.</summary>
        public readonly int Damage;

        /// <summary>폭발 반경(m).</summary>
        public readonly float ExplosionRadiusMeters;

        /// <summary>상점 가격.</summary>
        public readonly int Price;

        /// <summary>월드 드롭 1개가 담는 수량.</summary>
        public readonly int DropQuantity;

        /// <summary>소비품 슬롯 3칸 전체 합계 보유 상한.</summary>
        public readonly int CarryLimit;

        /// <summary>투척체가 날아가는 표준 비행 시간(초). 실제 착지 시각은 어댑터가 보고한다.</summary>
        public readonly double FlightSeconds;

        /// <summary>착지(또는 사망 드롭) 이후 폭발까지의 경고 시간(초).</summary>
        public readonly double WarningSeconds;

        /// <summary>모든 투척 항목이 공유하는 사용 쿨다운(초).</summary>
        public readonly double SharedCooldownSeconds;

        /// <summary>투척 전 예측 궤적 색. 노션 컨셉의 파란 점선에 사용한다.</summary>
        public readonly ThrowGuideColor GuideColor;

        /// <summary>투척 직후 실제 비행 궤적 색. 노션 컨셉의 빨간 실선에 사용한다.</summary>
        public readonly ThrowGuideColor FlightGuideColor;

        /// <summary>착탄 위험 반경 오버레이 색.</summary>
        public readonly ThrowGuideColor ImpactOverlayColor;

        /// <summary>착탄 뒤 재생할 종류별 VFX.</summary>
        public readonly ThrowActivationVfx ActivationVFX;

        /// <summary>착탄부터 발동 VFX 시작까지의 지연(초).</summary>
        public readonly double ActivationDelay;

        /// <summary>발동 VFX의 표시 수명(초).</summary>
        public readonly double ActivationDuration;

        /// <summary>비행 중 가이드의 기본 관찰자 범위.</summary>
        public readonly ThrowVisibilityPolicy VisibilityPolicy;

        public ThrowingItemDefinition(
            int itemId,
            string displayName,
            float throwDistanceMeters,
            int damage,
            float explosionRadiusMeters,
            int price,
            int dropQuantity,
            int carryLimit,
            double flightSeconds,
            double warningSeconds,
            double sharedCooldownSeconds)
            : this(
                itemId,
                displayName,
                throwDistanceMeters,
                damage,
                explosionRadiusMeters,
                price,
                dropQuantity,
                carryLimit,
                flightSeconds,
                warningSeconds,
                sharedCooldownSeconds,
                ThrowGuideColor.DefaultGuide,
                ThrowGuideColor.DefaultFlight,
                ThrowGuideColor.DefaultImpact,
                ThrowActivationVfx.MagicExplosion,
                warningSeconds,
                2d,
                ThrowVisibilityPolicy.OwnerAndAllies)
        {
        }

        public ThrowingItemDefinition(
            int itemId,
            string displayName,
            float throwDistanceMeters,
            int damage,
            float explosionRadiusMeters,
            int price,
            int dropQuantity,
            int carryLimit,
            double flightSeconds,
            double warningSeconds,
            double sharedCooldownSeconds,
            ThrowGuideColor guideColor,
            ThrowGuideColor flightGuideColor,
            ThrowGuideColor impactOverlayColor,
            ThrowActivationVfx activationVFX,
            double activationDelay,
            double activationDuration,
            ThrowVisibilityPolicy visibilityPolicy)
        {
            ItemId = itemId;
            DisplayName = displayName;
            ThrowDistanceMeters = throwDistanceMeters;
            Damage = damage;
            ExplosionRadiusMeters = explosionRadiusMeters;
            Price = price;
            DropQuantity = dropQuantity;
            CarryLimit = carryLimit;
            FlightSeconds = flightSeconds;
            WarningSeconds = warningSeconds;
            SharedCooldownSeconds = sharedCooldownSeconds;
            GuideColor = guideColor;
            FlightGuideColor = flightGuideColor;
            ImpactOverlayColor = impactOverlayColor;
            ActivationVFX = activationVFX;
            ActivationDelay = activationDelay;
            ActivationDuration = activationDuration;
            VisibilityPolicy = visibilityPolicy;
        }

        /// <summary>
        /// default(구조체), NaN, 무한대, 음수 수치는 모두 무효다.
        /// 사용 승인과 투척체 생성은 무효 정의를 거부한다.
        /// </summary>
        public bool IsValid =>
            ItemId > 0 &&
            !string.IsNullOrEmpty(DisplayName) &&
            IsFinitePositive(ThrowDistanceMeters) &&
            Damage > 0 &&
            IsFinitePositive(ExplosionRadiusMeters) &&
            Price >= 0 &&
            DropQuantity > 0 &&
            CarryLimit > 0 &&
            IsFinitePositive(FlightSeconds) &&
            IsFinitePositive(WarningSeconds) &&
            IsFiniteNonNegative(SharedCooldownSeconds) &&
            GuideColor.IsValid &&
            FlightGuideColor.IsValid &&
            ImpactOverlayColor.IsValid &&
            ActivationVFX is ThrowActivationVfx.MagicExplosion or ThrowActivationVfx.Smoke &&
            IsFiniteNonNegative(ActivationDelay) &&
            IsFinitePositive(ActivationDuration) &&
            VisibilityPolicy is ThrowVisibilityPolicy.OwnerAndAllies or ThrowVisibilityPolicy.Everyone;

        /// <summary>NaN·무한대가 아닌 유한 시각인지 검사한다.</summary>
        public static bool IsFiniteTime(double seconds) => !double.IsNaN(seconds) && !double.IsInfinity(seconds);

        private static bool IsFinitePositive(double value) => IsFiniteTime(value) && value > 0d;

        private static bool IsFiniteNonNegative(double value) => IsFiniteTime(value) && value >= 0d;

        private static bool IsFinitePositive(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }
}
