namespace BettingRoyale.Data.Combat
{
    /// <summary>한 번의 피해 적용에 필요한 데이터. 피해량은 호출자가 설정에서 가져와 공급한다 (S-GD-7DRYPY).</summary>
    public readonly struct DamageInfo
    {
        public readonly CombatantId Attacker;
        public readonly WeaponType Weapon;
        public readonly float Amount;
        public readonly CombatDamageSource Source;

        public readonly float DamageBonus;
        public readonly float DamagePenaltyMultiplier;
        public readonly bool IsCritical;

        /// <summary>
        /// 공격하는 쪽 공격력에 더할 <b>고정</b> 값 — 소모품(프로틴 쉐이크 +3). 스탯 시트 밖에서 온다 (S-GD-RVVB4H §6).
        /// </summary>
        public readonly float AttackPowerBonus;

        /// <summary>
        /// 맞는 쪽 방어력에 더할 <b>고정</b> 값 — 소모품(프로틴 쉐이크 +7). 맞는 쪽이 계산 직전에 채운다
        /// (<see cref="WithDefenseBonus"/>).
        /// </summary>
        public readonly float DefenseBonus;

        /// <summary>
        /// 맞는 쪽 <b>받는 피해</b>에 더할 증감분 — 소모품(철벽 물약 −25% 면 <c>-0.25</c>)과 축복의 받는 피해 감소
        /// (하우스 엣지 −10% 면 <c>-0.1</c>)를 모두 담는다. 피해 계산 4단계의 받는 피해 합산에 <b>더한다</b>
        /// (합연산, S-GD-EM82G5 · S-GD-SH74SC). 맞는 쪽이 계산 직전에 채운다(<see cref="WithDamageTakenBonus"/>).
        /// </summary>
        public readonly float DamageTakenBonus;

        public bool IsPrimaryWeapon => Source == CombatDamageSource.Weapon
            && (Weapon == WeaponType.Sword || Weapon == WeaponType.Bow);

        /// <summary>잭팟이 터졌는가(피해 계산 3-B). Host 가 굴린 결과다 — 치명타와 같다.</summary>
        public readonly bool IsJackpot;

        /// <summary>
        /// 고정 피해인가 — 받는 피해 감소·방어력·피해 상한을 전부 건너뛰고 무적·보호막만 통한다(S-GD-PA3E9T).
        /// 자기장은 소스만으로 고정 피해다. 이 값은 무기·축복 소스인데 고정인 것(표식 사격 등)에 쓴다.
        /// </summary>
        public readonly bool IsFixed;

        private readonly float damageMultiplier;
        private readonly bool hasDamageMultiplier;

        /// <summary>
        /// 이 한 대에만 곱하는 배율(잭팟 축복·분할 사격·관통 감쇠). 피해 계산이 <b>무기 공격력 + 축복 공격력 전체</b>에
        /// 곱한다(3-B). 기본값은 1 이다 — <c>default</c> 로 만든 값도 1 로 읽힌다.
        /// </summary>
        public float DamageMultiplier => hasDamageMultiplier ? damageMultiplier : 1f;

        public DamageInfo(CombatantId attacker, WeaponType weapon, float amount,
            CombatDamageSource source = CombatDamageSource.Weapon, float damageBonus = 0f,
            float damagePenaltyMultiplier = 1f, bool isCritical = false, float damageMultiplier = 1f,
            bool isJackpot = false, bool isFixed = false,
            float attackPowerBonus = 0f, float defenseBonus = 0f, float damageTakenBonus = 0f)
        {
            Attacker = attacker;
            Weapon = weapon;
            Amount = amount;
            Source = source;
            DamageBonus = damageBonus;
            DamagePenaltyMultiplier = damagePenaltyMultiplier;
            IsCritical = isCritical;
            IsJackpot = isJackpot;
            IsFixed = isFixed;
            this.damageMultiplier = damageMultiplier;
            hasDamageMultiplier = true;
            AttackPowerBonus = attackPowerBonus;
            DefenseBonus = defenseBonus;
            DamageTakenBonus = damageTakenBonus;
        }

        private DamageInfo(in DamageInfo source, float defenseBonus, float damageTakenBonus)
        {
            Attacker = source.Attacker;
            Weapon = source.Weapon;
            Amount = source.Amount;
            Source = source.Source;
            DamageBonus = source.DamageBonus;
            DamagePenaltyMultiplier = source.DamagePenaltyMultiplier;
            IsCritical = source.IsCritical;
            IsJackpot = source.IsJackpot;
            IsFixed = source.IsFixed;
            damageMultiplier = source.damageMultiplier;
            hasDamageMultiplier = source.hasDamageMultiplier;
            AttackPowerBonus = source.AttackPowerBonus;
            DefenseBonus = defenseBonus;
            DamageTakenBonus = damageTakenBonus;
        }

        /// <summary>맞는 쪽 방어력 보너스만 바꾼 사본. 나머지는 그대로다.</summary>
        public DamageInfo WithDefenseBonus(float defenseBonus) => new DamageInfo(this, defenseBonus, DamageTakenBonus);

        /// <summary>맞는 쪽 받는 피해 증감분만 바꾼 사본. 나머지는 그대로다.</summary>
        public DamageInfo WithDamageTakenBonus(float damageTakenBonus) => new DamageInfo(this, DefenseBonus, damageTakenBonus);
    }

    /// <summary>
    /// 피해 적용 권한을 판정하는 네트워크 비의존 경계.
    /// 로컬 프로토타입은 권한 구현을 연결하지 않아도 되며, Fusion 어댑터는 Host(StateAuthority)에서만
    /// <see langword="true"/>를 반환하는 구현을 주입한다. 피해량이나 명중 결과를 RPC로 전달하지 않는다.
    /// </summary>
    public interface IDamageAuthority
    {
        bool CanApplyDamage(CombatantId target, in DamageInfo damageInfo);
    }

    /// <summary>
    /// 라운드 결과 확정·회복을 실행할 권한의 네트워크 비의존 경계(S-GD-JRKHQA).
    /// 네트워크 참여자가 하나라도 있으면 이 계약에서 허용한 피어만 라운드를 넘긴다.
    /// </summary>
    public interface ICombatRoundAuthority
    {
        bool CanFinalizeCombatRound { get; }
    }

    /// <summary>
    /// 전투 흐름 계약: 공격 → 명중 → 피해(<see cref="ApplyDamage"/>) → 빈사(<see cref="IsDowned"/>)
    /// → 다음 라운드 회복(<see cref="RecoverForNextRound"/>) (S-GD-7DRYPY).
    /// 체력 감산·빈사 판정 같은 구현은 Gameplay 후속 작업이다 — 이 슬라이스는 계약만 정의한다.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>피해 대상의 도메인 식별자. 공격자는 이 값으로 자기 피해를 제외한다.</summary>
        CombatantId Id { get; }

        /// <summary>빈사 상태인가.</summary>
        bool IsDowned { get; }

        /// <summary>현재 공격을 피해로 받아들일 수 있는가. 빈사·무적 같은 방어 상태를 포함한다.</summary>
        bool CanBeHit { get; }

        /// <summary>현재 실행 주체가 이 피해를 적용할 권한이 있는가.</summary>
        bool IsDamageAuthorized(in DamageInfo damageInfo);

        /// <summary>네트워크 어댑터가 권한 판정기를 연결한다. <see langword="null"/>은 로컬 프로토타입이다.</summary>
        void SetDamageAuthority(IDamageAuthority authority);

        void ApplyDamage(in DamageInfo damageInfo);

        /// <summary>다음 라운드 시작 시 회복. 회복량·초기화 범위는 미정(S-GD-7DRYPY)이라 구현이 정한다.</summary>
        void RecoverForNextRound();
    }

    /// <summary>
    /// 권위 명중 경계가 피해 적용의 성공 여부를 좁게 확인하는 선택 계약이다.
    /// 기존 <see cref="IDamageable.ApplyDamage"/>의 void 계약과 직접 호출의 부작용 순서는 유지한다.
    /// </summary>
    public interface ICombatHitReceiptTarget
    {
        /// <summary>
        /// 양수·유한한 체력 감소 또는 훈련 더미 측정이 실제로 발생했을 때만 true를 반환한다.
        /// </summary>
        bool TryApplyHitDamage(in DamageInfo damageInfo);
    }
}
