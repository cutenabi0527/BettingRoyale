namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// Host가 이미 확정한 상자 수령을 관찰하는 경계. 상자 판정부는 무엇을 줄지 모르고, 구현이 정한다 —
    /// 제품 Host는 <see cref="ChestRewardPayout"/> 로 300G + 아이템을 지급한다(S-GD-DDXPX9 · S-GD-BPB9S3).
    /// callback은 상태 전이 뒤에 오며 실패해도 상자 상태를 되돌리지 않는다.
    /// </summary>
    public interface IChestRewardAdapter
    {
        void NotifyRewardClaimed(int playerId, int chestId);
    }
}
