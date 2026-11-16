namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 무기 1·소비품 3·장신구 1 슬롯의 순수 규칙 구현.
    /// 월드 드롭의 생성·삭제, 연출, 네트워크 전송은 이 클래스 밖의 어댑터가 담당한다.
    /// </summary>
    public sealed class EquipmentInventory
    {
        private int weaponItemId;
        private int accessoryItemId;
        private readonly EquipmentInventorySlot[] consumableSlots = new EquipmentInventorySlot[EquipmentInventorySnapshot.ConsumableSlotCount];

        public EquipmentInventorySnapshot Snapshot => CreateSnapshot();

        public EquipmentInventoryOperationResult TryPickup(
            int itemId,
            int quantity,
            IEquipmentItemCatalog catalog)
        {
            if (catalog == null || !catalog.TryGet(itemId, out EquipmentItemDefinition definition) || !definition.IsValid)
            {
                return EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.InvalidItem, CreateSnapshot());
            }
            if (quantity <= 0)
            {
                return EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.InvalidQuantity, CreateSnapshot());
            }
            if (definition.Kind != EquipmentItemKind.Consumable && quantity != 1)
            {
                return EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.InvalidQuantity, CreateSnapshot());
            }

            switch (definition.Kind)
            {
                case EquipmentItemKind.Weapon:
                    return EquipWeapon(itemId);
                case EquipmentItemKind.Accessory:
                    return EquipAccessory(itemId);
                case EquipmentItemKind.Consumable:
                    return AddConsumable(definition, quantity, catalog);
                default:
                    return EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.InvalidItem, CreateSnapshot());
            }
        }

        public EquipmentInventoryOperationResult TryDropWeapon()
        {
            if (weaponItemId <= 0)
            {
                return EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.SlotEmpty, CreateSnapshot());
            }

            int droppedItemId = weaponItemId;
            weaponItemId = 0;
            return EquipmentInventoryOperationResult.Success(
                changedItemId: droppedItemId,
                displacedWeaponItemId: droppedItemId,
                destroyedAccessoryItemId: 0,
                consumableSlotIndex: -1,
                CreateSnapshot());
        }

        public EquipmentInventoryOperationResult TryUseConsumable(int slotIndex, IEquipmentItemCatalog catalog)
        {
            if (slotIndex < 0 || slotIndex >= consumableSlots.Length || consumableSlots[slotIndex].IsEmpty)
            {
                return EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.SlotEmpty, CreateSnapshot());
            }

            EquipmentInventorySlot slot = consumableSlots[slotIndex];
            if (catalog == null || !catalog.TryGet(slot.ItemId, out EquipmentItemDefinition definition) || !definition.IsStackableConsumable)
            {
                return EquipmentInventoryOperationResult.Rejected(
                    EquipmentInventoryFailure.SlotDoesNotContainConsumable,
                    CreateSnapshot());
            }

            int remainingCount = slot.Count - 1;
            consumableSlots[slotIndex] = remainingCount > 0
                ? new EquipmentInventorySlot(slot.ItemId, remainingCount)
                : default;
            return EquipmentInventoryOperationResult.Success(
                changedItemId: slot.ItemId,
                displacedWeaponItemId: 0,
                destroyedAccessoryItemId: 0,
                consumableSlotIndex: slotIndex,
                CreateSnapshot());
        }

        /// <summary>Host가 복제한 스냅샷을 표현용 로컬 상태로 적용한다.</summary>
        /// <summary>
        /// 소모품 3칸을 비운다 — 전투 라운드 종료 경계 (S-GD-3DEQ2M). 무기·장신구는 그대로다.
        /// 비운 칸이 하나라도 있으면 <c>true</c>.
        /// </summary>
        public bool ClearConsumables()
        {
            bool changed = false;
            for (int i = 0; i < consumableSlots.Length; i++)
            {
                if (consumableSlots[i].IsEmpty) continue;
                consumableSlots[i] = default;
                changed = true;
            }

            return changed;
        }

        public void ApplySnapshot(in EquipmentInventorySnapshot snapshot)
        {
            weaponItemId = snapshot.WeaponItemId > 0 ? snapshot.WeaponItemId : 0;
            accessoryItemId = snapshot.AccessoryItemId > 0 ? snapshot.AccessoryItemId : 0;
            consumableSlots[0] = Normalize(snapshot.ConsumableSlot0);
            consumableSlots[1] = Normalize(snapshot.ConsumableSlot1);
            consumableSlots[2] = Normalize(snapshot.ConsumableSlot2);
        }

        private EquipmentInventoryOperationResult EquipWeapon(int itemId)
        {
            int displacedItemId = weaponItemId;
            weaponItemId = itemId;
            return EquipmentInventoryOperationResult.Success(
                changedItemId: itemId,
                displacedWeaponItemId: displacedItemId,
                destroyedAccessoryItemId: 0,
                consumableSlotIndex: -1,
                CreateSnapshot());
        }

        private EquipmentInventoryOperationResult EquipAccessory(int itemId)
        {
            int destroyedItemId = accessoryItemId;
            accessoryItemId = itemId;
            return EquipmentInventoryOperationResult.Success(
                changedItemId: itemId,
                displacedWeaponItemId: 0,
                destroyedAccessoryItemId: destroyedItemId,
                consumableSlotIndex: -1,
                CreateSnapshot());
        }

        private EquipmentInventoryOperationResult AddConsumable(
            in EquipmentItemDefinition definition,
            int quantity,
            IEquipmentItemCatalog catalog)
        {
            if (!definition.IsStackableConsumable)
            {
                return EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.InvalidItem, CreateSnapshot());
            }

            if (catalog is IEquipmentItemCarryLimitCatalog carryLimitCatalog &&
                carryLimitCatalog.TryGetCarryLimit(definition.ItemId, out int totalCarryLimit))
            {
                if (totalCarryLimit <= 0)
                {
                    return EquipmentInventoryOperationResult.Rejected(
                        EquipmentInventoryFailure.InvalidItem,
                        CreateSnapshot());
                }

                if (CountConsumable(definition.ItemId) + quantity > totalCarryLimit)
                {
                    return EquipmentInventoryOperationResult.Rejected(
                        EquipmentInventoryFailure.CarryLimitExceeded,
                        CreateSnapshot());
                }
            }

            int availableCapacity = 0;
            for (int i = 0; i < consumableSlots.Length; i++)
            {
                EquipmentInventorySlot slot = consumableSlots[i];
                if (slot.IsEmpty)
                {
                    availableCapacity += definition.MaxStack;
                }
                else if (slot.ItemId == definition.ItemId)
                {
                    availableCapacity += definition.MaxStack - slot.Count;
                }
            }

            // 월드 드롭 하나는 전량 수락하거나 남긴다. 부분 획득으로 드롭 수량이 애매해지지 않게 한다.
            if (availableCapacity < quantity)
            {
                return EquipmentInventoryOperationResult.Rejected(
                    EquipmentInventoryFailure.ConsumableCapacityFull,
                    CreateSnapshot());
            }

            int remaining = quantity;
            int firstChangedSlot = -1;
            for (int i = 0; i < consumableSlots.Length && remaining > 0; i++)
            {
                EquipmentInventorySlot slot = consumableSlots[i];
                if (slot.IsEmpty || slot.ItemId != definition.ItemId)
                {
                    continue;
                }

                int added = Min(definition.MaxStack - slot.Count, remaining);
                if (added <= 0)
                {
                    continue;
                }

                consumableSlots[i] = new EquipmentInventorySlot(slot.ItemId, slot.Count + added);
                remaining -= added;
                if (firstChangedSlot < 0)
                {
                    firstChangedSlot = i;
                }
            }

            for (int i = 0; i < consumableSlots.Length && remaining > 0; i++)
            {
                if (!consumableSlots[i].IsEmpty)
                {
                    continue;
                }

                int added = Min(definition.MaxStack, remaining);
                consumableSlots[i] = new EquipmentInventorySlot(definition.ItemId, added);
                remaining -= added;
                if (firstChangedSlot < 0)
                {
                    firstChangedSlot = i;
                }
            }

            return EquipmentInventoryOperationResult.Success(
                changedItemId: definition.ItemId,
                displacedWeaponItemId: 0,
                destroyedAccessoryItemId: 0,
                consumableSlotIndex: firstChangedSlot,
                CreateSnapshot());
        }

        private EquipmentInventorySnapshot CreateSnapshot() => new(
            weaponItemId,
            accessoryItemId,
            consumableSlots[0],
            consumableSlots[1],
            consumableSlots[2]);

        private long CountConsumable(int itemId)
        {
            long total = 0;
            for (int i = 0; i < consumableSlots.Length; i++)
            {
                EquipmentInventorySlot slot = consumableSlots[i];
                if (!slot.IsEmpty && slot.ItemId == itemId)
                {
                    total += slot.Count;
                }
            }

            return total;
        }

        private static int Min(int first, int second) => first < second ? first : second;

        private static EquipmentInventorySlot Normalize(in EquipmentInventorySlot slot) =>
            slot.ItemId > 0 && slot.Count > 0 ? slot : default;
    }
}
