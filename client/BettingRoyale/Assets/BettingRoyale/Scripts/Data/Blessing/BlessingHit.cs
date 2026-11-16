namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 거리·각도에 따라 달라지는 적중 보정 (<see cref="BlessingCombatState.PreviewHit"/>).
    ///
    /// <para>
    /// <b>상태를 바꾸지 않는 계산 결과다.</b> 같은 입력이면 몇 번을 물어도 같은 값이 나오므로
    /// 표시용 미리보기에도 쓸 수 있다. 실제 적용은 <see cref="BlessingCombatState.ConfirmHit"/> 가 맡는다.
    /// </para>
    /// </summary>
    public readonly struct BlessingHit
    {
        public BlessingHit(float damageBonus, float damagePenaltyMultiplier, float staggerSeconds,
            float damageMultiplier = 1f, float flatDamage = 0f)
        {
            DamageBonus = damageBonus;
            DamagePenaltyMultiplier = damagePenaltyMultiplier;
            StaggerSeconds = staggerSeconds;
            DamageMultiplier = damageMultiplier;
            FlatDamage = flatDamage;
        }

        /// <summary>가산 피해 보너스. <see cref="BlessingAttack.DamageBonus"/> 와 더해서 쓴다.</summary>
        public float DamageBonus { get; }

        /// <summary>승산 피해 배수(기본 1). <see cref="BlessingAttack.DamagePenaltyMultiplier"/> 와 곱한다.</summary>
        public float DamagePenaltyMultiplier { get; }

        /// <summary>이 적중이 주는 경직(초).</summary>
        public float StaggerSeconds { get; }

        /// <summary>거리·방향 조건으로 계산된 최종 피해 배수(기본 1).</summary>
        public float DamageMultiplier { get; }

        /// <summary>
        /// 이 적중에만 더하는 flat 피해 — 피해 계산 1 단계(공격력 + Σ flat)에 들어간다(S-GD-PA3E9T).
        /// 토큰 던지기의 추가 피해가 여기 온다. 기본 0.
        /// </summary>
        public float FlatDamage { get; }
    }
}
