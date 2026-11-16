namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 장비 수량과 분리해 복제하는 전투 포커스 경계. Host만 선택을 확정하고, 다른 피어는
    /// <see cref="ApplyEquipmentFocusPresentation"/>으로 받은 상태만 표시한다.
    /// </summary>
    public interface IEquipmentFocusDriver
    {
        EquipmentFocus SelectedEquipmentFocus { get; }

        bool TryAuthoritySelectEquipmentFocus(EquipmentFocus focus);
        bool TryAuthorityCycleEquipmentFocus(int step);
        void ApplyEquipmentFocusPresentation(EquipmentFocus focus);
    }
}
