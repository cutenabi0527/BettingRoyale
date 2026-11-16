namespace BettingRoyale.Data.Equipment
{
    /// <summary>복제와 HUD가 소비하는 고정 크기 장비 인벤토리 상태.</summary>
    public readonly struct EquipmentInventorySnapshot
    {
        public const int ConsumableSlotCount = 3;

        public readonly int WeaponItemId;
        public readonly int AccessoryItemId;
        public readonly EquipmentInventorySlot ConsumableSlot0;
        public readonly EquipmentInventorySlot ConsumableSlot1;
        public readonly EquipmentInventorySlot ConsumableSlot2;

        public EquipmentInventorySnapshot(
            int weaponItemId,
            int accessoryItemId,
            in EquipmentInventorySlot consumableSlot0,
            in EquipmentInventorySlot consumableSlot1,
            in EquipmentInventorySlot consumableSlot2)
        {
            WeaponItemId = weaponItemId;
            AccessoryItemId = accessoryItemId;
            ConsumableSlot0 = consumableSlot0;
            ConsumableSlot1 = consumableSlot1;
            ConsumableSlot2 = consumableSlot2;
        }

        public EquipmentInventorySlot GetConsumableSlot(int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return ConsumableSlot0;
                case 1: return ConsumableSlot1;
                case 2: return ConsumableSlot2;
                default: return default;
            }
        }
    }

    public readonly struct EquipmentInventorySlot
    {
        public readonly int ItemId;
        public readonly int Count;

        public EquipmentInventorySlot(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public bool IsEmpty => ItemId <= 0 || Count <= 0;
    }
}
