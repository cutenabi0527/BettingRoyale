using BettingRoyale.Core;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>현재 장착한 주무기 인스턴스의 다음 강화 단계를 요청한다 (S-GD-3PGNS5).</summary>
    /// <remarks>송신자는 전송 계층이 식별하므로 PlayerId를 담지 않는다 (D-069).</remarks>
    public readonly struct WeaponUpgradeCommand : IGameEvent
    {
    }

    public enum WeaponUpgradeRejectReason
    {
        None = 0,
        NotEnoughGold = 1,
        MaxLevelReached = 2,
        NotPreparationRound = 3,
        PreparationClosed = 4,
        DataUndefined = 5,
    }

    /// <summary>강화 판정이 성공해 단계가 상승했다.</summary>
    public readonly struct WeaponUpgradeSucceeded : IGameEvent
    {
        public readonly int PlayerId;
        public readonly bool IsMine;
        public readonly int Round;
        public readonly int Level;
        public readonly int CostGold;
        public readonly int AttackPowerBonusPercent;
        public readonly int Gold;
        public WeaponUpgradeSucceeded(
            int playerId,
            bool isMine,
            int round,
            int level,
            int costGold,
            int attackPowerBonusPercent,
            int gold)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Round = round;
            Level = level;
            CostGold = costGold;
            AttackPowerBonusPercent = attackPowerBonusPercent;
            Gold = gold;
        }
    }

    /// <summary>비용은 차감됐지만 확률 판정에 실패해 단계가 유지됐다.</summary>
    public readonly struct WeaponUpgradeFailed : IGameEvent
    {
        public readonly int PlayerId;
        public readonly bool IsMine;
        public readonly int Round;
        public readonly int Level;
        public readonly int CostGold;
        public readonly int Gold;

        public WeaponUpgradeFailed(int playerId, bool isMine, int round, int level, int costGold, int gold)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Round = round;
            Level = level;
            CostGold = costGold;
            Gold = gold;
        }
    }

    /// <summary>강화 요청이 판정 전에 거절됐다. 골드와 강화 단계는 변하지 않는다.</summary>
    public readonly struct WeaponUpgradeRejected : IGameEvent
    {
        public readonly int PlayerId;
        public readonly bool IsMine;
        public readonly WeaponUpgradeRejectReason Reason;

        public WeaponUpgradeRejected(int playerId, bool isMine, WeaponUpgradeRejectReason reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Reason = reason;
        }
    }

    /// <summary>
    /// 플레이어 주무기의 강화 전체 상태. 이벤트를 놓쳐도 다음 상태 하나로 화면을 복구한다.
    /// </summary>
    public readonly struct WeaponUpgradeStateChanged : IGameEvent
    {
        public readonly int PlayerId;
        public readonly bool IsMine;
        public readonly int Round;
        public readonly int Level;
        public readonly int MaxLevel;
        public readonly int AttackPowerBonusPercent;
        public readonly int Gold;
        public readonly bool HasNextLevel;
        public readonly int NextCostGold;
        public readonly int NextSuccessRatePercent;
        public readonly WeaponUpgradeRejectReason BlockedBy;

        public bool CanAttempt => BlockedBy == WeaponUpgradeRejectReason.None;

        public WeaponUpgradeStateChanged(
            int playerId,
            bool isMine,
            int round,
            int level,
            int maxLevel,
            int attackPowerBonusPercent,
            int gold,
            bool hasNextLevel,
            int nextCostGold,
            int nextSuccessRatePercent,
            WeaponUpgradeRejectReason blockedBy)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Round = round;
            Level = level;
            MaxLevel = maxLevel;
            AttackPowerBonusPercent = attackPowerBonusPercent;
            Gold = gold;
            HasNextLevel = hasNextLevel;
            NextCostGold = nextCostGold;
            NextSuccessRatePercent = nextSuccessRatePercent;
            BlockedBy = blockedBy;
        }
    }
}
