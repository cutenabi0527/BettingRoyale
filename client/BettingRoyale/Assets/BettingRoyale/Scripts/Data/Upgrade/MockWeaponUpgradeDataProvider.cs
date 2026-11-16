namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// Excel 기반 강화 데이터가 준비되기 전 Jira S15P21A606-98 검증에 사용하는 목업 공급자.
    /// 최종 밸런스 정본이 아니며 수치는 S-GD-3PGNS5를 따른다.
    /// </summary>
    public sealed class MockWeaponUpgradeDataProvider : IWeaponUpgradeDataProvider
    {
        private readonly WeaponUpgradeTable table = new(new[]
        {
            new WeaponUpgradeLevelData(1, 100, 100, 10),
            new WeaponUpgradeLevelData(2, 200, 70, 25),
            new WeaponUpgradeLevelData(3, 400, 40, 50),
        });

        public int MaxLevel => table.MaxLevel;

        public bool TryGetTargetLevel(int targetLevel, out WeaponUpgradeLevelData levelData) =>
            table.TryGetTargetLevel(targetLevel, out levelData);
    }
}
