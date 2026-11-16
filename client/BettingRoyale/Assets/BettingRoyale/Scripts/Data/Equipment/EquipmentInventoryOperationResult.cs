namespace BettingRoyale.Data.Equipment
{
    public enum EquipmentInventoryFailure
    {
        None = 0,
        InvalidItem = 1,
        InvalidQuantity = 2,
        ConsumableCapacityFull = 3,
        SlotEmpty = 4,
        SlotDoesNotContainConsumable = 5,
        Downed = 6,
        Dashing = 7,
        Attacking = 8,
        Interacting = 9,
        CarryLimitExceeded = 10,
        /// <summary>기절 중이다(몸통 박치기! — S-GD-9MWJRR). 경직과 달리 기절은 소모품을 쓰지 못한다.</summary>
        Stunned = 11,
    }

    /// <summary>획득·드롭·사용의 원자적 결과. UI와 Network 어댑터는 이 결과를 번역할 뿐 규칙을 재구현하지 않는다.</summary>
    public readonly struct EquipmentInventoryOperationResult
    {
        public readonly bool Succeeded;
        public readonly EquipmentInventoryFailure Failure;
        public readonly int ChangedItemId;
        public readonly int DisplacedWeaponItemId;
        public readonly int DestroyedAccessoryItemId;
        public readonly int ConsumableSlotIndex;
        public readonly EquipmentInventorySnapshot Snapshot;

        private EquipmentInventoryOperationResult(
            bool succeeded,
            EquipmentInventoryFailure failure,
            int changedItemId,
            int displacedWeaponItemId,
            int destroyedAccessoryItemId,
            int consumableSlotIndex,
            in EquipmentInventorySnapshot snapshot)
        {
            Succeeded = succeeded;
            Failure = failure;
            ChangedItemId = changedItemId;
            DisplacedWeaponItemId = displacedWeaponItemId;
            DestroyedAccessoryItemId = destroyedAccessoryItemId;
            ConsumableSlotIndex = consumableSlotIndex;
            Snapshot = snapshot;
        }

        public static EquipmentInventoryOperationResult Success(
            int changedItemId,
            int displacedWeaponItemId,
            int destroyedAccessoryItemId,
            int consumableSlotIndex,
            in EquipmentInventorySnapshot snapshot) =>
            new(true, EquipmentInventoryFailure.None, changedItemId, displacedWeaponItemId, destroyedAccessoryItemId,
                consumableSlotIndex, snapshot);

        public static EquipmentInventoryOperationResult Rejected(
            EquipmentInventoryFailure failure,
            in EquipmentInventorySnapshot snapshot) =>
            new(false, failure, 0, 0, 0, -1, snapshot);
    }
}
