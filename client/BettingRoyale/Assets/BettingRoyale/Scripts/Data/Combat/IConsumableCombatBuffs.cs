namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 소모품이 <b>전투 계산</b>에 거는 값 — 공격·피격 쪽이 읽는다 (S-GD-DDXPX9 §2 · S-GD-RVVB4H §6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>권위 피어에서만 켜진다.</b> 소모품 효과는 권위 피어에서만 걸리므로 비권위 피어의 구현은 언제나 항등값
    /// (보너스 0 · 배율 1 · 무적 아님)을 돌려준다. 피해 계산도 권위 피어에서만 돈다.
    /// </para>
    /// <para>
    /// <b>조회 시점의 값이다.</b> 구현은 읽을 때 만료를 먼저 처리한다 — 무적 1초가 한 프레임 더 남는 일이 없다.
    /// "공격을 시작하는 순간" 이 필요한 값(<see cref="DamageBonus"/>)은 공격 쪽이 그 순간 읽어 들고 간다.
    /// </para>
    /// <para>스탯 시트를 거치지 않는다 — 이유는 S-GD-RVVB4H §6. <c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public interface IConsumableCombatBuffs
    {
        /// <summary>주는 피해 증감분(0.20 = +20%). 합연산으로 피해 증가에 더한다.</summary>
        float DamageBonus { get; }

        /// <summary>공격력 <b>고정</b> 증가량(+3 이면 3). 피해 계산 1단계 공격력에 더한다.</summary>
        float AttackPowerBonus { get; }

        /// <summary>방어력 <b>고정</b> 증가량(+7 이면 7). 피해 계산 5단계 방어력에 더한다.</summary>
        float DefenseBonus { get; }

        /// <summary>
        /// 받는 피해 배율(−25% 면 0.75). 따로 곱하지 않는다 — 맞는 쪽이 <c>배율 − 1</c>(−0.25)을 공용 계산식의 받는 피해
        /// 합산에 <b>더한다</b> (S-GD-EM82G5). 계산식이 없는 경로에서만 이 배율을 홀로 곱한다.
        /// 고정 피해(자기장·도트)에는 어느 경로에서도 적용하지 않는다 (S-GD-S9KNJM).
        /// </summary>
        float DamageTakenMultiplier { get; }

        /// <summary>받는 피해가 0 인가. 명중 판정은 그대로 성립한다 — 대시 무적과 다르다.</summary>
        bool IsInvulnerable { get; }

        /// <summary>아이템 보호막이 피해를 먼저 막는다. 막고 <b>남은</b> 피해를 돌려준다.</summary>
        float AbsorbWithShield(float damage);
    }
}
