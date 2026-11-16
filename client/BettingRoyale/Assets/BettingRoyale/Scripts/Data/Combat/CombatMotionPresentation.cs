using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 본체 위치·회전은 NetworkTransform이 담당하고, 분리된 유령 위치와 이동·대시 표현은
    /// 이 값으로 원격 캐릭터의 기존 Gameplay 경로에 전달한다.
    /// </summary>
    public readonly struct CombatMotionPresentation
    {
        public readonly Vector2 MoveDirection;
        public readonly Vector2 AimDirection;
        public readonly bool IsDashing;
        public readonly int DashCharges;
        public readonly int MaxDashCharges;
        public readonly float DashRechargeDurationSeconds;
        public readonly float DashRechargeRemainingSeconds;
        public readonly Vector3 GhostPosition;
        public readonly bool IsGhostActive;
        public readonly Vector2 DashDirection;

        public CombatMotionPresentation(Vector2 moveDirection, Vector2 aimDirection, bool isDashing)
            : this(
                moveDirection,
                aimDirection,
                isDashing,
                dashCharges: 0,
                maxDashCharges: 0,
                dashRechargeDurationSeconds: 0f,
                dashRechargeRemainingSeconds: 0f,
                ghostPosition: Vector3.zero,
                isGhostActive: false)
        {
        }

        public CombatMotionPresentation(
            Vector2 moveDirection,
            Vector2 aimDirection,
            bool isDashing,
            int dashCharges,
            int maxDashCharges,
            float dashRechargeDurationSeconds,
            float dashRechargeRemainingSeconds)
            : this(
                moveDirection,
                aimDirection,
                isDashing,
                dashCharges,
                maxDashCharges,
                dashRechargeDurationSeconds,
                dashRechargeRemainingSeconds,
                Vector3.zero,
                isGhostActive: false)
        {
        }

        public CombatMotionPresentation(
            Vector2 moveDirection,
            Vector2 aimDirection,
            bool isDashing,
            int dashCharges,
            int maxDashCharges,
            float dashRechargeDurationSeconds,
            float dashRechargeRemainingSeconds,
            Vector3 ghostPosition,
            bool isGhostActive,
            Vector2 dashDirection = default)
        {
            MoveDirection = moveDirection;
            AimDirection = aimDirection;
            IsDashing = isDashing;
            DashCharges = dashCharges;
            MaxDashCharges = maxDashCharges;
            DashRechargeDurationSeconds = dashRechargeDurationSeconds;
            DashRechargeRemainingSeconds = dashRechargeRemainingSeconds;
            GhostPosition = ghostPosition;
            IsGhostActive = isGhostActive;
            DashDirection = dashDirection;
        }
    }
}
