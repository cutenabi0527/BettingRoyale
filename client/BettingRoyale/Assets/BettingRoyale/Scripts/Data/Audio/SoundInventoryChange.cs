using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Audio
{
    /// <summary>Compare item totals, not slot positions: sorting is not acquisition.</summary>
    public static class SoundInventoryChange
    {
        public static bool HasAcquired(in EquipmentInventorySnapshot before, in EquipmentInventorySnapshot after)
        {
            if (after.WeaponItemId > 0 && before.WeaponItemId != after.WeaponItemId) return true;
            if (after.AccessoryItemId > 0 && before.AccessoryItemId != after.AccessoryItemId) return true;
            for (int i = 0; i < EquipmentInventorySnapshot.ConsumableSlotCount; i++)
            {
                int id = after.GetConsumableSlot(i).ItemId;
                if (id > 0 && Count(after, id) > Count(before, id)) return true;
            }
            return false;
        }
        private static int Count(in EquipmentInventorySnapshot snapshot, int id)
        {
            int total = 0;
            for (int i = 0; i < EquipmentInventorySnapshot.ConsumableSlotCount; i++)
            {
                EquipmentInventorySlot slot = snapshot.GetConsumableSlot(i);
                if (slot.ItemId == id && !slot.IsEmpty) total += slot.Count;
            }
            return total;
        }
    }
}
