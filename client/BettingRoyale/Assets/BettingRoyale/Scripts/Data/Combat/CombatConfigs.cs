namespace BettingRoyale.Data.Combat
{
    // 전투 수치 계약 묶음. 값은 전부 호출자(추후 ScriptableObject·설정)가 공급한다.
    // ⚠️ 여기에 기본값·상수를 두지 않는다 — 피해량·사거리·쿨타임·투사체 속도·대시·체력은
    //    전부 스펙 미정이다 (S-GD-7DRYPY). 상수를 추가하는 것은 스펙 없이 수치를 정하는 것이다.

    /// <summary>무기 하나의 수치 묶음. <see cref="WeaponType"/> 과 짝지어 쓴다.</summary>
    public readonly struct WeaponConfig
    {
        public readonly float Damage;
        public readonly float Range;
        public readonly float CooldownSeconds;

        /// <summary><see cref="WeaponTag.Projectile"/> 태그가 없는 무기(근접)는 0 으로 두고 무시한다.</summary>
        public readonly float ProjectileSpeed;

        /// <summary>
        /// 투사체 충돌 반경(m). 0 = 선(레이) 판정. 활 세트가 배율(<c>projectile-radius</c>)로 키운다 (S-GD-G3D2WP).
        /// 근접 무기는 0 이다.
        /// </summary>
        public readonly float CollisionRadius;

        public WeaponConfig(float damage, float range, float cooldownSeconds, float projectileSpeed, float collisionRadius = 0f)
        {
            Damage = damage;
            Range = range;
            CooldownSeconds = cooldownSeconds;
            ProjectileSpeed = projectileSpeed;
            CollisionRadius = collisionRadius > 0f && !float.IsNaN(collisionRadius) && !float.IsInfinity(collisionRadius)
                ? collisionRadius : 0f;
        }
    }

    /// <summary>대시 수치 묶음.</summary>
    public readonly struct DashConfig
    {
        public readonly float Speed;
        public readonly float DurationSeconds;
        public readonly float CooldownSeconds;

        public DashConfig(float speed, float durationSeconds, float cooldownSeconds)
        {
            Speed = speed;
            DurationSeconds = durationSeconds;
            CooldownSeconds = cooldownSeconds;
        }
    }

    /// <summary>전투 참여자 공통 수치 묶음.</summary>
    public readonly struct CombatantConfig
    {
        public readonly float MaxHealth;
        public readonly float MoveSpeed;
        public readonly DashConfig Dash;

        public CombatantConfig(float maxHealth, float moveSpeed, DashConfig dash)
        {
            MaxHealth = maxHealth;
            MoveSpeed = moveSpeed;
            Dash = dash;
        }
    }
}
