using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Combat
{
    /// <summary>Fusion 어댑터와 훈련 월드 사이의 경계. 상대 레이어의 구현 컴포넌트를 찾지 않는다.</summary>
    public interface ITrainingWorldRuntime
    {
        bool IsReady { get; }
        bool TryGetSpawn(int npcIndex, int playerSlot, out WorldPosition position);
        void BindActor(int actorId, int npcIndex, bool isLocal, bool hasAuthority);
        void RemoveActor(int actorId);
        CombatMotionInputFrame TickBot(int actorId, float deltaTime, float now);
        void TickTargets(float sharedSeconds, bool hasAuthority);
        void SetPlayerDamageEnabled(bool enabled);
        void SetDamageResolver(ICombatDamageResolver resolver);
        bool TryTravel(int actorId, int zoneId, out WorldPosition position, out string reason);
        bool Recover(int actorId);
        bool IsDowned(int actorId);
    }
}
