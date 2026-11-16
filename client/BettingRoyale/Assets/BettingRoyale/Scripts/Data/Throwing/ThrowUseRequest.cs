namespace BettingRoyale.Data.Throwing
{
    /// <summary>
    /// 라운드 진행 단계. 투척 승인에 필요한 최소 구분만 담는다.
    /// </summary>
    /// <remarks>
    /// 이 값은 도메인 밖(라운드 상태 기계)에서 공급되는 권한 정보다. 도메인은 단계를 스스로 진행시키지 않는다.
    /// </remarks>
    public enum ThrowRoundPhase
    {
        /// <summary>알 수 없음. 항상 거부된다.</summary>
        None = 0,

        /// <summary>준비 단계. 수량을 소모하지 않는 연습 투척만 허용한다.</summary>
        Preparation = 1,

        /// <summary>전투 단계. 승인되면 수량 1을 소모한다.</summary>
        Combat = 2,

        /// <summary>결과·정산 등 그 외 단계. 항상 거부된다.</summary>
        Resolution = 3,
    }

    /// <summary>
    /// 호출자가 공급하는 현재 라운드 권한과 단조 증가 시각.
    /// </summary>
    public readonly struct ThrowRoundContext
    {
        public readonly int RoundNumber;
        public readonly ThrowRoundPhase Phase;

        /// <summary>유한한 단조 증가 절대 시각(초).</summary>
        public readonly double NowSeconds;

        public ThrowRoundContext(int roundNumber, ThrowRoundPhase phase, double nowSeconds)
        {
            RoundNumber = roundNumber;
            Phase = phase;
            NowSeconds = nowSeconds;
        }
    }

    /// <summary>
    /// 한 번의 투척 사용 요청. 소유자·라운드·슬롯·요청 일련번호를 모두 명시한다.
    /// </summary>
    /// <remarks>
    /// 조준 방향, 사거리 제한, 선택 UI, 입력 잠금 등은 호출자(어댑터)의 책임이며 이 요청에 포함하지 않는다.
    /// </remarks>
    public readonly struct ThrowUseRequest
    {
        public readonly int OwnerId;
        public readonly int RoundNumber;
        public readonly int ConsumableSlotIndex;

        /// <summary>플레이어별로 엄격히 증가하는 요청 일련번호. 중복·역행 요청 차단에 쓴다.</summary>
        public readonly int RequestSequence;

        public ThrowUseRequest(int ownerId, int roundNumber, int consumableSlotIndex, int requestSequence)
        {
            OwnerId = ownerId;
            RoundNumber = roundNumber;
            ConsumableSlotIndex = consumableSlotIndex;
            RequestSequence = requestSequence;
        }
    }
}
