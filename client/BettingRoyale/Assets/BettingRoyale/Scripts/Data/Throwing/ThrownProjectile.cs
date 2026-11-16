namespace BettingRoyale.Data.Throwing
{
    /// <summary>
    /// 투척체 1개의 수명 규칙. 절대 시각(초)만 사용하고 프레임·틱에 의존하지 않는다.
    /// </summary>
    /// <remarks>
    /// 규칙 요약:
    /// <list type="bullet">
    /// <item>소유자와 발생 라운드는 생성 이후 절대 바뀌지 않는다. 소유자가 죽어도 그대로다.</item>
    /// <item>일반 투척: 비행 → 최초 착지 → 경고 → 폭발. 착지가 퓨즈의 기준이며,
    /// 두 번째 이후의 착지 보고는 기준 시각도 타이머도 바꾸지 못한다.</item>
    /// <item>사망 드롭: 퓨즈가 떨어뜨린 시각에서 시작하므로 낙하 중에 만료될 수 있다.
    /// 다만 폭발 통보는 착지 이후에만 나간다.</item>
    /// <item><see cref="EndRound"/> 이후에는 호출자가 다시 전투를 허용해도 피해가 영구히 꺼진다.
    /// 퓨즈와 연출 수명 자체는 계속 흐른다.</item>
    /// <item>폭발 통보는 최대 1회다.</item>
    /// </list>
    /// 궤적·충돌·시야 판정은 Unity 어댑터가 나중에 공급한다. 여기에는 물리 구현이 없다.
    /// </remarks>
    public sealed class ThrownProjectile
    {
        private readonly int ownerId;
        private readonly int originRoundNumber;
        private readonly ThrowingItemDefinition definition;
        private readonly ThrownProjectileOrigin origin;
        private readonly double createdAtSeconds;

        private bool hasLanded;
        private double landedAtSeconds;
        private bool fuseStarted;
        private double fuseStartedAtSeconds;
        private double fuseEndsAtSeconds;
        private bool exploded;
        private bool damageEnabled;
        private bool roundEnded;
        private double lastTimeSeconds;

        private ThrownProjectile(
            int ownerId,
            int originRoundNumber,
            in ThrowingItemDefinition definition,
            ThrownProjectileOrigin origin,
            double createdAtSeconds)
        {
            this.ownerId = ownerId;
            this.originRoundNumber = originRoundNumber;
            this.definition = definition;
            this.origin = origin;
            this.createdAtSeconds = createdAtSeconds;
            FlightSeconds = definition.FlightSeconds;
            lastTimeSeconds = createdAtSeconds;
            damageEnabled = true;

            if (origin == ThrownProjectileOrigin.DeathDrop)
            {
                // 사망 드롭의 퓨즈는 떨어뜨린 순간부터 흐른다.
                fuseStarted = true;
                fuseStartedAtSeconds = createdAtSeconds;
                fuseEndsAtSeconds = createdAtSeconds + definition.WarningSeconds;
            }
        }

        public int OwnerId => ownerId;
        public int OriginRoundNumber => originRoundNumber;
        public int ItemId => definition.ItemId;
        public ThrowingItemDefinition Definition => definition;
        public ThrownProjectileOrigin Origin => origin;
        public double CreatedAtSeconds => createdAtSeconds;

        public bool HasLanded => hasLanded;
        public double LandedAtSeconds => landedAtSeconds;
        public bool IsFuseStarted => fuseStarted;
        public double FuseStartedAtSeconds => fuseStartedAtSeconds;
        public double FuseEndsAtSeconds => fuseEndsAtSeconds;
        public bool HasExploded => exploded;
        public bool IsRoundEnded => roundEnded;

        /// <summary>지금 폭발한다면 피해를 줄 수 있는지. 라운드 종료 뒤에는 영구히 false 다.</summary>
        public bool CanDamage => damageEnabled && !roundEnded;

        /// <summary>표준 비행 시간 기준의 착지 예상 시각(초). 참고 값이며 실제 착지는 어댑터가 보고한다.</summary>
        public double FlightSeconds { get; private set; }
        public double ExpectedLandingTimeSeconds => createdAtSeconds + FlightSeconds;

        /// <summary>퓨즈는 만료됐지만 아직 착지하지 않아 폭발 통보를 대기 중인지.</summary>
        public bool IsAwaitingLandingToExplode =>
            !exploded && fuseStarted && !hasLanded && lastTimeSeconds >= fuseEndsAtSeconds;

        public ThrownProjectileState State
        {
            get
            {
                if (exploded)
                {
                    return ThrownProjectileState.Exploded;
                }

                return fuseStarted ? ThrownProjectileState.Warning : ThrownProjectileState.InFlight;
            }
        }

        /// <summary>승인된 투척으로 발사된 투척체를 만든다.</summary>
        public static bool TryCreateLaunched(
            int ownerId,
            int originRoundNumber,
            in ThrowingItemDefinition definition,
            double launchTimeSeconds,
            out ThrownProjectile projectile) =>
            TryCreate(ownerId, originRoundNumber, definition, ThrownProjectileOrigin.Launched, launchTimeSeconds,
                out projectile);

        /// <summary>승인된 투척 정보로부터 발사된 투척체를 만든다. 소유자·라운드는 승인 시점 값을 그대로 쓴다.</summary>
        public static bool TryCreateLaunched(in AcceptedThrow accepted, out ThrownProjectile projectile) =>
            TryCreate(accepted.OwnerId, accepted.RoundNumber, accepted.Definition, ThrownProjectileOrigin.Launched,
                accepted.AcceptedAtSeconds, out projectile);

        /// <summary>거리 비율은 Host/로컬 지면 검증 뒤 제공하며, 타이머는 생성 시 한 번 고정한다.</summary>
        public static bool TryCreateLaunched(in AcceptedThrow accepted, double distanceRatio, out ThrownProjectile projectile)
        {
            projectile = null;
            double seconds = ThrowFlightTiming.Resolve(accepted.Definition.FlightSeconds, distanceRatio);
            if (seconds <= 0 || !TryCreateLaunched(accepted, out projectile)) return false;
            projectile.FlightSeconds = seconds;
            return true;
        }

        /// <summary>사망으로 떨어진 투척체를 만든다. 소유자는 사망한 플레이어로 고정된다.</summary>
        public static bool TryCreateDeathDrop(
            int ownerId,
            int originRoundNumber,
            in ThrowingItemDefinition definition,
            double dropTimeSeconds,
            out ThrownProjectile projectile) =>
            TryCreate(ownerId, originRoundNumber, definition, ThrownProjectileOrigin.DeathDrop, dropTimeSeconds,
                out projectile);

        private static bool TryCreate(
            int ownerId,
            int originRoundNumber,
            in ThrowingItemDefinition definition,
            ThrownProjectileOrigin origin,
            double createdAtSeconds,
            out ThrownProjectile projectile)
        {
            projectile = null;
            if (ownerId < 0 || originRoundNumber <= 0)
            {
                return false;
            }

            if (!definition.IsValid || !ThrowingItemDefinition.IsFiniteTime(createdAtSeconds))
            {
                return false;
            }

            if (origin != ThrownProjectileOrigin.Launched && origin != ThrownProjectileOrigin.DeathDrop)
            {
                return false;
            }

            projectile = new ThrownProjectile(ownerId, originRoundNumber, definition, origin, createdAtSeconds);
            return true;
        }

        /// <summary>어댑터가 착지를 보고한다. 최초 1회만 기준 시각이 되고 이후 보고는 무시된다.</summary>
        public ProjectileUpdateResult ReportLanded(double timeSeconds)
        {
            if (!ThrowingItemDefinition.IsFiniteTime(timeSeconds) || timeSeconds < lastTimeSeconds)
            {
                return ProjectileUpdateResult.Rejected(ProjectileUpdateFailure.InvalidTime, State);
            }

            if (exploded)
            {
                return ProjectileUpdateResult.Rejected(ProjectileUpdateFailure.AlreadyExploded, State);
            }

            if (hasLanded)
            {
                // 두 번째 이후의 착지 보고는 기준 시각·타이머·시각 진행 어느 것도 바꾸지 않는다.
                // 시간 진행과 폭발 통보는 Advance 의 책임이다.
                return ProjectileUpdateResult.Rejected(ProjectileUpdateFailure.AlreadyLanded, State);
            }

            hasLanded = true;
            landedAtSeconds = timeSeconds;
            lastTimeSeconds = timeSeconds;

            if (!fuseStarted)
            {
                fuseStarted = true;
                fuseStartedAtSeconds = timeSeconds;
                fuseEndsAtSeconds = timeSeconds + definition.WarningSeconds;
            }

            return Evaluate(timeSeconds);
        }

        /// <summary>시각을 진행시키고 필요하면 폭발을 통보한다.</summary>
        public ProjectileUpdateResult Advance(double timeSeconds)
        {
            if (!ThrowingItemDefinition.IsFiniteTime(timeSeconds) || timeSeconds < lastTimeSeconds)
            {
                return ProjectileUpdateResult.Rejected(ProjectileUpdateFailure.InvalidTime, State);
            }

            lastTimeSeconds = timeSeconds;
            if (exploded)
            {
                return ProjectileUpdateResult.Rejected(ProjectileUpdateFailure.AlreadyExploded, State);
            }

            return Evaluate(timeSeconds);
        }

        /// <summary>라운드를 종료해 피해를 영구히 끈다. 퓨즈와 연출 수명은 계속 진행된다.</summary>
        public void EndRound()
        {
            roundEnded = true;
            damageEnabled = false;
        }

        /// <summary>전투 허용 여부를 갱신한다. 라운드가 끝난 뒤에는 무시된다.</summary>
        public void SetDamageEnabled(bool enabled)
        {
            if (roundEnded)
            {
                return;
            }

            damageEnabled = enabled;
        }

        private ProjectileUpdateResult Evaluate(double timeSeconds)
        {
            if (exploded || !fuseStarted || timeSeconds < fuseEndsAtSeconds || !hasLanded)
            {
                // 퓨즈가 만료돼도 착지 전에는 폭발을 통보하지 않는다.
                return ProjectileUpdateResult.Unchanged(State);
            }

            exploded = true;
            return ProjectileUpdateResult.Exploding(new ProjectileExplosion(
                ownerId,
                originRoundNumber,
                definition.ItemId,
                definition.Damage,
                definition.ExplosionRadiusMeters,
                CanDamage,
                timeSeconds));
        }
    }
}
