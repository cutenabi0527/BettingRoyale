using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Chest
{
    /// <summary>뽑힌 아이템이 어떻게 전달되는가 (S-GD-DDXPX9 §4 · S-GD-JQ4E2J).</summary>
    public enum ChestRewardDelivery
    {
        /// <summary>인벤토리에 들어갔다.</summary>
        Inventory = 0,

        /// <summary>인벤토리가 받지 못해 아이템 대신 과잉 보상 버프를 건다.</summary>
        Overflow = 1,

        /// <summary>가득 찬 인벤토리 대신 비투척 소모품 효과를 즉시 적용했다.</summary>
        ImmediateUse = 2,

        /// <summary>가득 찬 인벤토리에서 투척류 지급을 차단했다.</summary>
        Blocked = 3,
    }

    /// <summary>상자 하나를 수령한 결과. 골드와 아이템 하나.</summary>
    public readonly struct ChestRewardOutcome
    {
        /// <summary>항상 주는 골드. 과잉 보상이어도 그대로 받는다.</summary>
        public readonly int Gold;

        /// <summary>
        /// 뽑힌 아이템. 과잉 보상이면 <b>받지 못한</b> 아이템 번호다.
        /// </summary>
        public readonly int ItemId;

        public readonly ChestRewardDelivery Delivery;

        public ChestRewardOutcome(int gold, int itemId, ChestRewardDelivery delivery)
        {
            Gold = gold;
            ItemId = itemId;
            Delivery = delivery;
        }

        public override string ToString() => $"{Gold}G + {ItemId} ({Delivery})";
    }

    /// <summary>
    /// 상자를 수령한 사람의 인벤토리가 아이템을 받는가. <b>받으면 이미 넣은 것이다</b> —
    /// 묻기와 넣기를 나누면 그 사이에 다른 지급이 끼어 둘 다 성공할 수 있다.
    /// </summary>
    public interface IChestItemReceiver
    {
        /// <summary>아이템 칸 3개가 <b>모두 쓰이고 있는가</b> (S-GD-JQ4E2J).</summary>
        bool IsFull { get; }

        /// <returns>넣었으면 true. 칸 부족·보유 한도로 거절되면 false(상태는 그대로).</returns>
        bool TryReceiveItem(int itemId);

        /// <summary>가득 찬 상태에서 뽑힌 아이템을 즉시 사용하거나 차단한다.</summary>
        ChestRewardDelivery ResolveFullInventoryItem(int itemId);
    }

    /// <summary>
    /// 상자 보상 <b>하나</b>를 정한다 — 골드는 고정, 아이템은 뽑고, 현행 전달 규칙에 따라 지급한다
    /// (S-GD-DDXPX9 §1·§4 · S-GD-JQ4E2J).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 아이템 칸 3개가 모두 쓰이면 실제 아이템을 뽑아 비투척 소모품은 즉시 사용하고 투척류는 차단한다
    /// (S-GD-JQ4E2J). 빈 칸이 있을 때 뽑힌 아이템이 보유 한도에 걸렸고, <b>같은 등급</b>에 들 수 있는
    /// 다른 아이템도 없으면 과잉 보상을 지급한다.
    /// 같은 등급에 들 수 있는 것이 있으면 그 안에서 다시 뽑아 준다 — 빈 칸이 있는데 버프로 새지 않는다.
    /// </para>
    /// <para>
    /// <b>골드·버프를 직접 주지 않는다.</b> 이 계층은 경제·체력 컴포넌트를 모른다(D-060). 결과를 받은
    /// 실행 경로(지금은 제품 Host 뿐 — Local Play 는 경제가 없어 지급하지 않는다)가 골드를 넣고 과잉 보상 효과를 건다.
    /// 인벤토리만은 여기서 넣는다 — 넣어 봐야 받는지 안다.
    /// </para>
    /// </remarks>
    public static class ChestRewardResolver
    {
        public static ChestRewardOutcome Resolve(
            ChestLootTable table,
            int rewardGold,
            bool isFinalRound,
            DeterministicRandom random,
            IChestItemReceiver receiver)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (receiver == null)
            {
                throw new ArgumentNullException(nameof(receiver));
            }

            if (rewardGold < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rewardGold), rewardGold, "보상 골드는 음수일 수 없습니다.");
            }

            // 칸이 다 차도 실제 아이템을 뽑는다. 비투척 소모품은 즉시 쓰고 투척류는 차단한다.
            if (receiver.IsFull)
            {
                int fullItemId = table.Draw(isFinalRound, random);
                return new ChestRewardOutcome(
                    rewardGold,
                    fullItemId,
                    receiver.ResolveFullInventoryItem(fullItemId));
            }

            int itemId = table.Draw(isFinalRound, random);
            if (receiver.TryReceiveItem(itemId))
            {
                return new ChestRewardOutcome(rewardGold, itemId, ChestRewardDelivery.Inventory);
            }

            // ② 빈 칸은 있는데 이 아이템을 더 들 수 없다(보유 한도) — 같은 등급 안에서 들 수 있는 것을 다시 뽑는다.
            if (table.TryGetRarity(itemId, out ChestItemRarity rarity))
            {
                var refused = new List<int> { itemId };
                while (table.TryDrawWithin(rarity, random, id => !refused.Contains(id), out int retry))
                {
                    if (receiver.TryReceiveItem(retry))
                    {
                        return new ChestRewardOutcome(rewardGold, retry, ChestRewardDelivery.Inventory);
                    }

                    refused.Add(retry);
                }
            }

            return new ChestRewardOutcome(rewardGold, itemId, ChestRewardDelivery.Overflow);
        }
    }
}
