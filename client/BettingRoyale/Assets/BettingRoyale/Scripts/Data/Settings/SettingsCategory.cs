namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 설정 화면의 최상위 분류이자 저장 문서의 섹션 단위.
    /// 각 분류는 독립된 도메인으로 다루며 값 저장 슬롯이 1:1로 대응한다.
    /// </summary>
    public enum SettingsCategory
    {
        Gameplay = 0,
        Hud = 1,
        KeyboardMouse = 2,
        Controller = 3,
        Audio = 4,
        Graphics = 5,
        Accessibility = 6
    }
}
