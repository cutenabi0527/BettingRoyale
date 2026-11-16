namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 전투 중 직접 선택할 수 있는 장비 칸. 장신구는 수동 선택·순회 대상이 아니다
    /// (S-GD-YDBKKW §2).
    /// </summary>
    public enum EquipmentFocus
    {
        Weapon = 0,
        Consumable1 = 1,
        Consumable2 = 2,
        Consumable3 = 3,
    }

    /// <summary>
    /// 숫자키·패드 어깨 버튼이 공유하는 장비 포커스 순회 규칙.
    /// 인벤토리를 바꾸지 않으므로 Host와 클라이언트 표현이 같은 스냅샷으로 계산할 수 있다.
    /// </summary>
    public static class EquipmentFocusSelection
    {
        private const int FocusCount = EquipmentInventorySnapshot.ConsumableSlotCount + 1;

        public static bool IsDefined(EquipmentFocus focus) =>
            focus >= EquipmentFocus.Weapon && focus <= EquipmentFocus.Consumable3;

        /// <summary>무기만 손의 무기 모델·보조 손 리그를 표시하는 포커스다.</summary>
        public static bool IsWeaponFocused(EquipmentFocus focus) => focus == EquipmentFocus.Weapon;

        public static bool TryGetConsumableSlotIndex(EquipmentFocus focus, out int slotIndex)
        {
            slotIndex = (int)focus - 1;
            return slotIndex >= 0 && slotIndex < EquipmentInventorySnapshot.ConsumableSlotCount;
        }

        public static bool IsOccupied(in EquipmentInventorySnapshot snapshot, EquipmentFocus focus)
        {
            if (focus == EquipmentFocus.Weapon)
            {
                return snapshot.WeaponItemId > 0;
            }

            return TryGetConsumableSlotIndex(focus, out int slotIndex)
                && !snapshot.GetConsumableSlot(slotIndex).IsEmpty;
        }

        /// <summary>
        /// 비어 있는 현재 포커스를 다음 보유 칸으로 정상화한다. 모두 비었으면 Weapon을 반환해
        /// 잘못된 enum 값이 UI·복제 상태에 남지 않게 한다.
        /// </summary>
        public static EquipmentFocus Normalize(in EquipmentInventorySnapshot snapshot, EquipmentFocus focus)
        {
            EquipmentFocus current = IsDefined(focus) ? focus : EquipmentFocus.Weapon;
            if (IsOccupied(snapshot, current))
            {
                return current;
            }

            return FindNextOccupied(snapshot, current, step: 1);
        }

        /// <summary>
        /// LB/RB 순회를 위한 다음 보유 칸을 찾는다. 빈 슬롯과 장신구는 건너뛰고, 방향은 0이면
        /// 앞으로 처리한다. 보유한 선택 칸이 하나도 없으면 Weapon을 반환한다.
        /// </summary>
        public static EquipmentFocus FindNextOccupied(
            in EquipmentInventorySnapshot snapshot,
            EquipmentFocus current,
            int step)
        {
            int direction = step < 0 ? -1 : 1;
            int currentIndex = IsDefined(current) ? (int)current : (int)EquipmentFocus.Weapon;
            for (int offset = 1; offset <= FocusCount; offset++)
            {
                int candidateIndex = (currentIndex + direction * offset) % FocusCount;
                if (candidateIndex < 0)
                {
                    candidateIndex += FocusCount;
                }

                EquipmentFocus candidate = (EquipmentFocus)candidateIndex;
                if (IsOccupied(snapshot, candidate))
                {
                    return candidate;
                }
            }

            return EquipmentFocus.Weapon;
        }
    }
}
