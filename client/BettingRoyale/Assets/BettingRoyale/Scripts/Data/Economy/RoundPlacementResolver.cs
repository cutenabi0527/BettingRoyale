using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Economy
{
    /// <summary>
    /// 한 라운드 순위를 정하는 데 필요한 <b>한 참가자의 원천 사실</b> (S-GD-7G8ZTA).
    ///
    /// <para>
    /// 전투 도메인의 <c>Data.Combat.CombatantRoundResult</c> 와 모양이 비슷하지만 <b>일부러 따로 둔다</b> —
    /// 경제 도메인은 전투 도메인을 참조하지 않는다(D-044). 어댑터가 경계에서 옮긴다.
    /// </para>
    /// </summary>
    public readonly struct RoundCombatFacts
    {
        /// <summary><c>PlayerSlot.PlayerId</c> 와 같은 값. <b>순위 동점을 가르는 데 쓰지 않는다.</b></summary>
        public readonly int PlayerId;

        /// <summary>
        /// 대기실 입장 순서. 1부터 시작하고 매치 안에서 유일하다(S-GD-7G8ZTA / S-GD-MQJPFK).
        /// <b>마지막 비교 단계이며 반드시 순위를 가른다.</b> 미확정(0)은 실패로 처리한다 —
        /// PlayerId 로 대신 가르는 것은 승인되지 않은 규칙이다.
        /// </summary>
        public readonly int LobbyJoinOrder;

        /// <summary>라운드를 살아서 끝냈는가. 생존자는 모든 탈락자보다 앞선다.</summary>
        public readonly bool Survived;

        /// <summary>
        /// 빈사가 확정된 Host 권위 시뮬레이션 틱. 생존자는 null, 탈락자는 반드시 값이 있어야 한다.
        /// <b>늦은 틱이 앞선다.</b> 벽시계·이벤트 도착 순서·DB 순서를 쓰지 않는다.
        /// </summary>
        public readonly int? EliminationTick;

        public readonly int Kills;
        public readonly int Assists;

        /// <summary>적의 체력을 실제로 깎은 양. 초과·환경·자해·아군·허수아비 피해는 빠져 있다.</summary>
        public readonly float EnemyDamageDealt;

        /// <summary>
        /// 중도 이탈 등으로 <b>점수는 꼴등 배점</b>을 받아야 하는가(S-GD-QJA6NY).
        /// 표시 순위의 유일성과는 별개다 — 이탈자도 유일한 Placement 를 받는다.
        /// </summary>
        public readonly bool Forfeited;

        public RoundCombatFacts(
            int playerId,
            int lobbyJoinOrder,
            bool survived,
            int? eliminationTick,
            int kills,
            int assists,
            float enemyDamageDealt,
            bool forfeited = false)
        {
            PlayerId = playerId;
            LobbyJoinOrder = lobbyJoinOrder;
            Survived = survived;
            EliminationTick = eliminationTick;
            Kills = kills;
            Assists = assists;
            EnemyDamageDealt = enemyDamageDealt;
            Forfeited = forfeited;
        }

        public override string ToString() =>
            $"P{PlayerId} join{LobbyJoinOrder} " +
            (Survived ? "생존" : $"tick{EliminationTick}") +
            $" {Kills}K {Assists}A dmg{EnemyDamageDealt}";
    }

    /// <summary>한 참가자의 라운드 표시 순위. 1등이 1이고 <b>공동 순위·건너뛰기가 없다</b>.</summary>
    public readonly struct RoundPlacementEntry
    {
        public readonly int Placement;
        public readonly int PlayerId;

        /// <summary>표시 순위와 무관하게 <b>점수는 꼴등</b>으로 준다(S-GD-QJA6NY).</summary>
        public readonly bool ScoredAsLastPlace;

        internal RoundPlacementEntry(int placement, int playerId, bool scoredAsLastPlace)
        {
            Placement = placement;
            PlayerId = playerId;
            ScoredAsLastPlace = scoredAsLastPlace;
        }

        public override string ToString() =>
            $"{Placement}등 P{PlayerId}{(ScoredAsLastPlace ? " (꼴등 배점)" : string.Empty)}";
    }

    /// <summary>순위를 낼 수 없는 이유. <b>조용히 임의 순위를 만들지 않는다.</b></summary>
    public enum RoundPlacementFailure
    {
        None = 0,

        /// <summary>사실 목록이 null 이다.</summary>
        MissingFacts,

        /// <summary>매치 시작 인원(N)이 유효하지 않다.</summary>
        InvalidRosterSize,

        /// <summary>사실 수가 매치 시작 인원과 다르다. 현재 접속자 수로 대신하지 않는다.</summary>
        RosterMismatch,

        /// <summary>같은 PlayerId 가 두 번 들어왔다.</summary>
        DuplicatePlayer,

        /// <summary>대기실 입장 순서가 미확정(0 이하)이다.</summary>
        MissingJoinOrder,

        /// <summary>대기실 입장 순서가 겹친다. 유일하지 않으면 마지막 단계가 순위를 가르지 못한다.</summary>
        DuplicateJoinOrder,

        /// <summary>탈락자에 권위 틱이 없거나 생존자에 틱이 붙어 있거나 틱이 음수다.</summary>
        InvalidEliminationTick,

        /// <summary>킬·어시스트·실제 피해가 음수이거나 유한하지 않다.</summary>
        InvalidCombatStats,
    }

    /// <summary>
    /// 라운드 표시 순위를 정한다 (S-GD-7G8ZTA). <b>순수 도메인이며 <c>UnityEngine</c> 을 참조하지 않는다</b>(D-060).
    ///
    /// <list type="number">
    /// <item>생존자가 탈락자보다 앞선다.</item>
    /// <item>늦은 Host 권위 틱에 탈락한 사람이 앞선다.</item>
    /// <item>같은 틱이면 킬 → 어시스트 → 실제 적 피해 → 대기실 입장 순서.</item>
    /// </list>
    ///
    /// <para>
    /// 입력은 <b>같은 권위 틱의 모든 이벤트를 반영한 최종 스냅샷</b>이어야 한다. 이벤트 도착 순서를
    /// 바꿔도 최종 사실이 같으면 결과가 같다 — 정렬 기준에 도착 순서·PlayerId·Dictionary 순회가 없다.
    /// </para>
    /// </summary>
    public static class RoundPlacementResolver
    {
        /// <summary>
        /// 순위를 계산한다. 반환 배열은 1등부터 정렬돼 있고 Placement 가 1..N 으로 유일하다.
        /// </summary>
        /// <param name="facts">매치 <b>시작</b> 인원 전원의 사실. 이탈자도 포함한다.</param>
        /// <param name="rosterSize">매치 시작 인원 N. 현재 접속자 수가 아니다(S-GD-QJA6NY).</param>
        public static bool TryResolve(
            IReadOnlyList<RoundCombatFacts> facts,
            int rosterSize,
            out RoundPlacementEntry[] placements,
            out RoundPlacementFailure failure)
        {
            placements = Array.Empty<RoundPlacementEntry>();

            if (facts == null)
            {
                failure = RoundPlacementFailure.MissingFacts;
                return false;
            }

            if (rosterSize <= 0)
            {
                failure = RoundPlacementFailure.InvalidRosterSize;
                return false;
            }

            if (facts.Count != rosterSize)
            {
                failure = RoundPlacementFailure.RosterMismatch;
                return false;
            }

            var seenPlayers = new HashSet<int>();
            var seenJoinOrders = new HashSet<int>();
            for (var i = 0; i < facts.Count; i++)
            {
                RoundCombatFacts fact = facts[i];

                if (!seenPlayers.Add(fact.PlayerId))
                {
                    failure = RoundPlacementFailure.DuplicatePlayer;
                    return false;
                }

                if (fact.LobbyJoinOrder <= PlayerMatchFacts.UnassignedJoinOrder)
                {
                    failure = RoundPlacementFailure.MissingJoinOrder;
                    return false;
                }

                if (!seenJoinOrders.Add(fact.LobbyJoinOrder))
                {
                    failure = RoundPlacementFailure.DuplicateJoinOrder;
                    return false;
                }

                if (fact.Survived
                    ? fact.EliminationTick.HasValue
                    : !fact.EliminationTick.HasValue || fact.EliminationTick.Value < 0)
                {
                    failure = RoundPlacementFailure.InvalidEliminationTick;
                    return false;
                }

                if (fact.Kills < 0
                    || fact.Assists < 0
                    || fact.EnemyDamageDealt < 0f
                    || float.IsNaN(fact.EnemyDamageDealt)
                    || float.IsInfinity(fact.EnemyDamageDealt))
                {
                    failure = RoundPlacementFailure.InvalidCombatStats;
                    return false;
                }
            }

            var sorted = new RoundCombatFacts[facts.Count];
            for (var i = 0; i < facts.Count; i++)
            {
                sorted[i] = facts[i];
            }

            Array.Sort(sorted, Compare);

            var result = new RoundPlacementEntry[sorted.Length];
            for (var i = 0; i < sorted.Length; i++)
            {
                result[i] = new RoundPlacementEntry(i + 1, sorted[i].PlayerId, sorted[i].Forfeited);
            }

            placements = result;
            failure = RoundPlacementFailure.None;
            return true;
        }

        /// <summary>실패하면 던진다. 호출자가 실패를 삼키지 못하게 하는 엄격 경로다.</summary>
        public static RoundPlacementEntry[] Resolve(IReadOnlyList<RoundCombatFacts> facts, int rosterSize)
        {
            if (TryResolve(facts, rosterSize, out RoundPlacementEntry[] placements, out RoundPlacementFailure failure))
            {
                return placements;
            }

            throw new ArgumentException(
                $"라운드 순위를 계산할 수 없습니다: {failure} (S-GD-7G8ZTA).", nameof(facts));
        }

        /// <summary>이 참가자가 앞서면 음수. 마지막 단계인 입장 순서에서 반드시 갈린다.</summary>
        private static int Compare(RoundCombatFacts left, RoundCombatFacts right)
        {
            if (left.Survived != right.Survived)
            {
                // 살아남은 참가자는 탈락자보다 앞선다.
                return left.Survived ? -1 : 1;
            }

            if (!left.Survived)
            {
                // 늦은 틱에 탈락한 사람이 앞선다. 다른 틱이면 전투 통계보다 탈락 시점이 우선한다.
                var byTick = right.EliminationTick.Value.CompareTo(left.EliminationTick.Value);
                if (byTick != 0)
                {
                    return byTick;
                }
            }

            var byKills = right.Kills.CompareTo(left.Kills);
            if (byKills != 0)
            {
                return byKills;
            }

            var byAssists = right.Assists.CompareTo(left.Assists);
            if (byAssists != 0)
            {
                return byAssists;
            }

            var byDamage = right.EnemyDamageDealt.CompareTo(left.EnemyDamageDealt);
            if (byDamage != 0)
            {
                return byDamage;
            }

            // 대기실에 먼저 들어온 사람이 앞선다. 입장 순서는 유일하므로 여기서 반드시 갈린다.
            return left.LobbyJoinOrder.CompareTo(right.LobbyJoinOrder);
        }
    }
}
