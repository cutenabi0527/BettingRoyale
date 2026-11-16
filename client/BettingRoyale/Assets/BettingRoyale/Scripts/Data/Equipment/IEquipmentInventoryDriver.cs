namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// Gameplay 인벤토리와 Fusion 어댑터가 공유하는 최소 경계.
    /// Network는 이 인터페이스만 참조하며 Gameplay 구현 타입을 알지 못한다.
    /// </summary>
    public interface IEquipmentInventoryDriver
    {
        EquipmentInventorySnapshot EquipmentSnapshot { get; }

        EquipmentInventoryOperationResult TryAuthorityPickup(int itemId, int quantity);
        EquipmentInventoryOperationResult TryAuthorityDropWeapon();
        EquipmentInventoryOperationResult TryAuthorityUseConsumable(int slotIndex);
        void ApplyPresentation(in EquipmentInventorySnapshot snapshot);
    }
}
