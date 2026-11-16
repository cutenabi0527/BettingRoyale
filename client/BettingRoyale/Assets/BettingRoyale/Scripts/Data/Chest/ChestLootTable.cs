using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Chest
{
    /// <summary>상자 아이템 등급 (S-GD-DDXPX9 §2).</summary>
    public enum ChestItemRarity
    {
        /// <summary>일반 — 상점과 같은 물건.</summary>
        Common = 0,

        /// <summary>고급 — 상자 전용.</summary>
        Rare = 1,

        /// <summary>희귀 — 상자 전용.</summary>
        Epic = 2,
    }

    /// <summary>추첨표의 한 줄 — 어떤 아이템이 어느 등급인가.</summary>
    public readonly struct ChestLootEntry
    {
        public readonly int ItemId;
        public readonly ChestItemRarity Rarity;

        public ChestLootEntry(int itemId, ChestItemRarity rarity)
        {
            if (itemId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "아이템 번호는 1 이상이어야 합니다.");
            }

            ItemId = itemId;
            Rarity = rarity;
        }
    }

    /// <summary>
    /// 등급 가중치. <b>정수</b>로 받는다 — 58·30·12 처럼. 부동소수 합이 1.0 에서 조금 어긋나
    /// 마지막 등급이 영영 안 뽑히는 사고를 막는다.
    /// </summary>
    public readonly struct ChestRarityWeights
    {
        public readonly int Common;
        public readonly int Rare;
        public readonly int Epic;

        public ChestRarityWeights(int common, int rare, int epic)
        {
            if (common < 0 || rare < 0 || epic < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(common), "가중치는 음수일 수 없습니다.");
            }

            if (common + rare + epic <= 0)
            {
                throw new ArgumentException("가중치 합이 0 이면 아무것도 뽑을 수 없습니다.");
            }

            Common = common;
            Rare = rare;
            Epic = epic;
        }

        public int Total => Common + Rare + Epic;

        public int Of(ChestItemRarity rarity) => rarity switch
        {
            ChestItemRarity.Common => Common,
            ChestItemRarity.Rare => Rare,
            ChestItemRarity.Epic => Epic,
            _ => 0,
        };
    }

    /// <summary>
    /// 상자에서 나올 아이템 <b>하나</b>를 뽑는다 (S-GD-DDXPX9 §3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>두 단계로 뽑는다.</b> ① 등급을 가중치로 ② 그 등급 안에서 <b>균등</b>하게. 아이템 수가 많은 등급이라고
    /// 더 자주 나오지 않는다 — 등급 확률이 곧 기획 확률이다.
    /// </para>
    /// <para>
    /// <b>목록은 라운드와 무관하고 가중치만 다르다.</b> 마지막 라운드에만 빠지던 골드 주머니·재추첨권은
    /// 폐기됐다(S-GD-DDXPX9 §7) — 지금은 모든 라운드가 같은 아이템을 쓰고 등급 확률만 바뀐다.
    /// </para>
    /// <para>
    /// ⚠️ <b>기본값을 두지 않는다</b>(S-GD-GQNA2B). 표와 가중치는 조립하는 쪽이 넘긴다.
    /// 지금 값은 <see cref="PlaceholderChestLootTable"/> 에 있다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public sealed class ChestLootTable
    {
        private static readonly ChestItemRarity[] Rarities =
            { ChestItemRarity.Common, ChestItemRarity.Rare, ChestItemRarity.Epic };

        private readonly ChestLootEntry[] entries;
        private readonly ChestRarityWeights regularWeights;
        private readonly ChestRarityWeights finalRoundWeights;

        public ChestLootTable(
            IReadOnlyList<ChestLootEntry> entries,
            ChestRarityWeights regularWeights,
            ChestRarityWeights finalRoundWeights)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (entries.Count == 0)
            {
                throw new ArgumentException("추첨표가 비어 있습니다.", nameof(entries));
            }

            this.entries = new ChestLootEntry[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                this.entries[i] = entries[i];
            }

            this.regularWeights = regularWeights;
            this.finalRoundWeights = finalRoundWeights;

            // 가중치가 있는데 줄이 없는 등급은 설정 실수다. 그대로 두면 그 등급이 뽑힌 순간 줄 것이 없다.
            EnsureEveryWeightedRarityHasItems(regularWeights, nameof(regularWeights));
            EnsureEveryWeightedRarityHasItems(finalRoundWeights, nameof(finalRoundWeights));
        }

        public IReadOnlyList<ChestLootEntry> Entries => entries;

        /// <summary>등급 가중치. 마지막 라운드면 그쪽 표를 준다.</summary>
        public ChestRarityWeights WeightsFor(bool isFinalRound) => isFinalRound ? finalRoundWeights : regularWeights;

        /// <summary>이번 라운드의 등급 확률(0~1).</summary>
        public double RarityChance(ChestItemRarity rarity, bool isFinalRound)
        {
            ChestRarityWeights weights = WeightsFor(isFinalRound);
            return (double)weights.Of(rarity) / weights.Total;
        }

        /// <summary>그 등급의 아이템 수.</summary>
        public int CountOf(ChestItemRarity rarity)
        {
            int count = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Rarity == rarity)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>아이템 하나가 뽑힐 확률(0~1). 등급 확률을 그 등급의 아이템 수로 나눈 값이다.</summary>
        public double ItemChance(int itemId, bool isFinalRound)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].ItemId == itemId)
                {
                    return RarityChance(entries[i].Rarity, isFinalRound) / CountOf(entries[i].Rarity);
                }
            }

            return 0d;
        }

        /// <summary>아이템 하나를 뽑아 번호를 준다.</summary>
        public int Draw(bool isFinalRound, DeterministicRandom random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            ChestItemRarity rarity = DrawRarity(isFinalRound, random);
            int pick = random.Next(CountOf(rarity));

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Rarity != rarity)
                {
                    continue;
                }

                if (pick == 0)
                {
                    return entries[i].ItemId;
                }

                pick--;
            }

            // 생성자 검사가 막았으므로 오지 않는다.
            throw new InvalidOperationException("추첨표에서 뽑을 줄을 찾지 못했습니다.");
        }

        /// <summary>표에 있는 아이템의 등급. 없으면 false.</summary>
        public bool TryGetRarity(int itemId, out ChestItemRarity rarity)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].ItemId == itemId)
                {
                    rarity = entries[i].Rarity;
                    return true;
                }
            }

            rarity = default;
            return false;
        }

        /// <summary>
        /// 그 등급 안에서 <paramref name="allowed"/> 가 허락한 아이템 하나를 <b>균등하게</b> 다시 뽑는다.
        /// 들 수 있는 것이 하나도 없으면 false (S-GD-ZPV7Q4 — 같은 등급 안에서 다시 뽑기).
        /// </summary>
        public bool TryDrawWithin(
            ChestItemRarity rarity, DeterministicRandom random, Func<int, bool> allowed, out int itemId)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (allowed == null)
            {
                throw new ArgumentNullException(nameof(allowed));
            }

            int candidates = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Rarity == rarity && allowed(entries[i].ItemId))
                {
                    candidates++;
                }
            }

            if (candidates == 0)
            {
                itemId = 0;
                return false;
            }

            int pick = random.Next(candidates);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Rarity != rarity || !allowed(entries[i].ItemId))
                {
                    continue;
                }

                if (pick == 0)
                {
                    itemId = entries[i].ItemId;
                    return true;
                }

                pick--;
            }

            // 위에서 센 수만큼만 돈다. 오지 않는다.
            throw new InvalidOperationException("다시 뽑을 줄을 찾지 못했습니다.");
        }

        private ChestItemRarity DrawRarity(bool isFinalRound, DeterministicRandom random)
        {
            ChestRarityWeights weights = WeightsFor(isFinalRound);
            int roll = random.Next(weights.Total);

            foreach (ChestItemRarity rarity in Rarities)
            {
                int weight = weights.Of(rarity);
                if (roll < weight)
                {
                    return rarity;
                }

                roll -= weight;
            }

            throw new InvalidOperationException("등급을 뽑지 못했습니다.");
        }

        private void EnsureEveryWeightedRarityHasItems(ChestRarityWeights weights, string parameterName)
        {
            foreach (ChestItemRarity rarity in Rarities)
            {
                if (weights.Of(rarity) > 0 && CountOf(rarity) == 0)
                {
                    throw new ArgumentException(
                        $"{rarity} 등급에 가중치 {weights.Of(rarity)} 가 있는데 아이템이 하나도 없습니다.", parameterName);
                }
            }
        }
    }
}
