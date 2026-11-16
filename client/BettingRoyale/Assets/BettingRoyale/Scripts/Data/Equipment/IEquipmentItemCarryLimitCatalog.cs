namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 항목별 "총 보유 상한"을 선택적으로 제공하는 카탈로그 확장 경계.
    /// </summary>
    /// <remarks>
    /// <see cref="IEquipmentItemCatalog"/> 를 구현한 객체가 이 인터페이스도 구현하면
    /// <see cref="EquipmentInventory"/> 는 소비품 획득 시 스택 용량과 별개로 총량 상한을 추가 검사한다.
    /// 구현하지 않은 기존 카탈로그의 동작은 바뀌지 않는다.
    /// 상한은 슬롯 단위가 아니라 소비품 슬롯 3칸 전체 합계에 적용된다.
    /// </remarks>
    public interface IEquipmentItemCarryLimitCatalog
    {
        /// <summary>
        /// 항목에 총 보유 상한이 있으면 true 와 상한을 돌려준다. 상한이 없으면 false 를 돌려준다.
        /// 0 이하 값은 잘못된 설정으로 간주되어 인벤토리가 획득을 거부한다.
        /// </summary>
        bool TryGetCarryLimit(int itemId, out int totalCarryLimit);
    }
}
