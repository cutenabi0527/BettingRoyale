namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 소모품 효과 칸 — 효과의 <b>종류</b>다. 같은 칸이라도 건 아이템이 다르면 따로 켜지고 값을 더한다 (S-GD-G3QJCW).
    /// HUD 효과 줄은 <b>건 아이템마다 아이콘 하나</b>를 띄운다 (<see cref="ConsumableHudEffects"/>).
    /// </summary>
    /// <remarks>⚠️ 값은 표시 순서가 아니다. HUD 순서는 켜진 순서(FIFO)가 정한다.</remarks>
    public enum ConsumableBuffChannel
    {
        MoveSpeed = 0,
        AttackSpeed = 1,
        DamageBonus = 2,
        DamageTaken = 3,
        DashRecharge = 4,
        Shield = 5,
        Invulnerable = 6,
        Defense = 7,
        AttackPower = 8,
        Regeneration = 9,
    }
}
