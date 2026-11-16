using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Economy
{
    /// <summary>
    /// 라운드 정산에 넣는 한 참가자의 사실. 전투 경계가 옮겨 담는다 —
    /// 경제 도메인은 전투 도메인을 참조하지 않는다 (D-044).
    /// </summary>
    public readonly struct RoundSettlementInput
    {
        public readonly int PlayerId;
        public readonly int LobbyJoinOrder;
        public readonly bool Survived;
        public readonly int? EliminationTick;
        public readonly int Kills;
        public readonly int Assists;
        public readonly int RangedAssists;
        public readonly float EnemyDamageDealt;

        /// <summary>중도 이탈 등으로 <b>배점만 꼴등</b>인가 (S-GD-QJA6NY).</summary>
        public readonly bool Forfeited;

        public RoundSettlementInput(
            int playerId,
            int lobbyJoinOrder,
            bool survived,
            int? eliminationTick,
            int kills,
            int assists,
            int rangedAssists,
            float enemyDamageDealt,
            bool forfeited = false)
        {
            PlayerId = playerId;
            LobbyJoinOrder = lobbyJoinOrder;
            Survived = survived;
            EliminationTick = eliminationTick;
            Kills = kills;
            Assists = assists;
            RangedAssists = rangedAssists;
            EnemyDamageDealt = enemyDamageDealt;
            Forfeited = forfeited;
        }
    }

    /// <summary>정산 계획을 세울 수 없는 이유. <b>조용히 부분 반영하지 않는다.</b></summary>
    public enum RoundSettlementFailure
    {
        None = 0,

        /// <summary>라운드 번호가 1 미만이다.</summary>
        InvalidRound,

        /// <summary>입력이 null 이다.</summary>
        MissingInputs,

        /// <summary>어시스트 분류가 총합을 넘는 등 전투 통계가 계약을 어겼다.</summary>
        InvalidCombatStats,

        /// <summary>순위를 유일하게 정하지 못했다. 자세한 사유는 <see cref="RoundPlacementFailure"/> 다.</summary>
        PlacementUnresolved,
    }

    /// <summary>
    /// 라운드 원천 사실을 <b>유일한 표시 순위가 붙은</b> <see cref="RoundOutcome"/> 목록으로 바꾼다
    /// (S-GD-7G8ZTA · S-GD-QJA6NY).
    ///
    /// <para>
    /// <b>여기서 실패하면 아무것도 반영하지 않는다.</b> 순위를 못 정한 채 집계만 올리면
    /// 그 라운드는 영원히 꼴등 배점으로 굳고(<see cref="RoundOutcome.UnknownPlacement"/>),
    /// 중복 방지 기록만 소비되어 재시도도 막힌다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class RoundSettlementPlanner
    {
        /// <param name="rosterSize">매치 <b>시작</b> 인원 N. 현재 접속자 수가 아니다.</param>
        public static bool TryBuild(
            int round,
            IReadOnlyList<RoundSettlementInput> inputs,
            int rosterSize,
            out RoundOutcome[] outcomes,
            out RoundSettlementFailure failure,
            out RoundPlacementFailure placementFailure)
        {
            outcomes = Array.Empty<RoundOutcome>();
            placementFailure = RoundPlacementFailure.None;

            if (round <= 0)
            {
                failure = RoundSettlementFailure.InvalidRound;
                return false;
            }

            if (inputs == null)
            {
                failure = RoundSettlementFailure.MissingInputs;
                return false;
            }

            var facts = new RoundCombatFacts[inputs.Count];
            for (var i = 0; i < inputs.Count; i++)
            {
                RoundSettlementInput input = inputs[i];
                if (input.Kills < 0
                    || input.Assists < 0
                    || input.RangedAssists < 0
                    || input.RangedAssists > input.Assists)
                {
                    failure = RoundSettlementFailure.InvalidCombatStats;
                    return false;
                }

                facts[i] = new RoundCombatFacts(
                    input.PlayerId,
                    input.LobbyJoinOrder,
                    input.Survived,
                    input.EliminationTick,
                    input.Kills,
                    input.Assists,
                    input.EnemyDamageDealt,
                    input.Forfeited);
            }

            if (!RoundPlacementResolver.TryResolve(
                    facts, rosterSize, out RoundPlacementEntry[] placements, out placementFailure))
            {
                failure = RoundSettlementFailure.PlacementUnresolved;
                return false;
            }

            var placementByPlayer = new Dictionary<int, int>(placements.Length);
            for (var i = 0; i < placements.Length; i++)
            {
                placementByPlayer[placements[i].PlayerId] = placements[i].Placement;
            }

            var built = new RoundOutcome[inputs.Count];
            for (var i = 0; i < inputs.Count; i++)
            {
                RoundSettlementInput input = inputs[i];
                built[i] = new RoundOutcome(
                    input.PlayerId,
                    input.Kills,
                    input.Assists,
                    input.RangedAssists,
                    input.Survived,
                    placementByPlayer[input.PlayerId],
                    input.Forfeited);
            }

            outcomes = built;
            failure = RoundSettlementFailure.None;
            return true;
        }
    }
}
