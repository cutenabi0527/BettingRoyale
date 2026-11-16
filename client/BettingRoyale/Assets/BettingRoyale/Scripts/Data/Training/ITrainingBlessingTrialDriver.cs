using System;

namespace BettingRoyale.Data.Training
{
    /// <summary>훈련 권위 어댑터가 월드에 전달하는 개인 시험 계약.</summary>
    public interface ITrainingBlessingTrialDriver
    {
        /// <param name="gold">시험 지갑의 골드(탐욕의 공격력 환산).</param>
        /// <param name="goldReward">전투 중에 번 골드(사냥꾼 처치·이자)를 받을 곳.</param>
        void ApplyTrainingTrial(int cycle, bool combat, bool deposited,
            float blindBetHealthCostFraction = 0f, int gold = 0, Func<int, bool> goldReward = null);
    }
}
