using BettingRoyale.Data.Equipment;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public class EquipmentFocusSelectionTests
    {
        [Test]
        public void FindNextOccupied_SkipsEmptySlotsAndWrapsBetweenWeaponAndConsumables()
        {
            var snapshot = new EquipmentInventorySnapshot(
                weaponItemId: 10,
                accessoryItemId: 0,
                new EquipmentInventorySlot(20, 1),
                default,
                new EquipmentInventorySlot(22, 1));

            Assert.That(
                EquipmentFocusSelection.FindNextOccupied(snapshot, EquipmentFocus.Weapon, step: 1),
                Is.EqualTo(EquipmentFocus.Consumable1));
            Assert.That(
                EquipmentFocusSelection.FindNextOccupied(snapshot, EquipmentFocus.Consumable1, step: 1),
                Is.EqualTo(EquipmentFocus.Consumable3));
            Assert.That(
                EquipmentFocusSelection.FindNextOccupied(snapshot, EquipmentFocus.Weapon, step: -1),
                Is.EqualTo(EquipmentFocus.Consumable3));
        }

        [Test]
        public void Normalize_LeavesOccupiedFocusAndMovesEmptyFocusToNextHeldSlot()
        {
            var snapshot = new EquipmentInventorySnapshot(
                weaponItemId: 10,
                accessoryItemId: 0,
                default,
                new EquipmentInventorySlot(21, 2),
                default);

            Assert.That(
                EquipmentFocusSelection.Normalize(snapshot, EquipmentFocus.Consumable2),
                Is.EqualTo(EquipmentFocus.Consumable2));
            Assert.That(
                EquipmentFocusSelection.Normalize(snapshot, EquipmentFocus.Consumable1),
                Is.EqualTo(EquipmentFocus.Consumable2));
        }

        [Test]
        public void Normalize_ReturnsWeaponWhenNoSelectableEquipmentExists()
        {
            var snapshot = new EquipmentInventorySnapshot(0, 999, default, default, default);

            Assert.That(
                EquipmentFocusSelection.Normalize(snapshot, (EquipmentFocus)123),
                Is.EqualTo(EquipmentFocus.Weapon));
            Assert.That(EquipmentFocusSelection.IsOccupied(snapshot, EquipmentFocus.Weapon), Is.False);
            Assert.That(EquipmentFocusSelection.TryGetConsumableSlotIndex(EquipmentFocus.Consumable3, out int index), Is.True);
            Assert.That(index, Is.EqualTo(2));
        }
    }
}
