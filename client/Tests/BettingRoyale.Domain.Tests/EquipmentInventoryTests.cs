using System.Collections.Generic;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Equipment;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public class EquipmentInventoryTests
    {
        private const int SwordItemId = 10;
        private const int BowItemId = 11;
        private const int PotionItemId = 20;
        private const int RingItemId = 30;
        private const int EarringItemId = 31;

        [Test]
        public void TryPickup_WeaponReplacesCurrentWeaponAndReturnsDisplacedWorldDrop()
        {
            var inventory = new EquipmentInventory();
            TestCatalog catalog = CreateCatalog();

            inventory.TryPickup(SwordItemId, 1, catalog);
            EquipmentInventoryOperationResult result = inventory.TryPickup(BowItemId, 1, catalog);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Snapshot.WeaponItemId, Is.EqualTo(BowItemId));
            Assert.That(result.DisplacedWeaponItemId, Is.EqualTo(SwordItemId));
            Assert.That(result.DestroyedAccessoryItemId, Is.Zero);
        }

        [Test]
        public void TryDropWeapon_LeavesInventoryUnarmedAndReturnsDroppedItem()
        {
            var inventory = new EquipmentInventory();
            TestCatalog catalog = CreateCatalog();
            inventory.TryPickup(SwordItemId, 1, catalog);

            EquipmentInventoryOperationResult result = inventory.TryDropWeapon();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Snapshot.WeaponItemId, Is.Zero);
            Assert.That(result.DisplacedWeaponItemId, Is.EqualTo(SwordItemId));
        }

        [Test]
        public void TryPickup_ConsumableStacksBeforeUsingAnotherSlot()
        {
            var inventory = new EquipmentInventory();
            TestCatalog catalog = CreateCatalog();

            inventory.TryPickup(PotionItemId, 2, catalog);
            EquipmentInventoryOperationResult result = inventory.TryPickup(PotionItemId, 1, catalog);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Snapshot.GetConsumableSlot(0).ItemId, Is.EqualTo(PotionItemId));
            Assert.That(result.Snapshot.GetConsumableSlot(0).Count, Is.EqualTo(3));
            Assert.That(result.Snapshot.GetConsumableSlot(1).IsEmpty, Is.True);
        }

        [Test]
        public void TryPickup_ConsumableRejectsAtomicallyWhenAllSlotsCannotHoldEntireDrop()
        {
            var inventory = new EquipmentInventory();
            TestCatalog catalog = CreateCatalog();
            inventory.TryPickup(PotionItemId, 9, catalog);

            EquipmentInventoryOperationResult result = inventory.TryPickup(PotionItemId, 1, catalog);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(EquipmentInventoryFailure.ConsumableCapacityFull));
            Assert.That(result.Snapshot.GetConsumableSlot(0).Count, Is.EqualTo(3));
            Assert.That(result.Snapshot.GetConsumableSlot(1).Count, Is.EqualTo(3));
            Assert.That(result.Snapshot.GetConsumableSlot(2).Count, Is.EqualTo(3));
        }

        [Test]
        public void TryPickup_AccessoryDestroysPreviousAccessoryInsteadOfDroppingIt()
        {
            var inventory = new EquipmentInventory();
            TestCatalog catalog = CreateCatalog();
            inventory.TryPickup(RingItemId, 1, catalog);

            EquipmentInventoryOperationResult result = inventory.TryPickup(EarringItemId, 1, catalog);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Snapshot.AccessoryItemId, Is.EqualTo(EarringItemId));
            Assert.That(result.DisplacedWeaponItemId, Is.Zero);
            Assert.That(result.DestroyedAccessoryItemId, Is.EqualTo(RingItemId));
        }

        [Test]
        public void TryPickup_NonConsumableStackIsRejectedWithoutChangingInventory()
        {
            var inventory = new EquipmentInventory();
            TestCatalog catalog = CreateCatalog();

            EquipmentInventoryOperationResult result = inventory.TryPickup(SwordItemId, 2, catalog);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(EquipmentInventoryFailure.InvalidQuantity));
            Assert.That(result.Snapshot.WeaponItemId, Is.Zero);
        }

        [Test]
        public void ApplySnapshot_NormalizesNonPositiveSlotsToEmpty()
        {
            var inventory = new EquipmentInventory();
            var source = new EquipmentInventorySnapshot(
                weaponItemId: SwordItemId,
                accessoryItemId: RingItemId,
                new EquipmentInventorySlot(PotionItemId, 2),
                new EquipmentInventorySlot(PotionItemId, 0),
                new EquipmentInventorySlot(0, 5));

            inventory.ApplySnapshot(source);

            Assert.That(inventory.Snapshot.WeaponItemId, Is.EqualTo(SwordItemId));
            Assert.That(inventory.Snapshot.AccessoryItemId, Is.EqualTo(RingItemId));
            Assert.That(inventory.Snapshot.GetConsumableSlot(0).Count, Is.EqualTo(2));
            Assert.That(inventory.Snapshot.GetConsumableSlot(1).IsEmpty, Is.True);
            Assert.That(inventory.Snapshot.GetConsumableSlot(2).IsEmpty, Is.True);
        }

        [Test]
        public void TryUseConsumable_DecrementsAndClearsEmptySlot()
        {
            var inventory = new EquipmentInventory();
            TestCatalog catalog = CreateCatalog();
            inventory.TryPickup(PotionItemId, 1, catalog);

            EquipmentInventoryOperationResult result = inventory.TryUseConsumable(0, catalog);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ConsumableSlotIndex, Is.Zero);
            Assert.That(result.Snapshot.GetConsumableSlot(0).IsEmpty, Is.True);
        }

        private static TestCatalog CreateCatalog() => new(
            new EquipmentItemDefinition(SwordItemId, EquipmentItemKind.Weapon, EquipmentArchetype.Sword, 100, WeaponType.Sword, 0),
            new EquipmentItemDefinition(BowItemId, EquipmentItemKind.Weapon, EquipmentArchetype.Bow, 101, WeaponType.Bow, 0),
            new EquipmentItemDefinition(PotionItemId, EquipmentItemKind.Consumable, EquipmentArchetype.None, 200, WeaponType.Sword, 3),
            new EquipmentItemDefinition(RingItemId, EquipmentItemKind.Accessory, EquipmentArchetype.Accessory, 300, WeaponType.Sword, 0),
            new EquipmentItemDefinition(EarringItemId, EquipmentItemKind.Accessory, EquipmentArchetype.Accessory, 301, WeaponType.Sword, 0));

        private sealed class TestCatalog : IEquipmentItemCatalog
        {
            private readonly Dictionary<int, EquipmentItemDefinition> definitions = new();

            public TestCatalog(params EquipmentItemDefinition[] values)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    definitions.Add(values[i].ItemId, values[i]);
                }
            }

            public bool TryGet(int itemId, out EquipmentItemDefinition definition) =>
                definitions.TryGetValue(itemId, out definition);
        }
    }
}
