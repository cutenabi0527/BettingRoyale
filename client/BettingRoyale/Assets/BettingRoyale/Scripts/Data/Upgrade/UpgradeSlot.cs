namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화 부위 (S-GD-A2YDDJ, S-GD-Z9CJ4H).
    ///
    /// <para>
    /// <b>둘뿐이고 각각 단계를 따로 가진다.</b> 무기 2강 + 방어구 1강 같은 상태가 성립하고,
    /// 세트 효과는 <b>낮은 쪽</b>을 본다 — 그것이 한쪽 몰빵을 막는 장치다.
    /// </para>
    ///
    /// <b>⚠️ 값은 뒤에만 붙인다.</b> 회선을 넘는다.
    /// </summary>
    public enum UpgradeSlot
    {
        /// <summary>무기. <b>종류마다 표가 다르다</b> — 검과 활이 같은 단계에서 다른 값을 받는다.</summary>
        Weapon = 0,

        /// <summary>방어구. 종류 구분이 없다.</summary>
        Armor = 1,
    }
}
