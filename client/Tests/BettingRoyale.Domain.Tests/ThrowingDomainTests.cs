using System;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Throwing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 투척 도메인 첫 슬라이스의 규칙 테스트. Unity·Fusion 없이 순수 규칙만 검증한다.
    /// </summary>
    [TestFixture]
    public sealed class ThrowingDomainTests
    {
        private const int OwnerId = 7;
        private const int OtherThrowingItemId = 2104;

        // ---------------------------------------------------------------- 카탈로그 기본값

        [Test]
        public void MagicBottle_ExposesApprovedInitialTuningValues()
        {
            ThrowingItemDefinition definition = RecommendedMagicBottleCatalog.MagicBottle;

            Assert.That(definition.ItemId, Is.EqualTo(2103));
            Assert.That(definition.DisplayName, Is.EqualTo("마력 폭발병"));
            Assert.That(definition.ThrowDistanceMeters, Is.EqualTo(8f));
            Assert.That(definition.Damage, Is.EqualTo(40));
            Assert.That(definition.ExplosionRadiusMeters, Is.EqualTo(2.5f));
            Assert.That(definition.Price, Is.EqualTo(200));
            Assert.That(definition.DropQuantity, Is.EqualTo(1));
            Assert.That(definition.CarryLimit, Is.EqualTo(3));
            Assert.That(definition.FlightSeconds, Is.EqualTo(0.7d));
            Assert.That(definition.WarningSeconds, Is.EqualTo(0.6d));
            Assert.That(definition.SharedCooldownSeconds, Is.EqualTo(2d));
            Assert.That(definition.IsValid, Is.True);
        }

        [Test]
        public void MagicBottle_EquipmentDefinitionIsConsumableWithoutWeaponOrArchetype()
        {
            var catalog = new RecommendedMagicBottleCatalog();

            Assert.That(catalog.TryGet(2103, out EquipmentItemDefinition definition), Is.True);
            Assert.That(definition.ItemId, Is.EqualTo(2103));
            Assert.That(definition.VisualId, Is.EqualTo(5103));
            Assert.That(definition.Kind, Is.EqualTo(EquipmentItemKind.Consumable));
            Assert.That(definition.Archetype, Is.EqualTo(EquipmentArchetype.None));
            Assert.That(definition.GameplayWeapon, Is.EqualTo(WeaponType.None));
            Assert.That(definition.MaxStack, Is.EqualTo(3));

            Assert.That(catalog.TryGetCarryLimit(2103, out int carryLimit), Is.True);
            Assert.That(carryLimit, Is.EqualTo(3));

            Assert.That(catalog.TryGet(9999, out _), Is.False);
            Assert.That(catalog.TryGetThrowing(9999, out _), Is.False);
            Assert.That(catalog.TryGetCarryLimit(9999, out int missingLimit), Is.False);
            Assert.That(missingLimit, Is.EqualTo(0));
        }

        [Test]
        public void MagicBottle_ShopOfferUsesTheRecommendedPriceAndSingleBottleQuantity()
        {
            ShopCatalog shop = PlaceholderShopCatalog.Create();

            Assert.That(shop.TryGet(3, out ShopOffer offer), Is.True);
            Assert.That(offer.ItemId, Is.EqualTo(RecommendedMagicBottleCatalog.MagicBottleItemId));
            Assert.That(offer.Price, Is.EqualTo(RecommendedMagicBottleCatalog.MagicBottlePrice));
            Assert.That(offer.Quantity, Is.EqualTo(RecommendedMagicBottleCatalog.MagicBottleDropQuantity));
        }

        [Test]
        public void ThrowingDefinition_RejectsDefaultAndNonFiniteAndNegativeValues()
        {
            Assert.That(default(ThrowingItemDefinition).IsValid, Is.False);
            Assert.That(WithWarningSeconds(double.NaN).IsValid, Is.False);
            Assert.That(WithWarningSeconds(double.PositiveInfinity).IsValid, Is.False);
            Assert.That(WithWarningSeconds(-1d).IsValid, Is.False);
            Assert.That(WithCooldownSeconds(double.NegativeInfinity).IsValid, Is.False);
            Assert.That(WithCooldownSeconds(-0.5d).IsValid, Is.False);
            Assert.That(WithRadiusMeters(float.NaN).IsValid, Is.False);
            Assert.That(WithRadiusMeters(-2.5f).IsValid, Is.False);
            Assert.That(WithDisplayName(null).IsValid, Is.False);
            Assert.That(WithDisplayName(string.Empty).IsValid, Is.False);
        }

        // ---------------------------------------------------------------- 인벤토리 총 보유 상한

        [Test]
        public void CarryLimit_RejectsPickupBeyondTotalAcrossAllSlots()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            var inventory = new EquipmentInventory();

            for (int i = 0; i < 3; i++)
            {
                Assert.That(inventory.TryPickup(2103, 1, catalog).Succeeded, Is.True);
            }

            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(3));

            EquipmentInventoryOperationResult rejected = inventory.TryPickup(2103, 1, catalog);
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Failure, Is.EqualTo(EquipmentInventoryFailure.CarryLimitExceeded));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(3));
        }

        [Test]
        public void CarryLimit_CountsSameItemSplitAcrossSlotsTogether()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            var inventory = new EquipmentInventory();
            inventory.ApplySnapshot(new EquipmentInventorySnapshot(
                0,
                0,
                new EquipmentInventorySlot(2103, 1),
                default,
                new EquipmentInventorySlot(2103, 1)));

            // 스택 여유는 넉넉하지만 합계 상한 3을 넘기므로 부분 수용 없이 통째로 거부된다.
            EquipmentInventoryOperationResult rejected = inventory.TryPickup(2103, 2, catalog);
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Failure, Is.EqualTo(EquipmentInventoryFailure.CarryLimitExceeded));
            Assert.That(rejected.Snapshot.ConsumableSlot0.Count, Is.EqualTo(1));
            Assert.That(rejected.Snapshot.ConsumableSlot1.IsEmpty, Is.True);
            Assert.That(rejected.Snapshot.ConsumableSlot2.Count, Is.EqualTo(1));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(2));

            Assert.That(inventory.TryPickup(2103, 1, catalog).Succeeded, Is.True);
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(3));
        }

        [Test]
        public void CarryLimit_InvalidCapIsRejectedWithoutWrites()
        {
            var catalog = new FakeThrowingCatalog { CarryLimitOverride = 0 };
            var inventory = new EquipmentInventory();

            EquipmentInventoryOperationResult rejected = inventory.TryPickup(2103, 1, catalog);
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Failure, Is.EqualTo(EquipmentInventoryFailure.InvalidItem));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(0));

            catalog.CarryLimitOverride = -5;
            EquipmentInventoryOperationResult negative = inventory.TryPickup(2103, 1, catalog);
            Assert.That(negative.Succeeded, Is.False);
            Assert.That(negative.Failure, Is.EqualTo(EquipmentInventoryFailure.InvalidItem));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(0));
        }

        [Test]
        public void CarryLimit_CatalogWithoutLimitKeepsExistingStackOnlyBehaviour()
        {
            var catalog = new PlainConsumableCatalog();
            var inventory = new EquipmentInventory();

            for (int i = 0; i < 9; i++)
            {
                Assert.That(inventory.TryPickup(2103, 1, catalog).Succeeded, Is.True);
            }

            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(9));

            EquipmentInventoryOperationResult full = inventory.TryPickup(2103, 1, catalog);
            Assert.That(full.Succeeded, Is.False);
            Assert.That(full.Failure, Is.EqualTo(EquipmentInventoryFailure.ConsumableCapacityFull));
        }

        // ---------------------------------------------------------------- 사용 승인 게이트

        [Test]
        public void Gate_RequiresNonNullCollaborators()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            Assert.Throws<ArgumentNullException>(() => new ThrowUseGate(OwnerId, null, catalog, catalog));
            Assert.Throws<ArgumentNullException>(
                () => new ThrowUseGate(OwnerId, new EquipmentInventory(), null, catalog));
            Assert.Throws<ArgumentNullException>(
                () => new ThrowUseGate(OwnerId, new EquipmentInventory(), catalog, null));
        }

        [Test]
        public void Gate_RejectsEveryRequestBeforeBeginRound()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 3);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);

            ThrowUseResult result = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 1),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 100d));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(ThrowUseFailure.RoundNotStarted));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(3));
            Assert.That(gate.CooldownReadyAtSeconds, Is.EqualTo(0d));
        }

        [Test]
        public void Gate_CombatUseConsumesExactlyOneAndArmsSharedCooldown()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 3);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            Assert.That(gate.BeginRound(1, 100d), Is.True);

            ThrowUseResult result = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 1),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 100d));

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Accepted.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(result.Accepted.RoundNumber, Is.EqualTo(1));
            Assert.That(result.Accepted.ItemId, Is.EqualTo(2103));
            Assert.That(result.Accepted.ConsumableSlotIndex, Is.EqualTo(0));
            Assert.That(result.Accepted.ConsumedQuantity, Is.EqualTo(1));
            Assert.That(result.Accepted.AcceptedAtSeconds, Is.EqualTo(100d));
            Assert.That(result.Accepted.CooldownReadyAtSeconds, Is.EqualTo(102d));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(2));
            Assert.That(gate.IsCooldownActive(101.9d), Is.True);
            Assert.That(gate.IsCooldownActive(102d), Is.False);
        }

        [Test]
        public void Gate_SharedCooldownCannotBeBypassedByOtherSlotOrOtherThrowingItem()
        {
            var catalog = new FakeThrowingCatalog();
            var inventory = new EquipmentInventory();
            inventory.ApplySnapshot(new EquipmentInventorySnapshot(
                0,
                0,
                new EquipmentInventorySlot(2103, 1),
                new EquipmentInventorySlot(2103, 1),
                new EquipmentInventorySlot(OtherThrowingItemId, 1)));
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(1, 100d);

            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 0, 1),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, 100d)).Succeeded,
                Is.True);

            ThrowUseResult otherSlot = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 1, 2),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 101d));
            Assert.That(otherSlot.Succeeded, Is.False);
            Assert.That(otherSlot.Failure, Is.EqualTo(ThrowUseFailure.CooldownActive));

            ThrowUseResult otherItem = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 2, 3),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 101.5d));
            Assert.That(otherItem.Succeeded, Is.False);
            Assert.That(otherItem.Failure, Is.EqualTo(ThrowUseFailure.CooldownActive));

            // 거부된 요청은 수량과 쿨다운을 그대로 둔다.
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(1));
            Assert.That(TotalCount(inventory, OtherThrowingItemId), Is.EqualTo(1));
            Assert.That(gate.CooldownReadyAtSeconds, Is.EqualTo(102d));
        }

        [Test]
        public void Gate_AcceptsExactlyAtCooldownBoundary()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 3);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(1, 100d);
            gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 1),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 100d));

            ThrowUseResult tooEarly = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 2),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 101.999d));
            Assert.That(tooEarly.Failure, Is.EqualTo(ThrowUseFailure.CooldownActive));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(2));

            ThrowUseResult atBoundary = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 3),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 102d));
            Assert.That(atBoundary.Succeeded, Is.True);
            Assert.That(atBoundary.Accepted.CooldownReadyAtSeconds, Is.EqualTo(104d));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(1));
        }

        [Test]
        public void Gate_PreparationAdmitsWithoutConsumingButStillArmsCooldown()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 3);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(1, 10d);

            ThrowUseResult result = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 1),
                new ThrowRoundContext(1, ThrowRoundPhase.Preparation, 10d));

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Accepted.Phase, Is.EqualTo(ThrowRoundPhase.Preparation));
            Assert.That(result.Accepted.ConsumedQuantity, Is.EqualTo(0));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(3));
            Assert.That(gate.CooldownReadyAtSeconds, Is.EqualTo(12d));
        }

        [Test]
        public void Gate_RejectsPhasesOtherThanPreparationAndCombat()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 3);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(1, 10d);

            ThrowUseResult resolution = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 1),
                new ThrowRoundContext(1, ThrowRoundPhase.Resolution, 10d));
            Assert.That(resolution.Failure, Is.EqualTo(ThrowUseFailure.PhaseNotAllowed));

            ThrowUseResult none = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 2),
                new ThrowRoundContext(1, ThrowRoundPhase.None, 11d));
            Assert.That(none.Failure, Is.EqualTo(ThrowUseFailure.PhaseNotAllowed));

            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(3));
            Assert.That(gate.CooldownReadyAtSeconds, Is.EqualTo(10d));
        }

        [Test]
        public void Gate_RejectsStaleRoundDuplicateAndOutOfOrderRequests()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 3);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(2, 50d);

            ThrowUseResult staleRequestRound = gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 1),
                new ThrowRoundContext(2, ThrowRoundPhase.Combat, 50d));
            Assert.That(staleRequestRound.Failure, Is.EqualTo(ThrowUseFailure.StaleRound));

            ThrowUseResult staleContextRound = gate.TryUse(
                new ThrowUseRequest(OwnerId, 2, 0, 1),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 50d));
            Assert.That(staleContextRound.Failure, Is.EqualTo(ThrowUseFailure.StaleRound));

            ThrowUseResult otherOwner = gate.TryUse(
                new ThrowUseRequest(OwnerId + 1, 2, 0, 1),
                new ThrowRoundContext(2, ThrowRoundPhase.Combat, 50d));
            Assert.That(otherOwner.Failure, Is.EqualTo(ThrowUseFailure.OwnerMismatch));
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(3));

            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 2, 0, 5),
                    new ThrowRoundContext(2, ThrowRoundPhase.Combat, 50d)).Succeeded,
                Is.True);
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(2));

            ThrowUseResult duplicate = gate.TryUse(
                new ThrowUseRequest(OwnerId, 2, 0, 5),
                new ThrowRoundContext(2, ThrowRoundPhase.Combat, 60d));
            Assert.That(duplicate.Failure, Is.EqualTo(ThrowUseFailure.DuplicateOrOutOfOrderRequest));

            ThrowUseResult outOfOrder = gate.TryUse(
                new ThrowUseRequest(OwnerId, 2, 0, 4),
                new ThrowRoundContext(2, ThrowRoundPhase.Combat, 61d));
            Assert.That(outOfOrder.Failure, Is.EqualTo(ThrowUseFailure.DuplicateOrOutOfOrderRequest));

            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(2));
            Assert.That(gate.CooldownReadyAtSeconds, Is.EqualTo(52d));
        }

        [Test]
        public void Gate_BeginRoundResetsCooldownAndSequenceAndRejectsBackwardRounds()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 3);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(1, 100d);
            gate.TryUse(
                new ThrowUseRequest(OwnerId, 1, 0, 9),
                new ThrowRoundContext(1, ThrowRoundPhase.Combat, 100d));
            Assert.That(gate.CooldownReadyAtSeconds, Is.EqualTo(102d));

            Assert.That(gate.BeginRound(1, 200d), Is.False);
            Assert.That(gate.BeginRound(0, 200d), Is.False);
            Assert.That(gate.BeginRound(2, double.NaN), Is.False);
            Assert.That(gate.BeginRound(2, 50d), Is.False);

            Assert.That(gate.BeginRound(2, 200d), Is.True);
            Assert.That(gate.CooldownReadyAtSeconds, Is.EqualTo(200d));
            Assert.That(gate.LastRequestSequence, Is.EqualTo(0));

            // 라운드가 바뀌면 이전 라운드보다 작은 일련번호도 다시 쓸 수 있고 쿨다운도 새로 시작한다.
            ThrowUseResult afterReset = gate.TryUse(
                new ThrowUseRequest(OwnerId, 2, 0, 1),
                new ThrowRoundContext(2, ThrowRoundPhase.Combat, 200d));
            Assert.That(afterReset.Succeeded, Is.True);
            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(1));
        }

        [Test]
        public void Gate_RejectsNonFiniteAndBackwardTimeWithoutMutation()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 3);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(1, 100d);

            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 0, 1),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, double.NaN)).Failure,
                Is.EqualTo(ThrowUseFailure.InvalidTime));
            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 0, 1),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, double.PositiveInfinity)).Failure,
                Is.EqualTo(ThrowUseFailure.InvalidTime));
            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 0, 1),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, 99d)).Failure,
                Is.EqualTo(ThrowUseFailure.InvalidTime));

            Assert.That(TotalCount(inventory, 2103), Is.EqualTo(3));
            Assert.That(gate.LastRequestSequence, Is.EqualTo(0));

            // 무효 시각 요청은 일련번호를 소모하지 않으므로 같은 번호로 정상 재시도할 수 있다.
            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 0, 1),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, 100d)).Succeeded,
                Is.True);
        }

        [Test]
        public void Gate_RejectsEmptySlotAndNonThrowableAndInvalidDefinition()
        {
            var catalog = new FakeThrowingCatalog();
            var inventory = new EquipmentInventory();
            inventory.ApplySnapshot(new EquipmentInventorySnapshot(
                0,
                0,
                default,
                new EquipmentInventorySlot(FakeThrowingCatalog.PotionItemId, 1),
                new EquipmentInventorySlot(FakeThrowingCatalog.BrokenThrowingItemId, 1)));
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(1, 0d);

            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 0, 1),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, 0d)).Failure,
                Is.EqualTo(ThrowUseFailure.InvalidSlot));
            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 3, 2),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, 0d)).Failure,
                Is.EqualTo(ThrowUseFailure.InvalidSlot));
            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 1, 3),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, 0d)).Failure,
                Is.EqualTo(ThrowUseFailure.SlotDoesNotContainThrowable));
            Assert.That(
                gate.TryUse(
                    new ThrowUseRequest(OwnerId, 1, 2, 4),
                    new ThrowRoundContext(1, ThrowRoundPhase.Combat, 0d)).Failure,
                Is.EqualTo(ThrowUseFailure.InvalidDefinition));

            Assert.That(TotalCount(inventory, FakeThrowingCatalog.PotionItemId), Is.EqualTo(1));
            Assert.That(TotalCount(inventory, FakeThrowingCatalog.BrokenThrowingItemId), Is.EqualTo(1));
            Assert.That(gate.CooldownReadyAtSeconds, Is.EqualTo(0d));
        }

        // ---------------------------------------------------------------- 투척체 수명

        [Test]
        public void Projectile_NormalFuseAnchorsAtFirstLandAndExplodesOnce()
        {
            ThrownProjectile projectile = Launched(100d);

            Assert.That(projectile.State, Is.EqualTo(ThrownProjectileState.InFlight));
            Assert.That(projectile.IsFuseStarted, Is.False);
            Assert.That(projectile.ExpectedLandingTimeSeconds, Is.EqualTo(100.7d).Within(1e-9));

            // 비행 시간이 지나도 착지 보고 전에는 퓨즈가 시작되지 않는다.
            Assert.That(projectile.Advance(105d).State, Is.EqualTo(ThrownProjectileState.InFlight));

            ProjectileUpdateResult landed = projectile.ReportLanded(106d);
            Assert.That(landed.Accepted, Is.True);
            Assert.That(landed.HasExplosion, Is.False);
            Assert.That(landed.State, Is.EqualTo(ThrownProjectileState.Warning));
            Assert.That(projectile.FuseStartedAtSeconds, Is.EqualTo(106d));
            Assert.That(projectile.FuseEndsAtSeconds, Is.EqualTo(106.6d).Within(1e-9));

            Assert.That(projectile.Advance(106.5d).HasExplosion, Is.False);

            ProjectileUpdateResult exploded = projectile.Advance(107d);
            Assert.That(exploded.HasExplosion, Is.True);
            Assert.That(exploded.State, Is.EqualTo(ThrownProjectileState.Exploded));
            Assert.That(exploded.Explosion.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(exploded.Explosion.OriginRoundNumber, Is.EqualTo(3));
            Assert.That(exploded.Explosion.ItemId, Is.EqualTo(2103));
            Assert.That(exploded.Explosion.Damage, Is.EqualTo(40));
            Assert.That(exploded.Explosion.RadiusMeters, Is.EqualTo(2.5f));
            Assert.That(exploded.Explosion.CanDamage, Is.True);
            Assert.That(exploded.Explosion.TimeSeconds, Is.EqualTo(107d));

            ProjectileUpdateResult afterwards = projectile.Advance(120d);
            Assert.That(afterwards.Accepted, Is.False);
            Assert.That(afterwards.Failure, Is.EqualTo(ProjectileUpdateFailure.AlreadyExploded));
            Assert.That(afterwards.HasExplosion, Is.False);
        }

        [Test]
        public void Projectile_SecondLandCannotMoveAnchorOrExtendTimer()
        {
            ThrownProjectile projectile = Launched(0d);
            projectile.ReportLanded(1d);

            ProjectileUpdateResult secondLand = projectile.ReportLanded(1.5d);
            Assert.That(secondLand.Accepted, Is.False);
            Assert.That(secondLand.Failure, Is.EqualTo(ProjectileUpdateFailure.AlreadyLanded));
            Assert.That(projectile.LandedAtSeconds, Is.EqualTo(1d));
            Assert.That(projectile.FuseStartedAtSeconds, Is.EqualTo(1d));
            Assert.That(projectile.FuseEndsAtSeconds, Is.EqualTo(1.6d).Within(1e-9));

            Assert.That(projectile.Advance(2d).HasExplosion, Is.True);
        }

        [Test]
        public void Projectile_DeathDropFuseStartsAtDropAndWaitsForLandingToExplode()
        {
            Assert.That(
                ThrownProjectile.TryCreateDeathDrop(
                    OwnerId,
                    3,
                    RecommendedMagicBottleCatalog.MagicBottle,
                    50d,
                    out ThrownProjectile projectile),
                Is.True);

            Assert.That(projectile.Origin, Is.EqualTo(ThrownProjectileOrigin.DeathDrop));
            Assert.That(projectile.IsFuseStarted, Is.True);
            Assert.That(projectile.FuseStartedAtSeconds, Is.EqualTo(50d));
            Assert.That(projectile.FuseEndsAtSeconds, Is.EqualTo(50.6d).Within(1e-9));
            Assert.That(projectile.State, Is.EqualTo(ThrownProjectileState.Warning));

            // 낙하 중 퓨즈가 만료돼도 착지 전에는 폭발을 통보하지 않는다.
            ProjectileUpdateResult falling = projectile.Advance(52d);
            Assert.That(falling.HasExplosion, Is.False);
            Assert.That(projectile.HasExploded, Is.False);
            Assert.That(projectile.IsAwaitingLandingToExplode, Is.True);

            ProjectileUpdateResult landed = projectile.ReportLanded(53d);
            Assert.That(landed.HasExplosion, Is.True);
            Assert.That(landed.Explosion.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(landed.Explosion.OriginRoundNumber, Is.EqualTo(3));
            Assert.That(landed.Explosion.TimeSeconds, Is.EqualTo(53d));

            // 소유자가 죽어 떨어뜨린 뒤에도 소유자와 발생 라운드는 바뀌지 않는다.
            Assert.That(projectile.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(projectile.OriginRoundNumber, Is.EqualTo(3));
            Assert.That(projectile.Advance(60d).HasExplosion, Is.False);
        }

        [Test]
        public void Projectile_EndRoundPermanentlyDisablesDamageButKeepsLifetime()
        {
            ThrownProjectile projectile = Launched(0d);
            projectile.ReportLanded(1d);

            projectile.EndRound();
            Assert.That(projectile.CanDamage, Is.False);

            // 라운드 종료 뒤 다시 전투를 켜도 피해는 되살아나지 않는다.
            projectile.SetDamageEnabled(true);
            Assert.That(projectile.CanDamage, Is.False);

            ProjectileUpdateResult exploded = projectile.Advance(2d);
            Assert.That(exploded.HasExplosion, Is.True);
            Assert.That(exploded.Explosion.CanDamage, Is.False);
            Assert.That(exploded.Explosion.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(exploded.Explosion.OriginRoundNumber, Is.EqualTo(3));
            Assert.That(projectile.State, Is.EqualTo(ThrownProjectileState.Exploded));
        }

        [Test]
        public void Projectile_DamageToggleWorksBeforeRoundEnd()
        {
            ThrownProjectile projectile = Launched(0d);
            projectile.SetDamageEnabled(false);
            Assert.That(projectile.CanDamage, Is.False);

            projectile.SetDamageEnabled(true);
            Assert.That(projectile.CanDamage, Is.True);

            projectile.ReportLanded(1d);
            Assert.That(projectile.Advance(1d).HasExplosion, Is.False);
            Assert.That(projectile.Advance(2d).Explosion.CanDamage, Is.True);
        }

        [Test]
        public void Projectile_RejectsNonFiniteAndBackwardTimeSafely()
        {
            ThrownProjectile projectile = Launched(10d);

            Assert.That(projectile.Advance(double.NaN).Failure, Is.EqualTo(ProjectileUpdateFailure.InvalidTime));
            Assert.That(
                projectile.Advance(double.NegativeInfinity).Failure,
                Is.EqualTo(ProjectileUpdateFailure.InvalidTime));
            Assert.That(projectile.Advance(9d).Failure, Is.EqualTo(ProjectileUpdateFailure.InvalidTime));
            Assert.That(projectile.ReportLanded(9d).Failure, Is.EqualTo(ProjectileUpdateFailure.InvalidTime));
            Assert.That(projectile.HasLanded, Is.False);
            Assert.That(projectile.IsFuseStarted, Is.False);

            projectile.Advance(20d);
            Assert.That(projectile.ReportLanded(19d).Failure, Is.EqualTo(ProjectileUpdateFailure.InvalidTime));
            Assert.That(projectile.HasLanded, Is.False);

            Assert.That(projectile.ReportLanded(20d).Accepted, Is.True);
            Assert.That(projectile.FuseEndsAtSeconds, Is.EqualTo(20.6d).Within(1e-9));
        }

        [Test]
        public void Projectile_RejectsInvalidCreationArguments()
        {
            ThrowingItemDefinition valid = RecommendedMagicBottleCatalog.MagicBottle;

            Assert.That(
                ThrownProjectile.TryCreateLaunched(OwnerId, 1, default, 0d, out ThrownProjectile fromDefault),
                Is.False);
            Assert.That(fromDefault, Is.Null);

            Assert.That(
                ThrownProjectile.TryCreateLaunched(OwnerId, 1, WithWarningSeconds(double.NaN), 0d, out _),
                Is.False);
            Assert.That(ThrownProjectile.TryCreateLaunched(OwnerId, 1, valid, double.NaN, out _), Is.False);
            Assert.That(
                ThrownProjectile.TryCreateLaunched(OwnerId, 1, valid, double.PositiveInfinity, out _),
                Is.False);
            Assert.That(ThrownProjectile.TryCreateLaunched(-1, 1, valid, 0d, out _), Is.False);
            Assert.That(ThrownProjectile.TryCreateLaunched(OwnerId, 0, valid, 0d, out _), Is.False);
            Assert.That(ThrownProjectile.TryCreateDeathDrop(OwnerId, 0, valid, 0d, out _), Is.False);
        }

        [Test]
        public void Projectile_CreatedFromAcceptedThrowKeepsOwnerAndRound()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            EquipmentInventory inventory = InventoryWithBottles(catalog, 1);
            var gate = new ThrowUseGate(OwnerId, inventory, catalog, catalog);
            gate.BeginRound(4, 30d);

            ThrowUseResult use = gate.TryUse(
                new ThrowUseRequest(OwnerId, 4, 0, 1),
                new ThrowRoundContext(4, ThrowRoundPhase.Combat, 30d));
            Assert.That(use.Succeeded, Is.True);

            Assert.That(ThrownProjectile.TryCreateLaunched(use.Accepted, out ThrownProjectile projectile), Is.True);
            Assert.That(projectile.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(projectile.OriginRoundNumber, Is.EqualTo(4));
            Assert.That(projectile.CreatedAtSeconds, Is.EqualTo(30d));
            Assert.That(projectile.ItemId, Is.EqualTo(2103));

            projectile.ReportLanded(31d);
            ProjectileUpdateResult explosion = projectile.Advance(32d);
            Assert.That(explosion.HasExplosion, Is.True);
            Assert.That(explosion.Explosion.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(explosion.Explosion.OriginRoundNumber, Is.EqualTo(4));
        }

        // ---------------------------------------------------------------- 도우미

        private static ThrownProjectile Launched(double launchTimeSeconds)
        {
            Assert.That(
                ThrownProjectile.TryCreateLaunched(
                    OwnerId,
                    3,
                    RecommendedMagicBottleCatalog.MagicBottle,
                    launchTimeSeconds,
                    out ThrownProjectile projectile),
                Is.True);
            return projectile;
        }

        private static EquipmentInventory InventoryWithBottles(IEquipmentItemCatalog catalog, int count)
        {
            var inventory = new EquipmentInventory();
            for (int i = 0; i < count; i++)
            {
                Assert.That(inventory.TryPickup(2103, 1, catalog).Succeeded, Is.True);
            }

            return inventory;
        }

        private static int TotalCount(EquipmentInventory inventory, int itemId)
        {
            EquipmentInventorySnapshot snapshot = inventory.Snapshot;
            int total = 0;
            for (int i = 0; i < EquipmentInventorySnapshot.ConsumableSlotCount; i++)
            {
                EquipmentInventorySlot slot = snapshot.GetConsumableSlot(i);
                if (!slot.IsEmpty && slot.ItemId == itemId)
                {
                    total += slot.Count;
                }
            }

            return total;
        }

        private static ThrowingItemDefinition WithWarningSeconds(double warningSeconds) =>
            Rebuild(warningSeconds: warningSeconds);

        private static ThrowingItemDefinition WithCooldownSeconds(double cooldownSeconds) =>
            Rebuild(cooldownSeconds: cooldownSeconds);

        private static ThrowingItemDefinition WithRadiusMeters(float radiusMeters) =>
            Rebuild(radiusMeters: radiusMeters);

        private static ThrowingItemDefinition WithDisplayName(string displayName) =>
            Rebuild(displayName: displayName, hasDisplayName: true);

        private static ThrowingItemDefinition Rebuild(
            double warningSeconds = 1d,
            double cooldownSeconds = 2d,
            float radiusMeters = 2.5f,
            string displayName = null,
            bool hasDisplayName = false) =>
            new(
                2103,
                hasDisplayName ? displayName : "마력 폭발병",
                8f,
                40,
                radiusMeters,
                200,
                1,
                3,
                1d,
                warningSeconds,
                cooldownSeconds);

        /// <summary>여러 투척 항목과 잘못된 정의를 함께 제공하는 테스트 전용 카탈로그.</summary>
        private sealed class FakeThrowingCatalog
            : IEquipmentItemCatalog, IEquipmentItemCarryLimitCatalog, IThrowingItemCatalog
        {
            public const int PotionItemId = 2001;
            public const int BrokenThrowingItemId = 2999;

            /// <summary>0 이하로 두면 잘못된 상한 설정을 흉내 낸다.</summary>
            public int CarryLimitOverride = 3;

            public bool TryGet(int itemId, out EquipmentItemDefinition definition)
            {
                switch (itemId)
                {
                    case 2103:
                    case OtherThrowingItemId:
                    case PotionItemId:
                    case BrokenThrowingItemId:
                        definition = new EquipmentItemDefinition(
                            itemId,
                            EquipmentItemKind.Consumable,
                            EquipmentArchetype.None,
                            5000 + itemId,
                            WeaponType.None,
                            3);
                        return true;
                    default:
                        definition = default;
                        return false;
                }
            }

            public bool TryGetCarryLimit(int itemId, out int totalCarryLimit)
            {
                if (itemId == 2103 || itemId == OtherThrowingItemId)
                {
                    totalCarryLimit = CarryLimitOverride;
                    return true;
                }

                totalCarryLimit = 0;
                return false;
            }

            public bool TryGetThrowing(int itemId, out ThrowingItemDefinition definition)
            {
                switch (itemId)
                {
                    case 2103:
                        definition = RecommendedMagicBottleCatalog.MagicBottle;
                        return true;
                    case OtherThrowingItemId:
                        definition = new ThrowingItemDefinition(
                            OtherThrowingItemId, "테스트 투척병", 8f, 40, 2.5f, 200, 1, 3, 1d, 1d, 2d);
                        return true;
                    case BrokenThrowingItemId:
                        // 무효 정의(음수 경고 시간)는 승인 단계에서 걸러져야 한다.
                        definition = new ThrowingItemDefinition(
                            BrokenThrowingItemId, "고장난 투척병", 8f, 40, 2.5f, 200, 1, 3, 1d, -1d, 2d);
                        return true;
                    default:
                        definition = default;
                        return false;
                }
            }
        }

        /// <summary>총 보유 상한을 제공하지 않는 기존 형태의 카탈로그.</summary>
        private sealed class PlainConsumableCatalog : IEquipmentItemCatalog
        {
            public bool TryGet(int itemId, out EquipmentItemDefinition definition)
            {
                if (itemId == 2103)
                {
                    definition = new EquipmentItemDefinition(
                        2103,
                        EquipmentItemKind.Consumable,
                        EquipmentArchetype.None,
                        5103,
                        WeaponType.None,
                        3);
                    return true;
                }

                definition = default;
                return false;
            }
        }
    }
}
