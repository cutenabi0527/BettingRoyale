namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 개별 설정 항목의 편집 형태. UI 위젯 선택과 값 검증 규칙을 결정한다.
    /// </summary>
    public enum SettingKind
    {
        Toggle = 0,
        Slider = 1,
        Choice = 2,
        Binding = 3
    }
}
