namespace BettingRoyale.Data.Preparation
{
    /// <summary>실제 시설 거리 또는 훈련 세션의 접근 권한을 공용 거래 게이트에 공급한다.</summary>
    public interface IPreparationAccessPolicy
    {
        bool CanUse(int playerId, PreparationFacility facility);
    }
}
