namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 룰렛 상태의 화면용 한국어 이름. 열거형 이름(<c>Spinning</c> 등)을 알림·상태 줄에 그대로 내보내지 않는다
    /// (S-UX-NT4ERM, design-system §1).
    /// </summary>
    /// <remarks>
    /// 훈련장의 로컬 연습(Gameplay)과 온라인 코디네이터(Network), 연습 창(UI)이 같은 낱말을 쓰도록 여기 한 곳에 둔다.
    /// 룰렛 화면의 "베팅 가능"처럼 내가 걸 수 있는지에 따라 달라지는 문구는 그 화면이 따로 정한다.
    /// </remarks>
    public static class RouletteStateText
    {
        public static string Korean(RouletteState state) => state switch
        {
            RouletteState.Betting => "베팅 중",
            RouletteState.Spinning => "회전 중",
            RouletteState.Result => "결과",
            RouletteState.Closed => "마감",
            _ => "대기",
        };
    }
}
