namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화 밸런스 데이터의 런타임 조회 경계.
    /// 목업 공급자는 향후 Excel → Binary 공급자로 교체한다 (D-041, S-GD-3PGNS5).
    /// </summary>
    public interface IWeaponUpgradeDataProvider
    {
        int MaxLevel { get; }

        bool TryGetTargetLevel(int targetLevel, out WeaponUpgradeLevelData levelData);
    }
}
