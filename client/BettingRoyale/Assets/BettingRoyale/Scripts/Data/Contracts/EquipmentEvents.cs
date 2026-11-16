using BettingRoyale.Core;
using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>Host가 확정한 장비 인벤토리 전체 상태. HUD는 IsMine으로 자기 슬롯만 골라 그린다.</summary>
    public readonly struct EquipmentInventoryStateChanged : IGameEvent
    {
        /// <summary>확정 상태의 소유자. 레거시 로컬 발행은 -1이며 <see cref="IsMine"/>으로만 소비한다.</summary>
        public readonly int PlayerId;
        public readonly EquipmentInventorySnapshot Snapshot;
        public readonly bool IsMine;

        public EquipmentInventoryStateChanged(in EquipmentInventorySnapshot snapshot, bool isMine)
            : this(-1, snapshot, isMine)
        {
        }

        public EquipmentInventoryStateChanged(int playerId, in EquipmentInventorySnapshot snapshot, bool isMine)
        {
            PlayerId = playerId;
            Snapshot = snapshot;
            IsMine = isMine;
        }
    }

    /// <summary>
    /// Host가 확정한 현재 장비 포커스. 인벤토리 수량과 독립적으로 바뀌므로 HUD는 마지막
    /// <see cref="EquipmentInventoryStateChanged"/> 스냅샷과 조합해 선택 테두리만 갱신한다.
    /// </summary>
    public readonly struct EquipmentFocusChanged : IGameEvent
    {
        public readonly EquipmentFocus Focus;
        public readonly bool IsMine;

        public EquipmentFocusChanged(EquipmentFocus focus, bool isMine)
        {
            Focus = focus;
            IsMine = isMine;
        }
    }

    /// <summary>획득 또는 소비품 사용이 거절된 경우의 비차단 피드백.</summary>
    public readonly struct EquipmentInteractionRejected : IGameEvent
    {
        public readonly EquipmentInventoryFailure Failure;

        public EquipmentInteractionRejected(EquipmentInventoryFailure failure)
        {
            Failure = failure;
        }
    }

    /// <summary>로컬 입력 권한자에게만 표시하는 가까운 월드 드롭 획득 안내 상태.</summary>
    public readonly struct EquipmentPickupPromptChanged : IGameEvent
    {
        public readonly bool IsAvailable;

        public EquipmentPickupPromptChanged(bool isAvailable)
        {
            IsAvailable = isAvailable;
        }
    }
}
