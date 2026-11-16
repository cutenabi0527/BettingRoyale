namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// <b>실제로 나간 기본 공격 한 번</b>의 축복 스냅샷 (<see cref="BlessingCombatState.BeginAttack"/>).
    ///
    /// <para>
    /// 불변이다. 어댑터는 이 값을 투사체·스윙에 실어 두었다가
    /// <see cref="BlessingCombatState.ConfirmHit"/>·<see cref="BlessingCombatState.CompleteAttack"/> 로 되돌려 준다.
    /// 늦게 도착한 화살이 그 사이 바뀐 상태를 건드리지 않도록 <see cref="Generation"/> 이 같이 실린다.
    /// </para>
    /// </summary>
    public readonly struct BlessingAttack
    {
        public BlessingAttack(
            int sequence,
            int generation,
            WeaponTag weapon,
            float damageBonus,
            float damagePenaltyMultiplier,
            float rangeMultiplier,
            float halfAngleOverride,
            float projectileSpeedMultiplier,
            float projectileRadius,
            float staggerSeconds,
            float recoveryMultiplier,
            bool counterWave,
            float damageMultiplier = 1f,
            float selfHealthCostFraction = 0f,
            int extraProjectileCount = 0,
            float extraProjectileDamageMultiplier = 1f,
            float extraProjectileSpreadDegrees = 0f,
            int pierceTargets = 0,
            bool splitPotReady = false,
            float splitPotShare = 0f,
            float splitPotRadius = 0f,
            bool crossCutReady = false,
            float crossCutDamage = 0f,
            float crossCutRadius = 0f,
            bool rainCheckReady = false,
            int rainCheckArrows = 0,
            float rainCheckDamage = 0f,
            float rainCheckRadius = 0f,
            float rainCheckDelay = 0f,
            float pierceDamageMultiplier = 1f,
            float halfAngleReduction = 0f,
            float minimumHalfAngle = 0f)
        {
            Sequence = sequence;
            Generation = generation;
            Weapon = weapon;
            DamageBonus = damageBonus;
            DamagePenaltyMultiplier = damagePenaltyMultiplier;
            RangeMultiplier = rangeMultiplier;
            HalfAngleOverride = halfAngleOverride;
            HalfAngleReduction = halfAngleReduction;
            MinimumHalfAngle = minimumHalfAngle;
            ProjectileSpeedMultiplier = projectileSpeedMultiplier;
            ProjectileRadius = projectileRadius;
            StaggerSeconds = staggerSeconds;
            RecoveryMultiplier = recoveryMultiplier;
            CounterWave = counterWave;
            DamageMultiplier = damageMultiplier;
            SelfHealthCostFraction = selfHealthCostFraction;
            ExtraProjectileCount = extraProjectileCount;
            ExtraProjectileDamageMultiplier = extraProjectileDamageMultiplier;
            ExtraProjectileSpreadDegrees = extraProjectileSpreadDegrees;
            PierceTargets = pierceTargets;
            SplitPotReady = splitPotReady;
            SplitPotShare = splitPotShare;
            SplitPotRadius = splitPotRadius;
            CrossCutReady = crossCutReady;
            CrossCutDamage = crossCutDamage;
            CrossCutRadius = crossCutRadius;
            RainCheckReady = rainCheckReady;
            RainCheckArrows = rainCheckArrows;
            RainCheckDamage = rainCheckDamage;
            RainCheckRadius = rainCheckRadius;
            RainCheckDelay = rainCheckDelay;
            PierceDamageMultiplier = pierceDamageMultiplier;
        }

        /// <summary>공격 일련번호. 상태가 이 번호로 중복 적중·완료를 가려낸다.</summary>
        public int Sequence { get; }

        /// <summary>발사 시점의 상태 세대. 라운드 초기화·축복 변경이 있으면 올라간다.</summary>
        public int Generation { get; }

        /// <summary>이 공격을 낸 주무기.</summary>
        public WeaponTag Weapon { get; }

        /// <summary>가산 피해 보너스. 최종 피해 = 기본 × (1 + 이 값) × <see cref="DamagePenaltyMultiplier"/>.</summary>
        public float DamageBonus { get; }

        /// <summary>승산 피해 배수. 감소 효과만 들어간다(기본 1).</summary>
        public float DamagePenaltyMultiplier { get; }

        /// <summary>사거리 배수. 보너스를 먼저 더하고 감소 배수를 곱한 결과다(기본 1).</summary>
        public float RangeMultiplier { get; }

        /// <summary>부채꼴 반각(도) 덮어쓰기. 음수면 무기 기본값을 쓴다.</summary>
        public float HalfAngleOverride { get; }

        /// <summary>현재 공격 반각에서 빼는 도수. 절대 반각 덮어쓰기가 있으면 적용하지 않는다.</summary>
        public float HalfAngleReduction { get; }

        /// <summary>반각 감소가 있는 공격의 하한(도). 일반 공격에는 적용하지 않는다.</summary>
        public float MinimumHalfAngle { get; }

        /// <summary>투사체 속도 배수(기본 1).</summary>
        public float ProjectileSpeedMultiplier { get; }

        /// <summary>판정 반경 가산치(m). 기본 반경에 더한다(기본 0).</summary>
        public float ProjectileRadius { get; }

        /// <summary>적중 시 추가로 주는 경직(초). 거리·칼끝 경직과는 별개로 더한다.</summary>
        public float StaggerSeconds { get; }

        /// <summary>발사 후 후딜 배수(기본 1).</summary>
        public float RecoveryMultiplier { get; }

        /// <summary>이 공격이 반격 충격파로 바뀌었는가. 반경·피해·밀치기 수치는 카탈로그에서 읽는다.</summary>
        public bool CounterWave { get; }

        /// <summary>이 공격에 적용할 최종 피해 배수. 치명타와 별도의 축복 배수다.</summary>
        public float DamageMultiplier { get; }

        /// <summary>발사 순간 지불할 최대 체력 비율. 어댑터가 실제 체력에서 차감한다.</summary>
        public float SelfHealthCostFraction { get; }

        /// <summary>분할 사격이 만드는 추가 화살 수.</summary>
        public int ExtraProjectileCount { get; }

        /// <summary>추가 화살의 기본 피해 배수.</summary>
        public float ExtraProjectileDamageMultiplier { get; }

        /// <summary>추가 화살이 양옆으로 퍼지는 전체 기준 각도.</summary>
        public float ExtraProjectileSpreadDegrees { get; }

        /// <summary>관통 공격이 허용하는 총 대상 수. 0 이면 첫 대상에서 끝난다.</summary>
        public int PierceTargets { get; }

        /// <summary>관통으로 이미 한 대상을 지난 뒤 다음 대상에 적용할 피해 배수.</summary>
        public float PierceDamageMultiplier { get; }

        /// <summary>팟 분배를 이번 공격에서 소비했는가.</summary>
        public bool SplitPotReady { get; }
        public float SplitPotShare { get; }
        public float SplitPotRadius { get; }

        /// <summary>십자 베기를 이번 공격에서 소비했는가.</summary>
        public bool CrossCutReady { get; }
        public float CrossCutDamage { get; }
        public float CrossCutRadius { get; }

        /// <summary>화살비 예약을 이번 공격에서 소비했는가.</summary>
        public bool RainCheckReady { get; }
        public int RainCheckArrows { get; }
        public float RainCheckDamage { get; }
        public float RainCheckRadius { get; }
        public float RainCheckDelay { get; }

        /// <summary><see cref="HalfAngleOverride"/> 를 써야 하는가.</summary>
        public bool HasHalfAngleOverride => HalfAngleOverride >= 0f;

        /// <summary>
        /// 화살비 같은 <b>파생 적중</b>용 스냅샷 (S-GD-ND8JZ5). 원본 공격의 상시 보너스(피해 증가·패널티·사거리 배율)는
        /// 그대로 들고, 공격 한 번에 한 번만 나가는 것(화살비 예약·팟 분배·십자 베기·관통·추가 화살·자기 체력 비용·
        /// 반격 충격파)은 끈다 — 화살비가 화살비를 다시 예약하거나 광역이 연쇄하지 않게 <b>출처에서</b> 막는다.
        /// </summary>
        /// <param name="sequence">파생 적중 전용 공격 순번(<c>BlessingCombatState.BeginDerivedAttack</c>). 예약마다 새로 받아 대상 중복 제거를 예약 단위로 한다.</param>
        public BlessingAttack AsDerivedHit(int sequence) =>
            new BlessingAttack(
                sequence: sequence,
                generation: Generation,
                weapon: Weapon,
                damageBonus: DamageBonus,
                damagePenaltyMultiplier: DamagePenaltyMultiplier,
                rangeMultiplier: RangeMultiplier,
                halfAngleOverride: HalfAngleOverride,
                projectileSpeedMultiplier: ProjectileSpeedMultiplier,
                projectileRadius: ProjectileRadius,
                staggerSeconds: StaggerSeconds,
                recoveryMultiplier: RecoveryMultiplier,
                counterWave: false,
                damageMultiplier: DamageMultiplier,
                selfHealthCostFraction: 0f,
                extraProjectileCount: 0,
                extraProjectileDamageMultiplier: 1f,
                extraProjectileSpreadDegrees: 0f,
                pierceTargets: 0,
                splitPotReady: false,
                splitPotShare: 0f,
                splitPotRadius: 0f,
                crossCutReady: false,
                crossCutDamage: 0f,
                crossCutRadius: 0f,
                rainCheckReady: false,
                rainCheckArrows: 0,
                rainCheckDamage: 0f,
                rainCheckRadius: 0f,
                rainCheckDelay: 0f,
                pierceDamageMultiplier: 1f,
                halfAngleReduction: HalfAngleReduction,
                minimumHalfAngle: MinimumHalfAngle);

        /// <summary>
        /// 공격 모양(사거리 배율·좌우 반각·투사체 속도 배율)만 바꾼 복사본. 나머지는 전부 그대로다.
        /// </summary>
        /// <remarks>
        /// 상시 능력치(엑셀 <c>attack-range</c>·<c>attack-arc</c>·<c>projectile-speed</c>)를 조건부 축복이 만든 스냅샷에
        /// 합칠 때 쓴다(<see cref="CombatantStatFormula.ApplyToAttack"/>). 순번·세대는 그대로라
        /// 적중 확정·공격 완료가 같은 공격으로 알아본다.
        /// </remarks>
        public BlessingAttack WithGeometry(float rangeMultiplier, float halfAngleOverride, float projectileSpeedMultiplier) =>
            new BlessingAttack(
                sequence: Sequence,
                generation: Generation,
                weapon: Weapon,
                damageBonus: DamageBonus,
                damagePenaltyMultiplier: DamagePenaltyMultiplier,
                rangeMultiplier: rangeMultiplier,
                halfAngleOverride: halfAngleOverride,
                projectileSpeedMultiplier: projectileSpeedMultiplier,
                projectileRadius: ProjectileRadius,
                staggerSeconds: StaggerSeconds,
                recoveryMultiplier: RecoveryMultiplier,
                counterWave: CounterWave,
                damageMultiplier: DamageMultiplier,
                selfHealthCostFraction: SelfHealthCostFraction,
                extraProjectileCount: ExtraProjectileCount,
                extraProjectileDamageMultiplier: ExtraProjectileDamageMultiplier,
                extraProjectileSpreadDegrees: ExtraProjectileSpreadDegrees,
                pierceTargets: PierceTargets,
                splitPotReady: SplitPotReady,
                splitPotShare: SplitPotShare,
                splitPotRadius: SplitPotRadius,
                crossCutReady: CrossCutReady,
                crossCutDamage: CrossCutDamage,
                crossCutRadius: CrossCutRadius,
                rainCheckReady: RainCheckReady,
                rainCheckArrows: RainCheckArrows,
                rainCheckDamage: RainCheckDamage,
                rainCheckRadius: RainCheckRadius,
                rainCheckDelay: RainCheckDelay,
                pierceDamageMultiplier: PierceDamageMultiplier,
                halfAngleReduction: HalfAngleReduction,
                minimumHalfAngle: MinimumHalfAngle);
    }
}
