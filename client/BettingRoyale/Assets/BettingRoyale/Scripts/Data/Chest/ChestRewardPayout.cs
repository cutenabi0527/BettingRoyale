using System;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Throwing;

namespace BettingRoyale.Data.Chest
{
    /// <summary>상자 보상 한 건을 실제로 넣은 결과.</summary>
    public readonly struct ChestPayoutResult
    {
        public readonly int PlayerId;
        public readonly int ChestId;

        /// <summary>
        /// 보상을 줬는가. false 면 <b>아무것도 주지 않았다</b> — 라운드가 끝났거나(S-GD-DFG3VR) 받을 몸이 없다.
        /// 이때 <see cref="Outcome"/> 은 비어 있고 추첨도 하지 않았다.
        /// </summary>
        public readonly bool Paid;

        /// <summary>무엇을 어떻게 줬는가 — 골드·뽑힌 아이템·전달 방식.</summary>
        public readonly ChestRewardOutcome Outcome;

        public readonly bool GoldGranted;

        /// <summary>과잉 보상 효과를 걸었는가. 아이템이 인벤토리로 들어갔으면 언제나 false.</summary>
        public readonly bool OverflowApplied;

        /// <summary>인벤토리가 아이템을 거절한 사유. 들어갔으면 <c>None</c>.</summary>
        public readonly EquipmentInventoryFailure ItemFailure;

        internal ChestPayoutResult(
            int playerId,
            int chestId,
            bool paid,
            ChestRewardOutcome outcome,
            bool goldGranted,
            bool overflowApplied,
            EquipmentInventoryFailure itemFailure)
        {
            PlayerId = playerId;
            ChestId = chestId;
            Paid = paid;
            Outcome = outcome;
            GoldGranted = goldGranted;
            OverflowApplied = overflowApplied;
            ItemFailure = itemFailure;
        }

        /// <summary>
        /// 과잉 보상이 <b>칸 부족·보유 한도가 아닌 이유</b>로 났다 — 카탈로그 누락 같은 설정 오류다.
        /// 플레이어에게는 과잉 보상을 그대로 주고, 부르는 쪽이 경고를 남긴다.
        /// </summary>
        public bool IsUnexpectedOverflow =>
            Paid
            && Outcome.Delivery == ChestRewardDelivery.Overflow
            && ItemFailure != EquipmentInventoryFailure.ConsumableCapacityFull
            && ItemFailure != EquipmentInventoryFailure.CarryLimitExceeded;

        public override string ToString() => Paid
            ? $"player {PlayerId} chest {ChestId}: {Outcome}"
                + $" gold={(GoldGranted ? "ok" : "rejected")}"
                + (Outcome.Delivery == ChestRewardDelivery.Overflow
                    ? $" overflow={(OverflowApplied ? "ok" : "rejected")} ({ItemFailure})"
                    : string.Empty)
            : $"player {PlayerId} chest {ChestId}: not paid";
    }

    /// <summary>
    /// 상자 수령 <b>한 건</b>을 실제로 지급한다 — 300G + 아이템 1개와 현행 전달 규칙
    /// (S-GD-DDXPX9 §1·§3·§4 · S-GD-JQ4E2J · S-GD-RC63HQ).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>한 라운드에 하나를 만든다.</b> 등급 확률은 라운드에 따라 다르므로(마지막 라운드) 라운드를 아는
    /// 조립 지점이 표·골드·마지막 라운드 여부를 넘긴다. 기본값은 두지 않는다(S-GD-GQNA2B) —
    /// 지금 값은 <see cref="PlaceholderChestLootTable"/> 에 있다.
    /// </para>
    /// <para>
    /// <b>순서는 아이템 → 골드 → 과잉 보상이다.</b> 아이템은 넣어 봐야 받는지 알므로 먼저 넣고,
    /// 거절됐을 때만 <see cref="ShopItemEffectCatalog.ChestOverflowEffects"/> 를 건다. 골드는 어느 쪽이든 받는다.
    /// </para>
    /// <para>
    /// <b>Host(권위)에서만 쓴다.</b> 다른 피어는 결과를 복제로 받는다 — 골드는 경제 스냅샷, 아이템은 인벤토리
    /// 복제, 회복·이동 속도는 권위 시뮬레이션 결과로.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public sealed class ChestRewardPayout
    {
        private readonly ChestLootTable table;
        private readonly int rewardGold;
        private readonly bool isFinalRound;
        private readonly DeterministicRandom random;
        private readonly IChestRewardRecipients recipients;

        public ChestRewardPayout(
            ChestLootTable table,
            int rewardGold,
            bool isFinalRound,
            DeterministicRandom random,
            IChestRewardRecipients recipients)
        {
            if (rewardGold < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rewardGold), rewardGold, "보상 골드는 음수일 수 없습니다.");
            }

            this.table = table ?? throw new ArgumentNullException(nameof(table));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.recipients = recipients ?? throw new ArgumentNullException(nameof(recipients));
            this.rewardGold = rewardGold;
            this.isFinalRound = isFinalRound;
        }

        public bool IsFinalRound => isFinalRound;

        /// <summary>이 사람에게 이 상자의 보상을 준다. 상자 쪽 단일 수령 판정은 이미 끝났다고 본다.</summary>
        public ChestPayoutResult Pay(int playerId, int chestId)
        {
            if (!recipients.TryResolveRecipient(playerId, out IEquipmentInventoryDriver inventory) || inventory == null)
            {
                return new ChestPayoutResult(playerId, chestId, paid: false, default, false, false,
                    EquipmentInventoryFailure.None);
            }

            var receiver = new InventoryReceiver(inventory);
            ChestRewardOutcome outcome = ChestRewardResolver.Resolve(table, rewardGold, isFinalRound, random, receiver);

            bool goldGranted = outcome.Gold > 0 && recipients.TryGrantGold(playerId, outcome.Gold);

            bool overflowApplied = outcome.Delivery == ChestRewardDelivery.Overflow
                && inventory is IConsumableEffectReceiver effects
                && effects.TryAuthorityApplyConsumableEffects(ShopItemEffectCatalog.ChestOverflowEffects,
                    ShopItemEffectCatalog.ChestOverflowSourceId);

            return new ChestPayoutResult(playerId, chestId, paid: true, outcome, goldGranted, overflowApplied,
                receiver.Failure);
        }

        /// <summary>기존 지급 경로(<c>TryAuthorityPickup</c>)로 한 개를 넣어 본다. 거절 사유를 남긴다.</summary>
        private sealed class InventoryReceiver : IChestItemReceiver
        {
            private readonly IEquipmentInventoryDriver inventory;

            public InventoryReceiver(IEquipmentInventoryDriver inventory) => this.inventory = inventory;

            public EquipmentInventoryFailure Failure { get; private set; }

            /// <summary>
            /// 소모품 칸 3개가 모두 쓰이고 있는가 (S-GD-JQ4E2J). 실제 아이템을 뽑아 즉시 사용 또는 차단하되
            /// 지급 실패 사유는 <c>ConsumableCapacityFull</c> 로 남긴다.
            /// </summary>
            public bool IsFull
            {
                get
                {
                    EquipmentInventorySnapshot snapshot = inventory.EquipmentSnapshot;
                    for (int i = 0; i < EquipmentInventorySnapshot.ConsumableSlotCount; i++)
                    {
                        if (snapshot.GetConsumableSlot(i).IsEmpty)
                        {
                            return false;
                        }
                    }

                    Failure = EquipmentInventoryFailure.ConsumableCapacityFull;
                    return true;
                }
            }

            public bool TryReceiveItem(int itemId)
            {
                EquipmentInventoryOperationResult result = inventory.TryAuthorityPickup(itemId, quantity: 1);
                Failure = result.Succeeded ? EquipmentInventoryFailure.None : result.Failure;
                return result.Succeeded;
            }

            public ChestRewardDelivery ResolveFullInventoryItem(int itemId)
            {
                Failure = EquipmentInventoryFailure.ConsumableCapacityFull;

                if (inventory is IEquipmentItemCatalogSource source
                    && source.ItemCatalog is IThrowingItemCatalog throwing
                    && throwing.TryGetThrowing(itemId, out _))
                {
                    return ChestRewardDelivery.Blocked;
                }

                if (inventory is IConsumableEffectReceiver effects
                    && ShopItemEffectCatalog.TryGetConsumableEffects(itemId, out var definitions))
                {
                    // 체력이 이미 가득 차 효과량이 0이어도 아이템은 즉시 사용된 것으로 처리한다.
                    effects.TryAuthorityApplyConsumableEffects(definitions, itemId);
                    return ChestRewardDelivery.ImmediateUse;
                }

                return ChestRewardDelivery.Overflow;
            }
        }
    }
}
