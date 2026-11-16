using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Throwing
{
    /// <summary>
    /// 플레이어 1명의 투척 사용 승인 규칙. 라운드·단계·시각은 모두 밖에서 공급받는다.
    /// </summary>
    /// <remarks>
    /// 규칙 요약:
    /// <list type="bullet">
    /// <item><see cref="BeginRound"/> 없이는 어떤 요청도 통과하지 않는다.</item>
    /// <item>라운드가 시작되면 쿨다운과 요청 일련번호가 초기화된다.</item>
    /// <item>공유 쿨다운은 슬롯·항목 ID와 무관하게 하나뿐이라 슬롯을 바꿔도 우회되지 않는다.</item>
    /// <item>전투 단계에서만 수량 1을 소모한다. 준비 단계는 소모 없이 승인만 한다.</item>
    /// <item>소모 이후에 실패할 수 있는 콜백은 없다. 결과는 불변 값으로만 반환한다.</item>
    /// </list>
    /// 조준 방향·사거리·선택 UI·입력 잠금 같은 제약은 호출자의 책임이다.
    /// </remarks>
    public sealed class ThrowUseGate
    {
        private readonly int ownerId;
        private readonly EquipmentInventory inventory;
        private readonly IEquipmentItemCatalog itemCatalog;
        private readonly IThrowingItemCatalog throwingCatalog;

        private bool roundStarted;
        private int currentRoundNumber;
        private int lastRequestSequence;
        private double lastObservedTimeSeconds;
        private double cooldownReadyAtSeconds;

        public ThrowUseGate(
            int ownerId,
            EquipmentInventory inventory,
            IEquipmentItemCatalog itemCatalog,
            IThrowingItemCatalog throwingCatalog)
        {
            this.ownerId = ownerId;
            this.inventory = inventory ?? throw new System.ArgumentNullException(nameof(inventory));
            this.itemCatalog = itemCatalog ?? throw new System.ArgumentNullException(nameof(itemCatalog));
            this.throwingCatalog = throwingCatalog ?? throw new System.ArgumentNullException(nameof(throwingCatalog));
        }

        public int OwnerId => ownerId;
        public bool IsRoundStarted => roundStarted;
        public int CurrentRoundNumber => currentRoundNumber;
        public int LastRequestSequence => lastRequestSequence;

        /// <summary>공유 쿨다운이 풀리는 절대 시각(초). 라운드 시작 시 0 으로 초기화된다.</summary>
        public double CooldownReadyAtSeconds => cooldownReadyAtSeconds;

        /// <summary>경계 시각은 사용 가능으로 본다(now == ready 이면 쿨다운 아님).</summary>
        public bool IsCooldownActive(double nowSeconds) =>
            ThrowingItemDefinition.IsFiniteTime(nowSeconds) && nowSeconds < cooldownReadyAtSeconds;

        /// <summary>
        /// 새 라운드를 시작하고 쿨다운·요청 일련번호를 초기화한다.
        /// 라운드 번호는 앞으로만 진행하고 시각은 역행할 수 없다.
        /// </summary>
        public bool BeginRound(int roundNumber, double startTimeSeconds)
        {
            if (roundNumber <= 0 || !ThrowingItemDefinition.IsFiniteTime(startTimeSeconds))
            {
                return false;
            }

            if (roundStarted && (roundNumber <= currentRoundNumber || startTimeSeconds < lastObservedTimeSeconds))
            {
                return false;
            }

            roundStarted = true;
            currentRoundNumber = roundNumber;
            lastRequestSequence = 0;
            lastObservedTimeSeconds = startTimeSeconds;
            cooldownReadyAtSeconds = startTimeSeconds;
            return true;
        }

        /// <summary>
        /// 투척 사용을 승인한다. 거부되는 요청은 인벤토리 수량과 쿨다운을 전혀 바꾸지 않는다.
        /// </summary>
        public ThrowUseResult TryUse(in ThrowUseRequest request, in ThrowRoundContext context)
        {
            if (!roundStarted)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.RoundNotStarted);
            }

            if (!ThrowingItemDefinition.IsFiniteTime(context.NowSeconds) || context.NowSeconds < lastObservedTimeSeconds)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.InvalidTime);
            }

            if (context.RoundNumber != currentRoundNumber || request.RoundNumber != currentRoundNumber)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.StaleRound);
            }

            if (request.OwnerId != ownerId)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.OwnerMismatch);
            }

            if (request.RequestSequence <= 0 || request.RequestSequence <= lastRequestSequence)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.DuplicateOrOutOfOrderRequest);
            }

            // 여기서부터는 요청이 "이 라운드의 새 요청"으로 확정된다.
            // 뒤에서 거부되더라도 같은 번호를 재사용한 재전송은 다시 통과시키지 않는다.
            // 이 갱신은 인벤토리·쿨다운을 건드리지 않는다.
            lastObservedTimeSeconds = context.NowSeconds;
            lastRequestSequence = request.RequestSequence;

            if (context.Phase != ThrowRoundPhase.Preparation && context.Phase != ThrowRoundPhase.Combat)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.PhaseNotAllowed);
            }

            EquipmentInventorySnapshot snapshot = inventory.Snapshot;
            if (request.ConsumableSlotIndex < 0 ||
                request.ConsumableSlotIndex >= EquipmentInventorySnapshot.ConsumableSlotCount)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.InvalidSlot);
            }

            EquipmentInventorySlot slot = snapshot.GetConsumableSlot(request.ConsumableSlotIndex);
            if (slot.IsEmpty)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.InvalidSlot);
            }

            if (!throwingCatalog.TryGetThrowing(slot.ItemId, out ThrowingItemDefinition definition))
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.SlotDoesNotContainThrowable);
            }

            if (!definition.IsValid || definition.ItemId != slot.ItemId)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.InvalidDefinition);
            }

            if (!itemCatalog.TryGet(slot.ItemId, out EquipmentItemDefinition itemDefinition) ||
                !itemDefinition.IsValid ||
                !itemDefinition.IsStackableConsumable)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.InvalidDefinition);
            }

            // 항목 ID·슬롯과 무관한 단일 공유 쿨다운.
            if (context.NowSeconds < cooldownReadyAtSeconds)
            {
                return ThrowUseResult.Rejected(ThrowUseFailure.CooldownActive);
            }

            int consumedQuantity = 0;
            if (context.Phase == ThrowRoundPhase.Combat)
            {
                EquipmentInventoryOperationResult consumeResult =
                    inventory.TryUseConsumable(request.ConsumableSlotIndex, itemCatalog);
                if (!consumeResult.Succeeded)
                {
                    // 소모가 실패하면 쿨다운도 걸지 않는다.
                    return ThrowUseResult.Rejected(ThrowUseFailure.InventoryRejected);
                }

                consumedQuantity = 1;
            }

            cooldownReadyAtSeconds = context.NowSeconds + definition.SharedCooldownSeconds;
            return ThrowUseResult.Success(new AcceptedThrow(
                ownerId,
                currentRoundNumber,
                context.Phase,
                slot.ItemId,
                request.ConsumableSlotIndex,
                request.RequestSequence,
                context.NowSeconds,
                consumedQuantity,
                cooldownReadyAtSeconds,
                definition));
        }
    }
}
