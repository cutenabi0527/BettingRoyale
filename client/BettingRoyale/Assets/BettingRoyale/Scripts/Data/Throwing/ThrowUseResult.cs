namespace BettingRoyale.Data.Throwing
{
    /// <summary>투척 사용 요청이 거부된 이유.</summary>
    public enum ThrowUseFailure
    {
        None = 0,

        /// <summary>BeginRound 가 아직 호출되지 않았다.</summary>
        RoundNotStarted = 1,

        /// <summary>요청 또는 문맥의 라운드가 현재 라운드와 다르다.</summary>
        StaleRound = 2,

        /// <summary>요청 소유자가 이 게이트의 소유자와 다르다.</summary>
        OwnerMismatch = 3,

        /// <summary>이미 처리한 번호이거나 순서가 뒤바뀐 요청이다.</summary>
        DuplicateOrOutOfOrderRequest = 4,

        /// <summary>NaN·무한대이거나 과거로 역행하는 시각이다.</summary>
        InvalidTime = 5,

        /// <summary>해당 단계에서는 투척이 허용되지 않는다.</summary>
        PhaseNotAllowed = 6,

        /// <summary>슬롯 번호가 범위를 벗어났거나 비어 있다.</summary>
        InvalidSlot = 7,

        /// <summary>슬롯의 항목이 투척 소비품이 아니다.</summary>
        SlotDoesNotContainThrowable = 8,

        /// <summary>카탈로그 정의가 무효다(default·NaN·무한대·음수 등).</summary>
        InvalidDefinition = 9,

        /// <summary>공유 쿨다운이 아직 남아 있다.</summary>
        CooldownActive = 10,

        /// <summary>인벤토리가 소모를 거부했다.</summary>
        InventoryRejected = 11,

        /// <summary>기절 중이다(S-GD-9MWJRR). 투척물도 소모품이라 쓰지 못한다.</summary>
        Stunned = 12,
    }

    /// <summary>
    /// 승인된 투척 1건의 불변 정보. Host 어댑터가 이 값을 받아 실제 스폰·복제를 수행한다.
    /// </summary>
    /// <remarks>이 구조체 자체는 아무것도 스폰하지 않으며 스폰 성공을 가정하지도 않는다.</remarks>
    public readonly struct AcceptedThrow
    {
        public readonly int OwnerId;
        public readonly int RoundNumber;
        public readonly ThrowRoundPhase Phase;
        public readonly int ItemId;
        public readonly int ConsumableSlotIndex;
        public readonly int RequestSequence;

        /// <summary>승인 시각(초). 투척체 발사 시각의 기준이다.</summary>
        public readonly double AcceptedAtSeconds;

        /// <summary>실제로 소모된 수량. 전투 1, 준비 0.</summary>
        public readonly int ConsumedQuantity;

        /// <summary>이번 승인으로 갱신된 공유 쿨다운 종료 시각(초).</summary>
        public readonly double CooldownReadyAtSeconds;

        public readonly ThrowingItemDefinition Definition;

        public AcceptedThrow(
            int ownerId,
            int roundNumber,
            ThrowRoundPhase phase,
            int itemId,
            int consumableSlotIndex,
            int requestSequence,
            double acceptedAtSeconds,
            int consumedQuantity,
            double cooldownReadyAtSeconds,
            in ThrowingItemDefinition definition)
        {
            OwnerId = ownerId;
            RoundNumber = roundNumber;
            Phase = phase;
            ItemId = itemId;
            ConsumableSlotIndex = consumableSlotIndex;
            RequestSequence = requestSequence;
            AcceptedAtSeconds = acceptedAtSeconds;
            ConsumedQuantity = consumedQuantity;
            CooldownReadyAtSeconds = cooldownReadyAtSeconds;
            Definition = definition;
        }
    }

    /// <summary>투척 사용 승인 결과. 성공 시에만 <see cref="Accepted"/> 가 의미를 가진다.</summary>
    public readonly struct ThrowUseResult
    {
        public readonly bool Succeeded;
        public readonly ThrowUseFailure Failure;
        public readonly AcceptedThrow Accepted;

        private ThrowUseResult(bool succeeded, ThrowUseFailure failure, in AcceptedThrow accepted)
        {
            Succeeded = succeeded;
            Failure = failure;
            Accepted = accepted;
        }

        public static ThrowUseResult Success(in AcceptedThrow accepted) =>
            new(true, ThrowUseFailure.None, accepted);

        public static ThrowUseResult Rejected(ThrowUseFailure failure) =>
            new(false, failure, default);
    }
}
