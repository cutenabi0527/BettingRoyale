namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// <b>체력에서 빼기 직전에 피해량을 다시 계산하는 자리</b> (축복 시스템 기획 §2, §12-5).
    ///
    /// <para>
    /// 지금 전투는 들어온 숫자를 그대로 체력에서 뺀다. 그래서 방어력·받는 피해 감소·치명타·
    /// 방어력 관통·무기 강화가 <b>개입할 지점이 없다.</b> 이 인터페이스가 그 한 자리다.
    /// </para>
    ///
    /// <para>
    /// <b>꽂는 쪽</b>: <c>CombatantHealth</c> 가 <c>SetDamageResolver</c> 로 받아
    /// 체력 감산 바로 앞에서 한 번 부른다. <b>안 꽂으면 아무 일도 일어나지 않는다</b> —
    /// 구현체가 없을 때의 동작은 지금과 완전히 같아야 한다.
    /// </para>
    ///
    /// <para>
    /// <b>구현하는 쪽</b>: 축3 의 <c>BlessingCombatResolver</c>. 스탯 조회와 파이프라인 호출을
    /// 전부 그쪽이 한다 — 전투 코드는 <b>결과 숫자만</b> 받는다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ Host 에서만 불린다.</b> 호출 지점이 권한 게이트 뒤라서, 비-Host 피어에서는
    /// 애초에 도달하지 않는다(D-061~D-063). 구현체가 권한을 다시 검사할 필요는 없지만,
    /// <b>여기서 상태를 바꾸면 안 된다</b> — 계산만 한다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public interface ICombatDamageResolver
    {
        /// <summary>
        /// 최종 피해량을 돌려준다.
        ///
        /// <para>
        /// <b>모르는 대상도 대상 시트만 neutral 로 계산한다.</b> 봇·훈련 더미·샌드박스 NPC 는
        /// 경제 플레이어가 아니므로 대상 축복은 없지만, 알려진 공격자의 시트와 무기 보너스는
        /// 그대로 적용할 수 있다. 따라서 준비 라운드의 더미 측정도 전투 라운드와 같은 계산
        /// 경계를 지나며, 해석기 자체가 없을 때에만 <paramref name="damageInfo"/> 의 원본값으로
        /// 돌아간다. 모르는 대상에서 예외를 던지면 전투가 멈추므로 neutral target을 사용한다.
        /// </para>
        /// </summary>
        /// <param name="target">맞는 쪽.</param>
        /// <param name="damageInfo">공격자·무기·소스·원래 피해량.</param>
        float ResolveDamage(CombatantId target, in DamageInfo damageInfo);
    }
}
