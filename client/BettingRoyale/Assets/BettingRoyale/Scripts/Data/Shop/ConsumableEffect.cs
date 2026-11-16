namespace BettingRoyale.Data.Shop
{
    /// <summary>소모품을 썼을 때 일어나는 일 (S-GD-YMKKDJ · S-GD-DDXPX9).</summary>
    public enum ConsumableEffectKind
    {
        /// <summary>아무 일도 없다. 효과가 정의되지 않은 아이템이다.</summary>
        None = 0,

        /// <summary>즉시 회복. 값 = 회복량.</summary>
        InstantHeal = 1,

        /// <summary>일정 시간 이동 속도. 값 = 증감분(0.10 = +10%).</summary>
        TimedMoveSpeed = 2,

        /// <summary>일정 시간 공격 속도. 값 = 증감분(0.30 = +30%).</summary>
        TimedAttackSpeed = 3,

        /// <summary>일정 시간 주는 피해. 값 = 증감분(0.20 = +20%).</summary>
        TimedDamageBonus = 4,

        /// <summary>일정 시간 받는 피해 감소. 값 = 감소분(0.25 = −25%).</summary>
        TimedDamageTaken = 5,

        /// <summary>일정 시간 유지되는 아이템 보호막. 값 = 보호막 양.</summary>
        Shield = 6,

        /// <summary>일정 시간 받는 피해 0. 값은 쓰지 않는다.</summary>
        Invulnerable = 7,

        /// <summary>대시를 즉시 최대 충전한다. 값·시간은 쓰지 않는다.</summary>
        DashRefill = 8,

        /// <summary>일정 시간 대시 재충전 시간 배율. 값 = 배율(0.5 = 절반).</summary>
        TimedDashRecharge = 9,

        /// <summary>일정 시간 1초마다 회복. 값 = 초당 회복량. 맞아도 끊기지 않는다.</summary>
        Regeneration = 10,

        // 11·12 는 폐기된 골드 주머니·재추첨권이 쓰던 값이다 (S-GD-DDXPX9 §7). 다시 쓰지 않는다.

        /// <summary>
        /// 일정 시간 방어력. 값 = <b>고정 증가량</b>(7 = +7). ⚠️ 퍼센트가 아니다 —
        /// 피해 계산 5단계에서 그대로 빼는 수다.
        /// </summary>
        TimedDefense = 13,

        /// <summary>
        /// 일정 시간 공격력. 값 = <b>고정 증가량</b>(3 = +3). ⚠️ 퍼센트가 아니다 —
        /// 피해 계산 1단계의 공격력에 그대로 더하는 수다.
        /// </summary>
        TimedAttackPower = 14,
    }

    /// <summary>
    /// 소모품 효과 하나. <b>수치를 들고 있을 뿐 적용하지 않는다</b> —
    /// 적용은 체력·이동·공격 컴포넌트와 경제가 하고, 이 계층은 <c>UnityEngine</c> 을 모른다 (D-060).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 한 아이템이 효과를 <b>여러 개</b> 가질 수 있다 — 대시 충전석(충전 + 재충전 절반),
    /// 프로틴 쉐이크(방어력 + 공격력), 과잉 보상(회복 + 보호막 + 공속 + 이속).
    /// 목록은 <see cref="ShopItemEffectCatalog.TryGetConsumableEffects"/> 가 준다.
    /// </para>
    /// <para>
    /// ⚠️ <b>같은 종류는 중첩되지 않는다</b>(S-GD-DDXPX9 §5). 다시 받으면 값과 시간을 덮어쓴다 —
    /// 그 규칙은 <see cref="ConsumableBuffState"/> 가 지킨다.
    /// </para>
    /// </remarks>
    /// <summary>회복 효과의 기준 (S-GD-Q75D75). 값이 고정량인지 최대 체력 비율인지를 <b>명시</b>한다 — 20 과 0.20 을 섞지 않는다.</summary>
    public enum ConsumableHealBasis
    {
        /// <summary>값 = 체력 고정량.</summary>
        Flat = 0,

        /// <summary>값 = 사용 시점 최종 최대 체력 대비 비율(0~1). 지속 회복은 총량 비율이다.</summary>
        MaxHealthRatio = 1,
    }

    public readonly struct ConsumableEffect
    {
        public readonly ConsumableEffectKind Kind;

        /// <summary>종류별 값. 뜻은 <see cref="ConsumableEffectKind"/> 각 항목에 적었다. 회복은 <see cref="Basis"/> 를 함께 본다.</summary>
        public readonly float Value;

        /// <summary>지속 시간(초). 즉시 효과는 0 이다.</summary>
        public readonly float DurationSeconds;

        /// <summary>회복(즉시·지속)의 값 기준. 회복이 아닌 효과는 <see cref="ConsumableHealBasis.Flat"/> 이다.</summary>
        public readonly ConsumableHealBasis Basis;

        private ConsumableEffect(ConsumableEffectKind kind, float value, float durationSeconds,
            ConsumableHealBasis basis = ConsumableHealBasis.Flat)
        {
            Kind = kind;
            Value = value;
            DurationSeconds = durationSeconds;
            Basis = basis;
        }

        /// <summary>
        /// <see cref="ConsumableEffectKind.InstantHeal"/> 일 때 <b>고정</b> 회복량. 비율 회복이면 0 — 최대 체력을 모르는 곳에서
        /// 잘못 읽지 않도록 <see cref="ResolveHealAmount"/> 를 쓴다.
        /// </summary>
        public float HealAmount => Kind == ConsumableEffectKind.InstantHeal && Basis == ConsumableHealBasis.Flat ? Value : 0f;

        /// <summary>비율 회복인가 (즉시·지속 공통).</summary>
        public bool IsMaxHealthRatio => Basis == ConsumableHealBasis.MaxHealthRatio;

        /// <summary>
        /// 이 효과가 <b>지금</b> 줄 회복 총량. 즉시 회복은 회복량, 지속 회복은 지속 시간 전체의 총량이다.
        /// 비율 회복은 <paramref name="maxHealth"/>(사용 시점 최종 최대 체력)를 곱한다.
        /// </summary>
        public float ResolveHealAmount(float maxHealth)
        {
            if (Kind != ConsumableEffectKind.InstantHeal && Kind != ConsumableEffectKind.Regeneration) return 0f;
            float total = Kind == ConsumableEffectKind.Regeneration && Basis == ConsumableHealBasis.Flat
                ? Value * DurationSeconds
                : Value;
            if (Basis == ConsumableHealBasis.MaxHealthRatio)
            {
                if (float.IsNaN(maxHealth) || float.IsInfinity(maxHealth) || maxHealth <= 0f) return 0f;
                total *= maxHealth;
            }

            return total > 0f ? total : 0f;
        }

        /// <summary>
        /// <see cref="ConsumableEffectKind.TimedMoveSpeed"/> 일 때 이동 속도 <b>증감분</b>. 다른 종류면 0.
        /// ⚠️ 0.10 은 "+10%" 다 — 배율(1.10)이 아니다.
        /// </summary>
        public float MoveSpeedBonus => Kind == ConsumableEffectKind.TimedMoveSpeed ? Value : 0f;

        public static ConsumableEffect InstantHeal(float amount) =>
            new(ConsumableEffectKind.InstantHeal, amount, 0f);

        /// <param name="ratio">최대 체력 대비 비율(0~1). 0.20 = 20%.</param>
        public static ConsumableEffect InstantHealRatio(float ratio) =>
            new(ConsumableEffectKind.InstantHeal, ratio, 0f, ConsumableHealBasis.MaxHealthRatio);

        public static ConsumableEffect TimedMoveSpeed(float bonus, float seconds) =>
            new(ConsumableEffectKind.TimedMoveSpeed, bonus, seconds);

        public static ConsumableEffect TimedAttackSpeed(float bonus, float seconds) =>
            new(ConsumableEffectKind.TimedAttackSpeed, bonus, seconds);

        public static ConsumableEffect TimedDamageBonus(float bonus, float seconds) =>
            new(ConsumableEffectKind.TimedDamageBonus, bonus, seconds);

        /// <param name="reduction">감소분. 0.25 = 받는 피해 −25%.</param>
        public static ConsumableEffect TimedDamageTaken(float reduction, float seconds) =>
            new(ConsumableEffectKind.TimedDamageTaken, reduction, seconds);

        public static ConsumableEffect Shield(float amount, float seconds) =>
            new(ConsumableEffectKind.Shield, amount, seconds);

        public static ConsumableEffect Invulnerable(float seconds) =>
            new(ConsumableEffectKind.Invulnerable, 0f, seconds);

        public static ConsumableEffect DashRefill() =>
            new(ConsumableEffectKind.DashRefill, 0f, 0f);

        /// <param name="multiplier">재충전 시간 배율. 0.5 = 절반.</param>
        public static ConsumableEffect TimedDashRecharge(float multiplier, float seconds) =>
            new(ConsumableEffectKind.TimedDashRecharge, multiplier, seconds);

        public static ConsumableEffect Regeneration(float healPerSecond, float seconds) =>
            new(ConsumableEffectKind.Regeneration, healPerSecond, seconds);

        /// <param name="totalRatio">지속 시간 전체에 나눠 주는 <b>총량</b> 비율(0~1). 0.40 / 8초 = 초당 5%.</param>
        public static ConsumableEffect RegenerationRatio(float totalRatio, float seconds) =>
            new(ConsumableEffectKind.Regeneration, totalRatio, seconds, ConsumableHealBasis.MaxHealthRatio);

        /// <param name="amount">방어력 <b>고정</b> 증가량. 7 = +7 (수호의 부적과 같은 단위).</param>
        public static ConsumableEffect TimedDefense(float amount, float seconds) =>
            new(ConsumableEffectKind.TimedDefense, amount, seconds);

        /// <param name="amount">공격력 <b>고정</b> 증가량. 3 = +3 (힘의 반지와 같은 단위).</param>
        public static ConsumableEffect TimedAttackPower(float amount, float seconds) =>
            new(ConsumableEffectKind.TimedAttackPower, amount, seconds);
    }
}
