using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>체력 판정과 복제 표현을 잇는 Fusion 비의존 경계.</summary>
    public interface ICombatHealthDriver : IDamageable
    {
        float CurrentHealth { get; }
        float MaxHealth { get; }
        Transform RootTransform { get; }

        void ApplyPresentation(float currentHealth, bool isDowned);

        /// <summary>
        /// 권위 피어가 확정한 최대 체력까지 적용한다. 구현은 공급된 값을 모두 검증한 뒤에만 상태를 바꾸고,
        /// 최대 체력을 현재 체력보다 먼저 적용한다. 적용 여부를 돌려주며 이벤트는 만들지 않는다.
        /// </summary>
        bool ApplyPresentation(float maxHealth, float currentHealth, bool isDowned);
    }
}
