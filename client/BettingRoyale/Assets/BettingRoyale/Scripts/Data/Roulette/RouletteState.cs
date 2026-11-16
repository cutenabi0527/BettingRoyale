namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 방에 하나뿐인 룰렛의 상태 (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>⚠️ 값은 뒤에만 붙인다.</b> 이 열거형은 계약에 실려 회선을 넘는다.
    /// 중간에 끼우면 배포된 값의 뜻이 밀린다.
    /// </para>
    /// </summary>
    public enum RouletteState
    {
        /// <summary>아직 열리지 않았거나 판 사이다. 다음 틱에 베팅이 열린다.</summary>
        Idle = 0,

        /// <summary><b>이때만 걸 수 있다.</b> 물릴 수도 있다.</summary>
        Betting = 1,

        /// <summary>
        /// 회전 중. <b>결과는 이미 정해져 있다</b> — 화면만 돈다.
        /// 여기서는 걸 수도 물릴 수도 없다.
        /// </summary>
        Spinning = 2,

        /// <summary>적중 숫자를 보여주는 중. 정산은 이 상태로 들어올 때 끝났다.</summary>
        Result = 3,

        /// <summary>
        /// 준비 시간이 얼마 안 남아 <b>새 판을 열지 않는다.</b>
        /// 진행 중이던 베팅은 이 상태로 갈 때 환불된다.
        /// </summary>
        Closed = 4,
    }
}
