using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>전투원 식별자를 개인전/팀전의 공통 진영 식별자로 해석하는 경계.</summary>
    public interface ICombatSideResolver
    {
        int ResolveSideId(CombatantId combatantId);
    }

    /// <summary>개인전에서는 각 전투원이 자기 식별자를 유일한 진영으로 사용한다.</summary>
    public sealed class IndividualCombatSideResolver : ICombatSideResolver
    {
        private IndividualCombatSideResolver() { }

        public static IndividualCombatSideResolver Instance { get; } = new IndividualCombatSideResolver();

        public int ResolveSideId(CombatantId combatantId) => combatantId.Value;
    }

    /// <summary>
    /// 개인전과 팀전이 공유하는 전투 진영 규칙. 개인전은 전투원마다 서로 다른 SideId를,
    /// 팀전은 같은 팀원끼리 같은 SideId를 사용한다.
    /// </summary>
    public static class CombatSideRules
    {
        public readonly struct MemberStatus
        {
            public MemberStatus(int sideId, bool isAlive)
            {
                SideId = sideId;
                IsAlive = isAlive;
            }

            public int SideId { get; }
            public bool IsAlive { get; }
        }

        /// <summary>
        /// 처음부터 대립 진영이 둘 이상이었고 현재 생존 진영이 하나 이하라면 전투를 끝낸다.
        /// 동시 빈사로 생존 진영이 0이 된 경우도 제한 시간까지 멈추지 않도록 즉시 종료한다.
        /// </summary>
        public static bool ShouldEndCombat(IReadOnlyList<MemberStatus> members)
        {
            if (members == null || members.Count == 0)
            {
                return false;
            }

            int configuredSideCount = 0;
            int survivingSideCount = 0;
            for (int i = 0; i < members.Count; i++)
            {
                MemberStatus member = members[i];
                if (!HasEarlierSide(members, i, member.SideId, aliveOnly: false))
                {
                    configuredSideCount++;
                }
                if (member.IsAlive
                    && !HasEarlierSide(members, i, member.SideId, aliveOnly: true))
                {
                    survivingSideCount++;
                }
            }

            return configuredSideCount > 1 && survivingSideCount <= 1;
        }

        /// <summary>공격자 개인 또는 공격자와 같은 팀만 진영 전용 피드백을 볼 수 있다.</summary>
        public static bool CanViewSideScopedFeedback(int viewerSideId, int sourceSideId) =>
            viewerSideId == sourceSideId;

        private static bool HasEarlierSide(
            IReadOnlyList<MemberStatus> members,
            int exclusiveEnd,
            int sideId,
            bool aliveOnly)
        {
            for (int i = 0; i < exclusiveEnd; i++)
            {
                MemberStatus earlier = members[i];
                if (earlier.SideId == sideId && (!aliveOnly || earlier.IsAlive))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
