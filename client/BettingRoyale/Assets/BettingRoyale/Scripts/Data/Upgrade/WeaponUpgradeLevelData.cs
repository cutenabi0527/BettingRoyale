using System;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>한 강화 목표 단계의 데이터 행 (S-GD-3PGNS5).</summary>
    public readonly struct WeaponUpgradeLevelData
    {
        public WeaponUpgradeLevelData(
            int targetLevel,
            int costGold,
            int successRatePercent,
            int attackPowerBonusPercent)
        {
            if (targetLevel < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetLevel), targetLevel, "목표 강화 단계는 1 이상이어야 합니다.");
            }

            if (costGold < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(costGold), costGold, "강화 비용은 음수일 수 없습니다.");
            }

            if (successRatePercent < 0 || successRatePercent > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(successRatePercent), successRatePercent, "성공률은 0% 이상 100% 이하여야 합니다.");
            }

            if (attackPowerBonusPercent < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attackPowerBonusPercent), attackPowerBonusPercent, "공격력 보너스는 음수일 수 없습니다.");
            }

            TargetLevel = targetLevel;
            CostGold = costGold;
            SuccessRatePercent = successRatePercent;
            AttackPowerBonusPercent = attackPowerBonusPercent;
        }

        public int TargetLevel { get; }

        public int CostGold { get; }

        public int SuccessRatePercent { get; }

        public int AttackPowerBonusPercent { get; }
    }
}
