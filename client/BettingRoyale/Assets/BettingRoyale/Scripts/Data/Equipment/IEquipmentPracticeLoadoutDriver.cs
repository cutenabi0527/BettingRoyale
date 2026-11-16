using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Equipment
{
    /// <summary>훈련·프리뷰의 장비 선택도 실제 인벤토리와 외형 동기화 경로를 사용한다. 호출 권한은 세션 어댑터가 검사한다.</summary>
    public interface IEquipmentPracticeLoadoutDriver
    {
        EquipmentInventoryOperationResult TryAuthorityEquipWeapon(WeaponType weapon);
        void ResetPracticeInventory();
    }
}
