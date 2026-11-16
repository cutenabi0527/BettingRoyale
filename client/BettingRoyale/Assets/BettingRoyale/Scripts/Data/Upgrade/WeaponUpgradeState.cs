using System;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 무기 인스턴스 하나의 라운드 한정 강화 상태와 판정을 소유한다.
    /// 준비 페이즈와 요청 중복 여부는 이 객체를 호출하기 전에 Host 어댑터가 판정한다.
    /// </summary>
    public sealed class WeaponUpgradeState
    {
        private readonly IWeaponUpgradeDataProvider dataProvider;
        private readonly DeterministicRandom random;

        public WeaponUpgradeState(
            IWeaponUpgradeDataProvider dataProvider,
            DeterministicRandom random)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int Level { get; private set; }

        public int AttackPowerBonusPercent
        {
            get
            {
                if (Level == 0)
                {
                    return 0;
                }

                return dataProvider.TryGetTargetLevel(Level, out var levelData)
                    ? levelData.AttackPowerBonusPercent
                    : 0;
            }
        }

        public bool TryGetNextLevel(out WeaponUpgradeLevelData levelData) =>
            dataProvider.TryGetTargetLevel(Level + 1, out levelData);

        public WeaponUpgradeAttemptResult TryUpgrade(Wallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (Level >= dataProvider.MaxLevel)
            {
                return WeaponUpgradeAttemptResult.MaxLevelReached;
            }

            if (!dataProvider.TryGetTargetLevel(Level + 1, out var target))
            {
                return WeaponUpgradeAttemptResult.DataUndefined;
            }

            if (!wallet.TrySpend(target.CostGold))
            {
                return WeaponUpgradeAttemptResult.NotEnoughGold;
            }

            if (random.Next(100) >= target.SuccessRatePercent)
            {
                return WeaponUpgradeAttemptResult.Failed;
            }

            Level = target.TargetLevel;
            return WeaponUpgradeAttemptResult.Success;
        }

        /// <summary>강화 효과가 적용된 전투 라운드 종료 시 0강으로 되돌린다.</summary>
        public void ResetAfterCombatRound() => Level = 0;
    }
}
