namespace BettingRoyale.Data.Throwing
{
    /// <summary>투척체가 생긴 경위. 도화선(퓨즈) 기준 시각이 달라진다.</summary>
    public enum ThrownProjectileOrigin
    {
        None = 0,

        /// <summary>플레이어가 승인받아 던진 경우. 퓨즈는 최초 착지에서 시작한다.</summary>
        Launched = 1,

        /// <summary>사망으로 떨어뜨린 경우. 퓨즈는 떨어뜨린 시각에서 시작한다.</summary>
        DeathDrop = 2,
    }

    /// <summary>투척체 수명 상태.</summary>
    public enum ThrownProjectileState
    {
        None = 0,

        /// <summary>이동 중이며 퓨즈가 아직 시작하지 않았다.</summary>
        InFlight = 1,

        /// <summary>퓨즈가 진행 중이다(경고 연출 구간).</summary>
        Warning = 2,

        /// <summary>폭발이 끝났다. 더 이상 상태가 바뀌지 않는다.</summary>
        Exploded = 3,
    }

    /// <summary>수명 갱신이 거부된 이유.</summary>
    public enum ProjectileUpdateFailure
    {
        None = 0,

        /// <summary>NaN·무한대이거나 과거로 역행하는 시각이다.</summary>
        InvalidTime = 1,

        /// <summary>이미 착지를 보고했다. 기준 시각과 타이머는 바뀌지 않는다.</summary>
        AlreadyLanded = 2,

        /// <summary>이미 폭발했다.</summary>
        AlreadyExploded = 3,
    }

    /// <summary>
    /// 폭발 1회의 불변 통보. 소유자와 발생 라운드를 항상 함께 전달한다.
    /// </summary>
    /// <remarks>
    /// 팀 필터와 거리 감쇠는 코어 정의에 없다. 대상 선별·차폐 판정은 어댑터의 책임이다.
    /// </remarks>
    public readonly struct ProjectileExplosion
    {
        public readonly int OwnerId;
        public readonly int OriginRoundNumber;
        public readonly int ItemId;
        public readonly int Damage;
        public readonly float RadiusMeters;

        /// <summary>피해를 줄 수 있는지 여부. 라운드 종료 이후 폭발은 항상 false 다.</summary>
        public readonly bool CanDamage;

        public readonly double TimeSeconds;

        public ProjectileExplosion(
            int ownerId,
            int originRoundNumber,
            int itemId,
            int damage,
            float radiusMeters,
            bool canDamage,
            double timeSeconds)
        {
            OwnerId = ownerId;
            OriginRoundNumber = originRoundNumber;
            ItemId = itemId;
            Damage = damage;
            RadiusMeters = radiusMeters;
            CanDamage = canDamage;
            TimeSeconds = timeSeconds;
        }
    }

    /// <summary>수명 갱신 결과. 폭발은 전체 수명에서 최대 1회만 실린다.</summary>
    public readonly struct ProjectileUpdateResult
    {
        public readonly bool Accepted;
        public readonly ProjectileUpdateFailure Failure;
        public readonly ThrownProjectileState State;
        public readonly bool HasExplosion;
        public readonly ProjectileExplosion Explosion;

        private ProjectileUpdateResult(
            bool accepted,
            ProjectileUpdateFailure failure,
            ThrownProjectileState state,
            bool hasExplosion,
            in ProjectileExplosion explosion)
        {
            Accepted = accepted;
            Failure = failure;
            State = state;
            HasExplosion = hasExplosion;
            Explosion = explosion;
        }

        public static ProjectileUpdateResult Unchanged(ThrownProjectileState state) =>
            new(true, ProjectileUpdateFailure.None, state, false, default);

        public static ProjectileUpdateResult Exploding(in ProjectileExplosion explosion) =>
            new(true, ProjectileUpdateFailure.None, ThrownProjectileState.Exploded, true, explosion);

        public static ProjectileUpdateResult Rejected(ProjectileUpdateFailure failure, ThrownProjectileState state) =>
            new(false, failure, state, false, default);
    }
}
