namespace BettingRoyale.Data.Chest
{
    /// <summary>Host가 peer에 전달할 상자 하나의 최소 상태. 위치는 결정된 공용 앵커에서 소비한다.</summary>
    public readonly struct ChestAuthorityState
    {
        public ChestAuthorityState(int chestId, ChestState state, int openerId, int elapsedTicks, int openDurationTicks,
            int openingSequence = 0, int completionSequence = 0)
        {
            ChestId = chestId;
            State = state;
            OpenerId = openerId;
            ElapsedTicks = elapsedTicks;
            OpenDurationTicks = openDurationTicks;
            OpeningSequence = openingSequence;
            CompletionSequence = completionSequence;
        }

        public int ChestId { get; }
        public ChestState State { get; }
        public int OpenerId { get; }
        public int ElapsedTicks { get; }
        public int OpenDurationTicks { get; }
        public int OpeningSequence { get; }
        public int CompletionSequence { get; }
    }
}
