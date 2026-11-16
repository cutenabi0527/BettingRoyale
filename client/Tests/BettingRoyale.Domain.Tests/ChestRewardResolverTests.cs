using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 수령 결과 — 골드 300 · 아이템 하나 · 못 받으면 과잉 보상 (S-GD-DDXPX9 §1·§4 · S-GD-ZPV7Q4).
    /// </summary>
    public class ChestRewardResolverTests
    {
        private sealed class FakeReceiver : IChestItemReceiver
        {
            private readonly bool accepts;
            private readonly HashSet<int> atLimit = new();
            public readonly List<int> Offered = new();
            public ChestRewardDelivery FullInventoryDelivery = ChestRewardDelivery.ImmediateUse;

            public FakeReceiver(bool accepts, bool isFull = false, params int[] atLimit)
            {
                this.accepts = accepts;
                IsFull = isFull;
                this.atLimit.UnionWith(atLimit);
            }

            public bool IsFull { get; }

            public bool TryReceiveItem(int itemId)
            {
                Offered.Add(itemId);
                return accepts && !atLimit.Contains(itemId);
            }

            public ChestRewardDelivery ResolveFullInventoryItem(int itemId)
            {
                Offered.Add(itemId);
                return FullInventoryDelivery;
            }
        }

        /// <summary>한 줄짜리 표 — 무엇이 뽑힐지 고정해 전달 방식만 본다.</summary>
        private static ChestLootTable Only(int itemId) => new(
            new[] { new ChestLootEntry(itemId, ChestItemRarity.Common) },
            new ChestRarityWeights(1, 0, 0),
            new ChestRarityWeights(1, 0, 0));

        private static ChestRewardOutcome Resolve(int itemId, FakeReceiver receiver) =>
            ChestRewardResolver.Resolve(Only(itemId), PlaceholderChestLootTable.RewardGold,
                isFinalRound: false, new DeterministicRandom(1), receiver);

        [Test]
        public void 상자_골드는_기획_엑셀대로_300이다()
        {
            // S-GD-BPB9S3 — 엑셀 CHEST_REWARD_GOLD 300. 한때 200 으로 내렸던 값(S-GD-DDXPX9)을 되돌렸다.
            Assert.That(PlaceholderChestLootTable.RewardGold, Is.EqualTo(300));
        }

        [Test]
        public void 인벤토리가_받으면_아이템이_들어가고_골드도_받는다()
        {
            var receiver = new FakeReceiver(accepts: true);

            ChestRewardOutcome outcome = Resolve(RecommendedChestItemCatalog.FuryPotionItemId, receiver);

            Assert.That(outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Inventory));
            Assert.That(outcome.ItemId, Is.EqualTo(RecommendedChestItemCatalog.FuryPotionItemId));
            Assert.That(outcome.Gold, Is.EqualTo(300));
            Assert.That(receiver.Offered, Is.EqualTo(new[] { RecommendedChestItemCatalog.FuryPotionItemId }));
        }

        [Test]
        public void 인벤토리가_받지_못하면_과잉_보상이고_골드는_그대로_받는다()
        {
            var receiver = new FakeReceiver(accepts: false);

            ChestRewardOutcome outcome = Resolve(PlaceholderShopCatalog.HealthPotionItemId, receiver);

            Assert.That(outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Overflow));
            Assert.That(outcome.Gold, Is.EqualTo(300), "과잉 보상이어도 골드 300 은 그대로다.");
            Assert.That(outcome.ItemId, Is.EqualTo(PlaceholderShopCatalog.HealthPotionItemId),
                "무엇을 못 받았는지 남겨야 화면이 알릴 수 있다.");
        }

        [Test]
        public void 뽑은_아이템은_반드시_인벤토리에_넣어_본다()
        {
            // 넣어 보지 않고 '가득 찼나' 만 묻는 구조면, 묻는 사이에 다른 지급이 끼어 둘 다 성공할 수 있다.
            ChestLootTable table = PlaceholderChestLootTable.Create();
            var random = new DeterministicRandom(99);
            HashSet<int> known = PlaceholderChestLootTable.Entries.Select(e => e.ItemId).ToHashSet();

            for (int i = 0; i < 1_000; i++)
            {
                var receiver = new FakeReceiver(accepts: i % 2 == 0);
                ChestRewardOutcome outcome = ChestRewardResolver.Resolve(
                    table, PlaceholderChestLootTable.RewardGold, isFinalRound: i % 3 == 0, random, receiver);

                Assert.That(known.Contains(outcome.ItemId), Is.True, "표에 없는 아이템이 나왔다.");
                Assert.That(receiver.Offered[0], Is.EqualTo(outcome.ItemId), "처음 뽑은 것부터 넣어 본다.");
                if (i % 2 == 0)
                {
                    Assert.That(receiver.Offered, Is.EqualTo(new[] { outcome.ItemId }));
                    Assert.That(outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Inventory));
                    continue;
                }

                // 전부 거절하면 같은 등급의 모든 아이템을 한 번씩 넣어 보고 과잉 보상이 된다 (S-GD-ZPV7Q4).
                Assert.That(table.TryGetRarity(outcome.ItemId, out ChestItemRarity rarity), Is.True);
                Assert.That(receiver.Offered.Count, Is.EqualTo(table.CountOf(rarity)));
                Assert.That(receiver.Offered.Distinct().Count(), Is.EqualTo(receiver.Offered.Count), "같은 것을 두 번 넣지 않는다.");
                Assert.That(receiver.Offered.All(id => table.TryGetRarity(id, out ChestItemRarity r) && r == rarity), Is.True,
                    "다른 등급으로 넘어가면 안 된다.");
                Assert.That(outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Overflow));
            }
        }

        [Test]
        public void 칸이_셋_다_쓰여도_아이템을_뽑고_가득_찬_인벤토리_규칙에_위임한다()
        {
            var receiver = new FakeReceiver(accepts: true, isFull: true)
            {
                FullInventoryDelivery = ChestRewardDelivery.ImmediateUse,
            };
            ChestLootTable table = PlaceholderChestLootTable.Create();

            ChestRewardOutcome outcome = ChestRewardResolver.Resolve(table,
                PlaceholderChestLootTable.RewardGold, isFinalRound: false, new DeterministicRandom(5), receiver);

            Assert.That(outcome.Delivery, Is.EqualTo(ChestRewardDelivery.ImmediateUse));
            Assert.That(outcome.Gold, Is.EqualTo(300));
            Assert.That(table.TryGetRarity(outcome.ItemId, out _), Is.True, "가득 차도 실제 추첨표에서 하나를 뽑는다.");
            Assert.That(receiver.Offered, Is.EqualTo(new[] { outcome.ItemId }),
                "뽑힌 아이템은 가득 찬 인벤토리의 즉시 사용·차단 판정에 한 번만 넘긴다.");
        }

        [Test]
        public void 보유_한도에_걸리면_같은_등급_안에서만_다시_뽑는다()
        {
            ChestLootTable table = PlaceholderChestLootTable.Create();
            int swiftness = PlaceholderShopCatalog.SwiftnessPotionItemId;
            int health = PlaceholderShopCatalog.HealthPotionItemId;
            int retried = 0;

            for (int seed = 1; seed <= 300; seed++)
            {
                var receiver = new FakeReceiver(accepts: true, isFull: false, swiftness);
                ChestRewardOutcome outcome = ChestRewardResolver.Resolve(
                    table, PlaceholderChestLootTable.RewardGold, isFinalRound: false, new DeterministicRandom(seed), receiver);

                Assert.That(outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Inventory), "빈 칸이 있으면 버프로 새지 않는다.");
                if (receiver.Offered[0] != swiftness)
                {
                    Assert.That(receiver.Offered.Count, Is.EqualTo(1));
                    continue;
                }

                retried++;
                Assert.That(receiver.Offered, Is.EqualTo(new[] { swiftness, health }),
                    "신속이 한도면 같은 일반 등급의 체력 물약을 다시 뽑는다 — 고급·희귀로 넘어가지 않는다.");
                Assert.That(outcome.ItemId, Is.EqualTo(health));
            }

            Assert.That(retried, Is.GreaterThan(0), "다시 뽑기 경로를 한 번도 지나지 않았다.");
        }

        [Test]
        public void 체력_3_신속_2면_일반_등급이_나올_때만_과잉_보상이다()
        {
            // 사용자 예시(2026-09-18) — 일반 등급 둘이 다 한도라 같은 등급에서 줄 것이 없다. 고급·희귀는 빈 칸에 받는다.
            ChestLootTable table = PlaceholderChestLootTable.Create();
            int swiftness = PlaceholderShopCatalog.SwiftnessPotionItemId;
            int health = PlaceholderShopCatalog.HealthPotionItemId;
            int commonOverflows = 0;

            for (int seed = 1; seed <= 300; seed++)
            {
                var receiver = new FakeReceiver(accepts: true, isFull: false, swiftness, health);
                ChestRewardOutcome outcome = ChestRewardResolver.Resolve(
                    table, PlaceholderChestLootTable.RewardGold, isFinalRound: false, new DeterministicRandom(seed), receiver);

                Assert.That(table.TryGetRarity(receiver.Offered[0], out ChestItemRarity first), Is.True);
                if (first != ChestItemRarity.Common)
                {
                    Assert.That(outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Inventory));
                    continue;
                }

                commonOverflows++;
                Assert.That(outcome.Delivery, Is.EqualTo(ChestRewardDelivery.Overflow));
                Assert.That(receiver.Offered, Is.EquivalentTo(new[] { swiftness, health }), "일반 등급만 넣어 본다.");
            }

            Assert.That(commonOverflows, Is.GreaterThan(0));
        }

        [Test]
        public void 잘못된_인자는_거절한다()
        {
            Assert.Throws<ArgumentNullException>(() => ChestRewardResolver.Resolve(
                null, 200, false, new DeterministicRandom(1), new FakeReceiver(true)));
            Assert.Throws<ArgumentNullException>(() => ChestRewardResolver.Resolve(
                PlaceholderChestLootTable.Create(), 200, false, new DeterministicRandom(1), null));
            Assert.Throws<ArgumentOutOfRangeException>(() => ChestRewardResolver.Resolve(
                PlaceholderChestLootTable.Create(), -1, false, new DeterministicRandom(1), new FakeReceiver(true)));
        }
    }
}
