using System;
using System.Collections.Generic;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 수령 한 건의 실제 지급 — 골드·아이템·과잉 보상이 어디로 가는가
    /// (S-GD-DDXPX9 §1·§3·§4 · S-GD-RVVB4H §3 · S-GD-ZPV7Q4 · S-GD-DFG3VR).
    /// </summary>
    /// <remarks>
    /// 인벤토리는 <b>실제 규칙</b>(<see cref="EquipmentInventory"/> + 제품 폴백 카탈로그)을 쓴다 —
    /// 칸 부족·보유 한도 판정을 가짜로 흉내 내면 "받지 못함"(S-GD-ZPV7Q4)을 검증한 것이 아니다.
    /// </remarks>
    public class ChestRewardPayoutTests
    {
        private const int Alice = 1;
        private const int ChestId = 0;

        /// <summary>실제 인벤토리 규칙 + 효과 기록. 제품의 <c>EquipmentInventoryBehaviour</c> 자리다.</summary>
        private sealed class RecordingInventory : IEquipmentInventoryDriver, IConsumableEffectReceiver
        {
            private readonly EquipmentInventory inventory = new();
            public readonly List<ConsumableEffect> AppliedEffects = new();
            public bool AcceptsEffects = true;

            public EquipmentInventorySnapshot EquipmentSnapshot => inventory.Snapshot;

            public EquipmentInventoryOperationResult TryAuthorityPickup(int itemId, int quantity) =>
                inventory.TryPickup(itemId, quantity, PreparationShopItemFallbackCatalog.Instance);

            public EquipmentInventoryOperationResult TryAuthorityDropWeapon() => inventory.TryDropWeapon();

            public EquipmentInventoryOperationResult TryAuthorityUseConsumable(int slotIndex) =>
                inventory.TryUseConsumable(slotIndex, PreparationShopItemFallbackCatalog.Instance);

            public void ApplyPresentation(in EquipmentInventorySnapshot snapshot) => inventory.ApplySnapshot(snapshot);

            public bool TryAuthorityApplyConsumableEffects(IReadOnlyList<ConsumableEffect> effects, int sourceItemId)
            {
                if (!AcceptsEffects)
                {
                    return false;
                }

                AppliedEffects.AddRange(effects);
                AppliedSources.Add(sourceItemId);
                return true;
            }

            public readonly List<int> AppliedSources = new();

            public int CountOf(int itemId)
            {
                int total = 0;
                for (int i = 0; i < EquipmentInventorySnapshot.ConsumableSlotCount; i++)
                {
                    EquipmentInventorySlot slot = inventory.Snapshot.GetConsumableSlot(i);
                    if (slot.ItemId == itemId)
                    {
                        total += slot.Count;
                    }
                }

                return total;
            }
        }

        /// <summary>인벤토리를 모르는 몸 — 효과를 받을 수 없다.</summary>
        private sealed class InventoryWithoutEffects : IEquipmentInventoryDriver
        {
            private readonly EquipmentInventory inventory = new();

            public EquipmentInventorySnapshot EquipmentSnapshot => inventory.Snapshot;

            public EquipmentInventoryOperationResult TryAuthorityPickup(int itemId, int quantity) =>
                EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.ConsumableCapacityFull, inventory.Snapshot);

            public EquipmentInventoryOperationResult TryAuthorityDropWeapon() => inventory.TryDropWeapon();

            public EquipmentInventoryOperationResult TryAuthorityUseConsumable(int slotIndex) =>
                inventory.TryUseConsumable(slotIndex, PreparationShopItemFallbackCatalog.Instance);

            public void ApplyPresentation(in EquipmentInventorySnapshot snapshot) => inventory.ApplySnapshot(snapshot);
        }

        private sealed class FakeRecipients : IChestRewardRecipients
        {
            public IEquipmentInventoryDriver Inventory;
            public bool CanReceive = true;
            public bool AcceptsGold = true;
            public readonly List<(int PlayerId, int Amount)> GoldGrants = new();
            public int ResolveCalls;

            public bool TryResolveRecipient(int playerId, out IEquipmentInventoryDriver inventory)
            {
                ResolveCalls++;
                inventory = CanReceive ? Inventory : null;
                return CanReceive && Inventory != null;
            }

            public bool TryGrantGold(int playerId, int amount)
            {
                if (!AcceptsGold)
                {
                    return false;
                }

                GoldGrants.Add((playerId, amount));
                return true;
            }
        }

        /// <summary>한 줄짜리 표 — 무엇이 뽑힐지 고정해 전달 방식만 본다.</summary>
        private static ChestLootTable Only(int itemId) => new(
            new[] { new ChestLootEntry(itemId, ChestItemRarity.Common) },
            new ChestRarityWeights(1, 0, 0),
            new ChestRarityWeights(1, 0, 0));

        private static ChestRewardPayout Payout(ChestLootTable table, FakeRecipients recipients, bool isFinalRound = false) =>
            new(table, PlaceholderChestLootTable.RewardGold, isFinalRound, new DeterministicRandom(7), recipients);

        [Test]
        public void 인벤토리가_받으면_아이템과_골드_300이_들어가고_과잉_보상은_없다()
        {
            var inventory = new RecordingInventory();
            var recipients = new FakeRecipients { Inventory = inventory };

            ChestPayoutResult result = Payout(Only(RecommendedChestItemCatalog.FuryPotionItemId), recipients)
                .Pay(Alice, ChestId);

            Assert.That(result.Paid, Is.True);
            Assert.That(result.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Inventory));
            Assert.That(inventory.CountOf(RecommendedChestItemCatalog.FuryPotionItemId), Is.EqualTo(1));
            Assert.That(recipients.GoldGrants, Is.EqualTo(new[] { (Alice, 300) }));
            Assert.That(result.GoldGranted, Is.True);
            Assert.That(inventory.AppliedEffects, Is.Empty, "받았으면 과잉 보상을 걸지 않는다.");
            Assert.That(result.OverflowApplied, Is.False);
        }

        [Test]
        public void 같은_상자_전용품을_또_뽑고_같은_등급에_다른_것이_없으면_과잉_보상이다()
        {
            // S-GD-RVVB4H §3 — 상자 전용품은 보유 한도 1. 이 표는 한 줄이라 같은 등급에서 다시 뽑을 것이 없다 (S-GD-ZPV7Q4).
            var inventory = new RecordingInventory();
            var recipients = new FakeRecipients { Inventory = inventory };
            ChestRewardPayout payout = Payout(Only(RecommendedChestItemCatalog.ShieldCharmItemId), recipients);

            payout.Pay(Alice, ChestId);
            ChestPayoutResult second = payout.Pay(Alice, ChestId);

            Assert.That(second.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Overflow));
            Assert.That(second.ItemFailure, Is.EqualTo(EquipmentInventoryFailure.CarryLimitExceeded));
            Assert.That(second.IsUnexpectedOverflow, Is.False, "보유 한도는 예정된 과잉 보상이다.");
            Assert.That(inventory.CountOf(RecommendedChestItemCatalog.ShieldCharmItemId), Is.EqualTo(1),
                "한도를 넘겨 쌓이면 안 된다.");
            Assert.That(inventory.AppliedEffects, Is.EqualTo(ShopItemEffectCatalog.ChestOverflowEffects),
                "과잉 보상 네 가지(회복·보호막·공속·이속)를 그대로 건다.");
            Assert.That(inventory.AppliedSources, Is.EqualTo(new[] { ShopItemEffectCatalog.ChestOverflowSourceId }),
                "HUD 가 상자 그림 한 칸으로 묶으려면 근원이 과잉 보상이어야 한다 (S-UX-XEC4CX).");
            Assert.That(recipients.GoldGrants.Count, Is.EqualTo(2), "과잉 보상이어도 골드 300 은 그대로 받는다.");
        }

        [Test]
        public void 소모품_칸이_셋_다_차면_비투척_소모품을_즉시_사용한다()
        {
            var inventory = new RecordingInventory();
            Assert.That(inventory.TryAuthorityPickup(RecommendedChestItemCatalog.SprintPotionItemId, 1).Succeeded, Is.True);
            Assert.That(inventory.TryAuthorityPickup(RecommendedChestItemCatalog.FuryPotionItemId, 1).Succeeded, Is.True);
            Assert.That(inventory.TryAuthorityPickup(RecommendedChestItemCatalog.FrenzyPotionItemId, 1).Succeeded, Is.True);
            var recipients = new FakeRecipients { Inventory = inventory };

            ChestPayoutResult result = Payout(Only(RecommendedChestItemCatalog.BulwarkPotionItemId), recipients)
                .Pay(Alice, ChestId);

            Assert.That(result.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.ImmediateUse));
            Assert.That(result.ItemFailure, Is.EqualTo(EquipmentInventoryFailure.ConsumableCapacityFull));
            Assert.That(result.OverflowApplied, Is.False);
            Assert.That(result.GoldGranted, Is.True);
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(
                RecommendedChestItemCatalog.BulwarkPotionItemId, out var expectedEffects), Is.True);
            Assert.That(inventory.AppliedEffects, Is.EqualTo(expectedEffects));
            Assert.That(inventory.AppliedSources,
                Is.EqualTo(new[] { RecommendedChestItemCatalog.BulwarkPotionItemId }));
        }

        [Test]
        public void 칸이_셋_다_쓰이면_같은_물약을_쌓을_여유가_있어도_즉시_사용한다()
        {
            // S-GD-JQ4E2J — 3칸이 다 차면 기존 스택에 넣지 않고 비투척 소모품 효과를 즉시 적용한다.
            var inventory = new RecordingInventory();
            inventory.TryAuthorityPickup(PlaceholderShopCatalog.HealthPotionItemId, 1);
            inventory.TryAuthorityPickup(RecommendedChestItemCatalog.FuryPotionItemId, 1);
            inventory.TryAuthorityPickup(RecommendedChestItemCatalog.FrenzyPotionItemId, 1);
            var recipients = new FakeRecipients { Inventory = inventory };

            ChestPayoutResult result = Payout(Only(PlaceholderShopCatalog.HealthPotionItemId), recipients)
                .Pay(Alice, ChestId);

            Assert.That(result.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.ImmediateUse));
            Assert.That(result.ItemFailure, Is.EqualTo(EquipmentInventoryFailure.ConsumableCapacityFull));
            Assert.That(result.OverflowApplied, Is.False);
            Assert.That(inventory.CountOf(PlaceholderShopCatalog.HealthPotionItemId), Is.EqualTo(1));
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(
                PlaceholderShopCatalog.HealthPotionItemId, out var expectedEffects), Is.True);
            Assert.That(inventory.AppliedEffects, Is.EqualTo(expectedEffects));
            Assert.That(inventory.AppliedSources,
                Is.EqualTo(new[] { PlaceholderShopCatalog.HealthPotionItemId }));
        }

        [Test]
        public void 빈_칸이_있으면_보유_한도에_걸려도_같은_등급의_다른_아이템을_준다()
        {
            // S-GD-ZPV7Q4 — 체력 1·신속 2 에서 신속이 뽑혀도 버프가 아니라 같은 일반 등급의 체력 물약을 준다.
            var inventory = new RecordingInventory();
            inventory.TryAuthorityPickup(PlaceholderShopCatalog.HealthPotionItemId, 1);
            inventory.TryAuthorityPickup(PlaceholderShopCatalog.SwiftnessPotionItemId, 2);
            var recipients = new FakeRecipients { Inventory = inventory };

            // 첫 뽑기가 신속 물약인 시드를 골라 '다시 뽑기' 경로를 반드시 지나게 한다.
            int seed = SeedWhoseFirstDrawIs(Commons(), PlaceholderShopCatalog.SwiftnessPotionItemId);
            ChestPayoutResult result = new ChestRewardPayout(Commons(), PlaceholderChestLootTable.RewardGold,
                isFinalRound: false, new DeterministicRandom(seed), recipients).Pay(Alice, ChestId);

            Assert.That(result.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Inventory));
            Assert.That(result.Outcome.ItemId, Is.EqualTo(PlaceholderShopCatalog.HealthPotionItemId));
            Assert.That(inventory.CountOf(PlaceholderShopCatalog.HealthPotionItemId), Is.EqualTo(2));
            Assert.That(inventory.CountOf(PlaceholderShopCatalog.SwiftnessPotionItemId), Is.EqualTo(2));
            Assert.That(inventory.AppliedEffects, Is.Empty, "빈 칸이 있는데 버프로 새면 안 된다.");
        }

        [Test]
        public void 체력_3_신속_2에서_일반_등급이_나오면_다시_뽑아도_의미가_없어_과잉_보상이다()
        {
            // 사용자 예시(2026-09-18) — 빈 칸이 하나 있어도 일반 등급 둘이 다 한도라 같은 등급에서 줄 것이 없다 (S-GD-ZPV7Q4).
            var inventory = new RecordingInventory();
            inventory.TryAuthorityPickup(PlaceholderShopCatalog.HealthPotionItemId, 3);
            inventory.TryAuthorityPickup(PlaceholderShopCatalog.SwiftnessPotionItemId, 2);
            var recipients = new FakeRecipients { Inventory = inventory };

            ChestPayoutResult result = Payout(Commons(), recipients).Pay(Alice, ChestId);

            Assert.That(result.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Overflow));
            Assert.That(result.ItemFailure, Is.EqualTo(EquipmentInventoryFailure.CarryLimitExceeded));
            Assert.That(result.IsUnexpectedOverflow, Is.False);
            Assert.That(inventory.CountOf(PlaceholderShopCatalog.HealthPotionItemId), Is.EqualTo(3));
            Assert.That(inventory.CountOf(PlaceholderShopCatalog.SwiftnessPotionItemId), Is.EqualTo(2));
            Assert.That(inventory.AppliedEffects, Is.EqualTo(ShopItemEffectCatalog.ChestOverflowEffects));
        }

        private static int SeedWhoseFirstDrawIs(ChestLootTable table, int itemId)
        {
            for (int seed = 1; seed < 1_000; seed++)
            {
                if (table.Draw(isFinalRound: false, new DeterministicRandom(seed)) == itemId)
                {
                    return seed;
                }
            }

            throw new InvalidOperationException($"{itemId} 가 처음 뽑히는 시드를 찾지 못했다.");
        }

        /// <summary>일반 등급 두 줄 — 체력·신속 물약. 어느 쪽이 먼저 뽑혀도 같은 등급의 다른 한쪽이 남는다.</summary>
        private static ChestLootTable Commons() => new(
            new[]
            {
                new ChestLootEntry(PlaceholderShopCatalog.HealthPotionItemId, ChestItemRarity.Common),
                new ChestLootEntry(PlaceholderShopCatalog.SwiftnessPotionItemId, ChestItemRarity.Common),
            },
            new ChestRarityWeights(1, 0, 0),
            new ChestRarityWeights(1, 0, 0));

        [Test]
        public void 라운드가_끝났거나_받을_몸이_없으면_아무것도_주지_않고_뽑지도_않는다()
        {
            // S-GD-DFG3VR — 개봉 완료와 라운드 종료가 겹치면 라운드 종료가 이긴다.
            var inventory = new RecordingInventory();
            var recipients = new FakeRecipients { Inventory = inventory, CanReceive = false };

            ChestPayoutResult result = Payout(Only(PlaceholderShopCatalog.HealthPotionItemId), recipients)
                .Pay(Alice, ChestId);

            Assert.That(result.Paid, Is.False);
            Assert.That(recipients.GoldGrants, Is.Empty);
            Assert.That(inventory.CountOf(PlaceholderShopCatalog.HealthPotionItemId), Is.Zero);
            Assert.That(inventory.AppliedEffects, Is.Empty);
            Assert.That(result.IsUnexpectedOverflow, Is.False);
        }

        [Test]
        public void 설정_오류로_거절되면_과잉_보상은_주되_예상_밖으로_표시한다()
        {
            // 카탈로그가 모르는 번호 — InvalidItem. 플레이어 몫은 그대로 주고 경고할 근거를 남긴다.
            var inventory = new RecordingInventory();
            var recipients = new FakeRecipients { Inventory = inventory };

            ChestPayoutResult result = Payout(Only(999_999), recipients).Pay(Alice, ChestId);

            Assert.That(result.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Overflow));
            Assert.That(result.ItemFailure, Is.EqualTo(EquipmentInventoryFailure.InvalidItem));
            Assert.That(result.IsUnexpectedOverflow, Is.True);
            Assert.That(result.OverflowApplied, Is.True);
        }

        [Test]
        public void 효과를_받을_수_없는_인벤토리면_과잉_보상이_걸리지_않았다고_알린다()
        {
            var recipients = new FakeRecipients { Inventory = new InventoryWithoutEffects() };

            ChestPayoutResult result = Payout(Only(PlaceholderShopCatalog.HealthPotionItemId), recipients)
                .Pay(Alice, ChestId);

            Assert.That(result.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Overflow));
            Assert.That(result.OverflowApplied, Is.False);
            Assert.That(result.GoldGranted, Is.True);
        }

        [Test]
        public void 골드가_거절되면_결과에_남긴다()
        {
            var recipients = new FakeRecipients { Inventory = new RecordingInventory(), AcceptsGold = false };

            ChestPayoutResult result = Payout(Only(PlaceholderShopCatalog.HealthPotionItemId), recipients)
                .Pay(Alice, ChestId);

            Assert.That(result.Paid, Is.True);
            Assert.That(result.GoldGranted, Is.False);
        }

        [Test]
        public void 마지막_라운드면_마지막_라운드_확률표로_뽑는다()
        {
            // 평소엔 일반만, 마지막 라운드엔 희귀만 나오는 표 — 어느 표를 썼는지 결과로 드러난다.
            var table = new ChestLootTable(
                new[]
                {
                    new ChestLootEntry(PlaceholderShopCatalog.HealthPotionItemId, ChestItemRarity.Common),
                    new ChestLootEntry(RecommendedChestItemCatalog.DashStoneItemId, ChestItemRarity.Epic),
                },
                new ChestRarityWeights(1, 0, 0),
                new ChestRarityWeights(0, 0, 1));

            ChestPayoutResult regular = Payout(table, new FakeRecipients { Inventory = new RecordingInventory() })
                .Pay(Alice, ChestId);
            ChestPayoutResult final = Payout(table, new FakeRecipients { Inventory = new RecordingInventory() },
                isFinalRound: true).Pay(Alice, ChestId);

            Assert.That(regular.Outcome.ItemId, Is.EqualTo(PlaceholderShopCatalog.HealthPotionItemId));
            Assert.That(final.Outcome.ItemId, Is.EqualTo(RecommendedChestItemCatalog.DashStoneItemId));
        }

        [Test]
        public void 상자_추첨표의_12종은_제품_인벤토리가_전부_받는다()
        {
            // 인벤토리 카탈로그가 모르는 번호가 표에 있으면 그 아이템은 언제나 과잉 보상으로 샌다.
            foreach (ChestLootEntry entry in PlaceholderChestLootTable.Entries)
            {
                var inventory = new RecordingInventory();
                ChestPayoutResult result = Payout(Only(entry.ItemId), new FakeRecipients { Inventory = inventory })
                    .Pay(Alice, ChestId);

                Assert.That(result.Outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Inventory),
                    $"{entry.ItemId} 가 빈 인벤토리에 들어가지 않았다 ({result.ItemFailure}).");
                Assert.That(inventory.CountOf(entry.ItemId), Is.EqualTo(1));
            }
        }
    }
}
