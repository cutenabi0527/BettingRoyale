namespace BettingRoyale.Data.Contracts
{
    /// <summary>한 피어가 보고한 매치 진입 준비율. 송신자 ID는 연결에서 붙인다.</summary>
    public readonly struct MatchLoadingProgressReceived : BettingRoyale.Core.IGameEvent
    {
        public MatchLoadingProgressReceived(int playerId, byte percent)
        {
            PlayerId = playerId;
            Percent = percent > 100 ? (byte)100 : percent;
        }

        public int PlayerId { get; }
        public byte Percent { get; }
    }

    /// <summary>로컬 플레이어가 준비 라운드의 축복 후보를 받아 즉시 선택할 수 있게 된 시점.</summary>
    public readonly struct LocalPreparationSelectionReady : BettingRoyale.Core.IGameEvent
    {
        public LocalPreparationSelectionReady(int round) => Round = round;
        public int Round { get; }
    }
}
