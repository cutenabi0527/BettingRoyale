using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>공격·무기 상태를 Host에서 판정하고 원격 피어에 표시하기 위한 Fusion 비의존 경계.</summary>
    public interface ICombatActionDriver
    {
        WeaponType EquippedWeapon { get; }
        float AttackCooldownDurationSeconds { get; }

        bool TryAttack(float now);
        float GetAttackCooldownRemaining(float now);
        void EquipNext();
        void EquipPrevious();
        void SetReplicatedPresentation(bool enabled);
        void ApplyPresentation(WeaponType weapon, float cooldownDurationSeconds, float cooldownRemainingSeconds);
        void PresentReplicatedAttack(WeaponType weapon, Vector2 aimDirection);
    }
}
