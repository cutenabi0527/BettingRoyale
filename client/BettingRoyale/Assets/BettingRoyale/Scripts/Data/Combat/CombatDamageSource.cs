namespace BettingRoyale.Data.Combat
{
    /// <summary>피해/빈사의 원인. 환경 원인은 킬/어시스트 귀속을 확정하지 않는다.</summary>
    /// <remarks><see cref="Dot"/> 은 독화살 같은 지속 피해다 — 고정 피해이고 피격 트리거를 돌리지 않는다(S-GD-PA3E9T).</remarks>
    public enum CombatDamageSource { Weapon = 0, Zone = 1, Blessing = 2, Dot = 3 }
}
