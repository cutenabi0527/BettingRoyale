namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 베팅 요청 하나의 결과 (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>⚠️ 값은 뒤에만 붙인다.</b> 계약에 실려 회선을 넘는다.
    /// </para>
    /// </summary>
    public enum RouletteBetResult
    {
        /// <summary>걸었다.</summary>
        Success = 0,

        /// <summary>지금은 베팅을 받지 않는다 — 회전 중이거나 마감됐다.</summary>
        NotBetting = 1,

        /// <summary>바퀴에 없는 숫자다.</summary>
        NoSuchNumber = 2,

        /// <summary>최소 베팅보다 적다.</summary>
        BelowMinimum = 3,

        /// <summary>
        /// 가진 골드가 최소 베팅에도 못 미친다.
        ///
        /// <b>가진 것보다 많이 걸었을 때는 이 값이 아니다</b> — 가진 만큼으로 깎아서
        /// 걸어준다. 사용자가 "전부 걸기" 를 눌렀을 때 거절당하는 것이 더 이상하다.
        /// </summary>
        NotEnoughGold = 4,
    }
}
