using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 한 렌더 프레임에서 수집한 이동·조준·대시·공격·무기 전환 의도. 네트워크 구현을 모르는 Gameplay와
    /// Fusion 어댑터가 이 값 타입만 공유한다.
    /// </summary>
    public readonly struct CombatMotionInputFrame
    {
        public static readonly CombatMotionInputFrame Neutral = new(
            Vector2.zero,
            Vector2.zero,
            hasAim: false,
            dashPressed: false,
            attackPressed: false,
            nextWeaponPressed: false,
            previousWeaponPressed: false,
            useConsumable1Pressed: false,
            useConsumable2Pressed: false,
            useConsumable3Pressed: false,
            pickupPressed: false,
            selectWeaponPressed: false,
            selectConsumable1Pressed: false,
            selectConsumable2Pressed: false,
            selectConsumable3Pressed: false,
            nextEquipmentFocusPressed: false,
            previousEquipmentFocusPressed: false);

        public readonly CombatThrowAim ThrowAim;
        public readonly Vector2 MoveDirection;
        public readonly Vector2 AimDirection;
        public readonly bool HasAim;
        public readonly bool DashPressed;
        public readonly bool AttackPressed;
        public readonly bool NextWeaponPressed;
        public readonly bool PreviousWeaponPressed;
        public readonly bool UseConsumable1Pressed;
        public readonly bool UseConsumable2Pressed;
        public readonly bool UseConsumable3Pressed;
        public readonly bool PickupPressed;
        public readonly bool SelectWeaponPressed;
        public readonly bool SelectConsumable1Pressed;
        public readonly bool SelectConsumable2Pressed;
        public readonly bool SelectConsumable3Pressed;
        public readonly bool NextEquipmentFocusPressed;
        public readonly bool PreviousEquipmentFocusPressed;

        /// <summary>액티브 축복 켜기 — 키보드 Q · 게임패드 Y (S-GD-NJQ5NM).</summary>
        public readonly bool ActiveBlessingPressed;

        public CombatMotionInputFrame(
            Vector2 moveDirection,
            Vector2 aimDirection,
            bool hasAim,
            bool dashPressed)
            : this(
                moveDirection,
                aimDirection,
                hasAim,
                dashPressed,
                attackPressed: false,
                nextWeaponPressed: false,
                previousWeaponPressed: false,
                useConsumable1Pressed: false,
                useConsumable2Pressed: false,
                useConsumable3Pressed: false,
                pickupPressed: false,
                selectWeaponPressed: false,
                selectConsumable1Pressed: false,
                selectConsumable2Pressed: false,
                selectConsumable3Pressed: false,
                nextEquipmentFocusPressed: false,
                previousEquipmentFocusPressed: false)
        {
        }

        public CombatMotionInputFrame(
            Vector2 moveDirection,
            Vector2 aimDirection,
            bool hasAim,
            bool dashPressed,
            bool attackPressed,
            bool nextWeaponPressed,
            bool previousWeaponPressed)
            : this(
                moveDirection,
                aimDirection,
                hasAim,
                dashPressed,
                attackPressed,
                nextWeaponPressed,
                previousWeaponPressed,
                useConsumable1Pressed: false,
                useConsumable2Pressed: false,
                useConsumable3Pressed: false,
                pickupPressed: false,
                selectWeaponPressed: false,
                selectConsumable1Pressed: false,
                selectConsumable2Pressed: false,
                selectConsumable3Pressed: false,
                nextEquipmentFocusPressed: false,
                previousEquipmentFocusPressed: false)
        {
        }

        public CombatMotionInputFrame(
            Vector2 moveDirection,
            Vector2 aimDirection,
            bool hasAim,
            bool dashPressed,
            bool attackPressed,
            bool nextWeaponPressed,
            bool previousWeaponPressed,
            bool useConsumable1Pressed,
            bool useConsumable2Pressed,
            bool useConsumable3Pressed,
            bool pickupPressed)
            : this(
                moveDirection,
                aimDirection,
                hasAim,
                dashPressed,
                attackPressed,
                nextWeaponPressed,
                previousWeaponPressed,
                useConsumable1Pressed,
                useConsumable2Pressed,
                useConsumable3Pressed,
                pickupPressed,
                selectWeaponPressed: false,
                selectConsumable1Pressed: false,
                selectConsumable2Pressed: false,
                selectConsumable3Pressed: false,
                nextEquipmentFocusPressed: false,
                previousEquipmentFocusPressed: false)
        {
        }

        public CombatMotionInputFrame(
            Vector2 moveDirection,
            Vector2 aimDirection,
            bool hasAim,
            bool dashPressed,
            bool attackPressed,
            bool nextWeaponPressed,
            bool previousWeaponPressed,
            bool useConsumable1Pressed,
            bool useConsumable2Pressed,
            bool useConsumable3Pressed,
            bool pickupPressed,
            bool selectWeaponPressed,
            bool selectConsumable1Pressed,
            bool selectConsumable2Pressed,
            bool selectConsumable3Pressed,
            bool nextEquipmentFocusPressed,
            bool previousEquipmentFocusPressed,
            CombatThrowAim throwAim = default,
            bool activeBlessingPressed = false)
        {
            ThrowAim = throwAim;
            MoveDirection = moveDirection;
            AimDirection = aimDirection;
            HasAim = hasAim;
            DashPressed = dashPressed;
            AttackPressed = attackPressed;
            NextWeaponPressed = nextWeaponPressed;
            PreviousWeaponPressed = previousWeaponPressed;
            UseConsumable1Pressed = useConsumable1Pressed;
            UseConsumable2Pressed = useConsumable2Pressed;
            UseConsumable3Pressed = useConsumable3Pressed;
            PickupPressed = pickupPressed;
            SelectWeaponPressed = selectWeaponPressed;
            SelectConsumable1Pressed = selectConsumable1Pressed;
            SelectConsumable2Pressed = selectConsumable2Pressed;
            SelectConsumable3Pressed = selectConsumable3Pressed;
            NextEquipmentFocusPressed = nextEquipmentFocusPressed;
            PreviousEquipmentFocusPressed = previousEquipmentFocusPressed;
            ActiveBlessingPressed = activeBlessingPressed;
        }
    }
}
