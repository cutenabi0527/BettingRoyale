using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>CombatSandbox의 Host 권위 NPC와 세 훈련 표적을 표현하는 네트워크 비의존 스냅샷.</summary>
    public readonly struct CombatSandboxStateSnapshot
    {
        public readonly struct TargetState
        {
            public readonly float CurrentHealth;
            public readonly bool IsDowned;
            public readonly int CharacterIndex;
            public readonly int OutfitIndex;
            public readonly int HitSequence;

            public TargetState(
                float currentHealth,
                bool isDowned,
                int characterIndex,
                int outfitIndex,
                int hitSequence = 0)
            {
                CurrentHealth = currentHealth;
                IsDowned = isDowned;
                CharacterIndex = characterIndex;
                OutfitIndex = outfitIndex;
                HitSequence = hitSequence;
            }
        }

        public readonly bool IsValid;
        public readonly Vector3 NpcPosition;
        public readonly Quaternion NpcRotation;
        public readonly int NpcMode;
        public readonly bool IsNpcInvulnerable;
        public readonly Vector2 NpcMoveDirection;
        public readonly Vector2 NpcAimDirection;
        public readonly bool IsNpcDashing;
        public readonly int NpcAttackSequence;
        public readonly TargetState FirstTarget;
        public readonly TargetState SecondTarget;
        public readonly TargetState ThirdTarget;

        public CombatSandboxStateSnapshot(
            bool isValid,
            Vector3 npcPosition,
            Quaternion npcRotation,
            int npcMode,
            bool isNpcInvulnerable,
            Vector2 npcMoveDirection,
            Vector2 npcAimDirection,
            bool isNpcDashing,
            int npcAttackSequence,
            in TargetState firstTarget,
            in TargetState secondTarget,
            in TargetState thirdTarget)
        {
            IsValid = isValid;
            NpcPosition = npcPosition;
            NpcRotation = npcRotation;
            NpcMode = npcMode;
            IsNpcInvulnerable = isNpcInvulnerable;
            NpcMoveDirection = npcMoveDirection;
            NpcAimDirection = npcAimDirection;
            IsNpcDashing = isNpcDashing;
            NpcAttackSequence = npcAttackSequence;
            FirstTarget = firstTarget;
            SecondTarget = secondTarget;
            ThirdTarget = thirdTarget;
        }
    }
}
