using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 이동 명령과 시뮬레이션의 Fusion 비의존 경계. 로컬 Update와 Host 네트워크 틱이
    /// 같은 Gameplay 구현을 구동하고, 원격 피어는 복제된 표현 상태만 적용한다.
    /// </summary>
    public interface ICombatMotionDriver : IMoveCommandHandler, IAimCommandHandler, IDashCommandHandler
    {
        Vector2 MoveDirection { get; }
        Vector2 AimDirection { get; }
        bool IsDashing { get; }
        int DashCharges { get; }
        int MaxDashCharges { get; }
        float DashRechargeDurationSeconds { get; }
        float DashRechargeRemainingSeconds { get; }
        Transform RootTransform { get; }
        Vector3 GhostPosition { get; }
        bool IsGhostActive { get; }

        void Tick(float deltaTime, float now);
        void SetExternalSimulation(bool externallySimulated);
        void ApplyPresentation(in CombatMotionPresentation presentation);
        void BeginDownedTransition();
        void EnterGhostState();
        void ResetForNextRound();
    }
}
