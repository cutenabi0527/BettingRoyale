namespace BettingRoyale.Data.Upgrade
{
    /// <summary>무기 강화 시도의 도메인 결과 (S-GD-3PGNS5).</summary>
    public enum WeaponUpgradeAttemptResult
    {
        Success = 0,
        Failed = 1,
        NotEnoughGold = 2,
        MaxLevelReached = 3,
        DataUndefined = 4,
    }
}
