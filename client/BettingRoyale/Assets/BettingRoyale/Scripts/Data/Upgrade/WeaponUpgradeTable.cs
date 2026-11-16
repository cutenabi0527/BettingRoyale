using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 외부에서 읽은 강화 데이터 행을 검증하고 불변 조회 형태로 보관한다.
    /// </summary>
    public sealed class WeaponUpgradeTable : IWeaponUpgradeDataProvider
    {
        private readonly WeaponUpgradeLevelData[] levels;

        public WeaponUpgradeTable(IReadOnlyList<WeaponUpgradeLevelData> levels)
        {
            if (levels == null)
            {
                throw new ArgumentNullException(nameof(levels));
            }

            if (levels.Count == 0)
            {
                throw new ArgumentException("강화 데이터에는 한 단계 이상이 필요합니다.", nameof(levels));
            }

            this.levels = new WeaponUpgradeLevelData[levels.Count];
            var previousBonusPercent = -1;

            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                var expectedLevel = i + 1;
                if (level.TargetLevel != expectedLevel)
                {
                    throw new ArgumentException(
                        $"강화 단계는 1부터 연속이어야 합니다. 예상 {expectedLevel}, 실제 {level.TargetLevel}.",
                        nameof(levels));
                }

                if (level.AttackPowerBonusPercent < previousBonusPercent)
                {
                    throw new ArgumentException(
                        $"{level.TargetLevel}강 공격력 보너스가 이전 단계보다 낮습니다.",
                        nameof(levels));
                }

                this.levels[i] = level;
                previousBonusPercent = level.AttackPowerBonusPercent;
            }
        }

        public int MaxLevel => levels.Length;

        public bool TryGetTargetLevel(int targetLevel, out WeaponUpgradeLevelData levelData)
        {
            if (targetLevel < 1 || targetLevel > levels.Length)
            {
                levelData = default;
                return false;
            }

            levelData = levels[targetLevel - 1];
            return true;
        }
    }
}
