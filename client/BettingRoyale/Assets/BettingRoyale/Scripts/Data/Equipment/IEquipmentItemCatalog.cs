namespace BettingRoyale.Data.Equipment
{
    /// <summary>ItemId를 인벤토리 규칙에 필요한 최소 정의로 해석하는 Fusion 비의존 경계.</summary>
    public interface IEquipmentItemCatalog
    {
        bool TryGet(int itemId, out EquipmentItemDefinition definition);
    }
}
