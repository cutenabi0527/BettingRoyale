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
    /// 상자 아이템 추첨표가 기획 확률 그대로 뽑는지 못 박는다 (S-GD-DDXPX9 §2·§3).
    /// </summary>
    public class ChestLootTableTests
    {
        private const int Draws = 40_000;

        // 표본 4만 번이면 표준오차가 0.25%p 안쪽이다. 1.5%p 를 넘게 벗어나면 확률 계산이 틀린 것이다.
        private const double Tolerance = 0.015;

        private static readonly int[] CommonItems =
        {
            PlaceholderShopCatalog.HealthPotionItemId, PlaceholderShopCatalog.SwiftnessPotionItemId,
        };

        private static readonly int[] RareItems =
        {
            RecommendedChestItemCatalog.SprintPotionItemId, RecommendedChestItemCatalog.FuryPotionItemId,
            RecommendedChestItemCatalog.FrenzyPotionItemId, RecommendedChestItemCatalog.BulwarkPotionItemId,
            RecommendedChestItemCatalog.ShieldCharmItemId, RecommendedChestItemCatalog.RegenerationPotionItemId,
        };

        private static readonly int[] EpicItems =
        {
            RecommendedChestItemCatalog.LargeHealthPotionItemId, RecommendedChestItemCatalog.DashStoneItemId,
            RecommendedChestItemCatalog.InvulnerabilityCharmItemId, RecommendedChestItemCatalog.ProteinShakeItemId,
        };

        private static Dictionary<int, int> DrawMany(bool isFinalRound, int seed = 20260918)
        {
            ChestLootTable table = PlaceholderChestLootTable.Create();
            var random = new DeterministicRandom(seed);
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < Draws; i++)
            {
                int id = table.Draw(isFinalRound, random);
                counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;
            }

            return counts;
        }

        private static double Share(Dictionary<int, int> counts, IEnumerable<int> ids) =>
            (double)ids.Sum(id => counts.TryGetValue(id, out int c) ? c : 0) / Draws;

        [Test]
        public void 추첨표는_12종이고_등급은_일반2_고급6_희귀4다()
        {
            ChestLootEntry[] entries = PlaceholderChestLootTable.Entries;

            Assert.That(entries.Length, Is.EqualTo(12));
            Assert.That(entries.Select(e => e.ItemId), Is.Unique, "같은 아이템이 두 줄이면 확률이 두 배가 된다.");
            Assert.That(entries.Where(e => e.Rarity == ChestItemRarity.Common).Select(e => e.ItemId),
                Is.EquivalentTo(CommonItems), "일반 등급은 상점과 같은 물건이다.");
            Assert.That(entries.Where(e => e.Rarity == ChestItemRarity.Rare).Select(e => e.ItemId),
                Is.EquivalentTo(RareItems));
            Assert.That(entries.Where(e => e.Rarity == ChestItemRarity.Epic).Select(e => e.ItemId),
                Is.EquivalentTo(EpicItems));
        }

        [Test]
        public void 폐기한_골드_주머니와_재추첨권은_표에_없다()
        {
            // S-GD-DDXPX9 §7 — 전투에 도움이 안 돼서 뺐다. 2113·2114 번은 다시 쓰지 않는다.
            int[] ids = PlaceholderChestLootTable.Entries.Select(e => e.ItemId).ToArray();

            Assert.That(ids, Does.Not.Contain(2113));
            Assert.That(ids, Does.Not.Contain(2114));
            Assert.That(ids, Does.Contain(RecommendedChestItemCatalog.ProteinShakeItemId),
                "프로틴 쉐이크는 2115 다 — 폐기 번호를 재사용하지 않았다.");
        }

        [Test]
        public void 라운드가_달라도_아이템_목록은_같다()
        {
            // 마지막 라운드에만 빠지던 아이템이 없어졌다. 달라지는 것은 등급 확률뿐이다.
            Assert.That(DrawMany(isFinalRound: false).Keys, Is.EquivalentTo(DrawMany(isFinalRound: true).Keys));
            Assert.That(DrawMany(isFinalRound: false).Keys,
                Is.EquivalentTo(PlaceholderChestLootTable.Entries.Select(e => e.ItemId)));
        }

        [Test]
        public void 명목_확률은_라운드에_따라_다르다()
        {
            ChestLootTable table = PlaceholderChestLootTable.Create();

            Assert.That(table.RarityChance(ChestItemRarity.Common, isFinalRound: false), Is.EqualTo(0.58).Within(1e-9));
            Assert.That(table.RarityChance(ChestItemRarity.Rare, isFinalRound: false), Is.EqualTo(0.30).Within(1e-9));
            Assert.That(table.RarityChance(ChestItemRarity.Epic, isFinalRound: false), Is.EqualTo(0.12).Within(1e-9));

            Assert.That(table.RarityChance(ChestItemRarity.Common, isFinalRound: true), Is.EqualTo(0.38).Within(1e-9));
            Assert.That(table.RarityChance(ChestItemRarity.Rare, isFinalRound: true), Is.EqualTo(0.42).Within(1e-9));
            Assert.That(table.RarityChance(ChestItemRarity.Epic, isFinalRound: true), Is.EqualTo(0.20).Within(1e-9));
        }

        [Test]
        public void 아이템_하나의_확률은_등급_확률을_그_등급_수로_나눈_값이다()
        {
            ChestLootTable table = PlaceholderChestLootTable.Create();

            // 일반 29% · 고급 5% · 희귀 3% (마지막이 아닌 라운드)
            Assert.That(table.ItemChance(PlaceholderShopCatalog.HealthPotionItemId, false), Is.EqualTo(0.29).Within(1e-9));
            Assert.That(table.ItemChance(RecommendedChestItemCatalog.FuryPotionItemId, false), Is.EqualTo(0.05).Within(1e-9));
            Assert.That(table.ItemChance(RecommendedChestItemCatalog.DashStoneItemId, false), Is.EqualTo(0.03).Within(1e-9));

            // 일반 19% · 고급 7% · 희귀 5% (마지막 라운드)
            Assert.That(table.ItemChance(PlaceholderShopCatalog.HealthPotionItemId, true), Is.EqualTo(0.19).Within(1e-9));
            Assert.That(table.ItemChance(RecommendedChestItemCatalog.FuryPotionItemId, true), Is.EqualTo(0.07).Within(1e-9));
            Assert.That(table.ItemChance(RecommendedChestItemCatalog.DashStoneItemId, true), Is.EqualTo(0.05).Within(1e-9));

            Assert.That(table.ItemChance(999_999, false), Is.EqualTo(0d), "표에 없는 아이템은 안 나온다.");
        }

        [Test]
        public void 실제로_뽑으면_등급_비율이_명목_확률에_맞는다_마지막이_아닌_라운드()
        {
            Dictionary<int, int> counts = DrawMany(isFinalRound: false);

            Assert.That(Share(counts, CommonItems), Is.EqualTo(0.58).Within(Tolerance));
            Assert.That(Share(counts, RareItems), Is.EqualTo(0.30).Within(Tolerance));
            Assert.That(Share(counts, EpicItems), Is.EqualTo(0.12).Within(Tolerance));
        }

        [Test]
        public void 실제로_뽑으면_등급_비율이_명목_확률에_맞는다_마지막_라운드()
        {
            Dictionary<int, int> counts = DrawMany(isFinalRound: true);

            Assert.That(Share(counts, CommonItems), Is.EqualTo(0.38).Within(Tolerance));
            Assert.That(Share(counts, RareItems), Is.EqualTo(0.42).Within(Tolerance));
            Assert.That(Share(counts, EpicItems), Is.EqualTo(0.20).Within(Tolerance));
        }

        [Test]
        public void 등급_안에서는_균등하다()
        {
            Dictionary<int, int> counts = DrawMany(isFinalRound: false);

            foreach ((int[] group, double each) in new[]
                     {
                         (CommonItems, 1d / 2d), (RareItems, 1d / 6d), (EpicItems, 1d / 4d),
                     })
            {
                int total = group.Sum(id => counts[id]);
                foreach (int id in group)
                {
                    Assert.That((double)counts[id] / total, Is.EqualTo(each).Within(0.03),
                        $"{id} 가 등급 안에서 균등하지 않다.");
                }
            }
        }

        [Test]
        public void 같은_시드면_같은_순서로_뽑는다()
        {
            ChestLootTable table = PlaceholderChestLootTable.Create();
            var a = new DeterministicRandom(42);
            var b = new DeterministicRandom(42);

            for (int i = 0; i < 200; i++)
            {
                Assert.That(table.Draw(false, a), Is.EqualTo(table.Draw(false, b)));
            }
        }

        [Test]
        public void 가중치가_있는데_아이템이_없는_등급은_만들_수_없다()
        {
            // 그대로 두면 그 등급이 뽑힌 순간 줄 것이 없다.
            Assert.Throws<ArgumentException>(() => new ChestLootTable(
                new[] { new ChestLootEntry(1, ChestItemRarity.Common) },
                new ChestRarityWeights(1, 1, 0), new ChestRarityWeights(1, 0, 0)));

            Assert.Throws<ArgumentException>(() => new ChestLootTable(
                new[] { new ChestLootEntry(1, ChestItemRarity.Common) },
                new ChestRarityWeights(1, 0, 0), new ChestRarityWeights(0, 0, 1)));
        }

        [Test]
        public void 빈_표나_잘못된_가중치는_만들_수_없다()
        {
            Assert.Throws<ArgumentException>(() => new ChestLootTable(
                Array.Empty<ChestLootEntry>(), new ChestRarityWeights(1, 1, 1), new ChestRarityWeights(1, 1, 1)));
            Assert.Throws<ArgumentNullException>(() => new ChestLootTable(
                null, new ChestRarityWeights(1, 0, 0), new ChestRarityWeights(1, 0, 0)));
            Assert.Throws<ArgumentException>(() => new ChestRarityWeights(0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChestRarityWeights(-1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChestLootEntry(0, ChestItemRarity.Common));
        }

        [Test]
        public void 가중치가_0인_등급은_한_번도_안_뽑힌다()
        {
            var table = new ChestLootTable(
                new[] { new ChestLootEntry(1, ChestItemRarity.Common), new ChestLootEntry(2, ChestItemRarity.Epic) },
                new ChestRarityWeights(1, 0, 0), new ChestRarityWeights(0, 0, 1));
            var random = new DeterministicRandom(5);

            for (int i = 0; i < 500; i++)
            {
                Assert.That(table.Draw(isFinalRound: false, random), Is.EqualTo(1));
                Assert.That(table.Draw(isFinalRound: true, random), Is.EqualTo(2));
            }
        }
    }
}
