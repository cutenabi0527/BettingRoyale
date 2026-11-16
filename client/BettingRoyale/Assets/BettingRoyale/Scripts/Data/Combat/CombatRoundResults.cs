using System;
using BettingRoyale.Core;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 정산이 소비하는 한 참여자의 라운드 전투 사실(S-GD-JRKHQA).
    /// 점수와 골드 보상은 경제·정산 소유자가 별도로 계산한다.
    /// </summary>
    public readonly struct CombatantRoundResult
    {
        public readonly CombatantId Combatant;
        public readonly int Kills;
        public readonly int Assists;
        public readonly bool Survived;
        public readonly bool AttributionPending;

        /// <summary>
        /// 라운드 안에서 이 참여자가 몇 번째로 빈사했는지의 원시 사실(O-025 합의).
        /// Host가 첫 유효 빈사마다 1부터 단조 증가하는 값을 부여하며, 관측된 빈사가 없으면 <c>null</c> 이다.
        /// 순위·점수·동시 탈락 판정은 여기서 하지 않는다 — 경제·정산 소유자가 계산한다(S-GD-P8V682).
        /// </summary>
        public readonly int? EliminationOrder;

        /// <summary>
        /// 빈사가 확정된 Host 권위 시뮬레이션 틱 번호(S-GD-7G8ZTA). 생존자와 틱을 모르는 옛 경계는 <c>null</c> 이다.
        /// <b>순위는 이 값으로만 시점을 비교한다</b> — 벽시계·도착 순서·DB 순서를 쓰지 않는다.
        /// 같은 틱의 여러 빈사는 정산 경계가 킬·어시스트·실제 적 피해·입장 순서로 가른다.
        /// </summary>
        public readonly int? EliminationTick;

        /// <summary>
        /// 이 라운드에 <b>적의 체력을 실제로 깎은 양</b>의 합(S-GD-7G8ZTA).
        /// <c>DamageApplied.Amount</c> 가 이미 <c>min(남은 체력, 요청 피해)</c> 라 초과 피해는 들어오지 않는다.
        /// 환경·자해·아군·허수아비 예상 피해와 <b>받은 피해</b>는 포함하지 않는다.
        /// </summary>
        public readonly float EnemyDamageDealt;

        public CombatantRoundResult(
            CombatantId combatant,
            int kills,
            int assists,
            bool survived,
            bool attributionPending = false,
            int? eliminationOrder = null,
            int? eliminationTick = null,
            float enemyDamageDealt = 0f)
        {
            if (kills < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(kills));
            }
            if (assists < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(assists));
            }
            if (eliminationOrder.HasValue && eliminationOrder.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(eliminationOrder),
                    "Elimination order must be a positive sequence number when supplied.");
            }
            if (survived && eliminationOrder.HasValue)
            {
                throw new ArgumentException(
                    "A survivor cannot carry an elimination order.",
                    nameof(eliminationOrder));
            }
            if (survived && eliminationTick.HasValue)
            {
                throw new ArgumentException(
                    "A survivor cannot carry an elimination tick.",
                    nameof(eliminationTick));
            }
            if (eliminationTick.HasValue && eliminationTick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(eliminationTick),
                    "Authority simulation tick cannot be negative.");
            }
            if (enemyDamageDealt < 0f
                || float.IsNaN(enemyDamageDealt)
                || float.IsInfinity(enemyDamageDealt))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(enemyDamageDealt),
                    "Actual enemy damage must be a finite non-negative value.");
            }

            Combatant = combatant;
            Kills = kills;
            Assists = assists;
            Survived = survived;
            AttributionPending = attributionPending;
            EliminationOrder = eliminationOrder;
            EliminationTick = eliminationTick;
            EnemyDamageDealt = enemyDamageDealt;
        }
    }

    /// <summary>
    /// Host가 한 라운드의 킬·어시스트·생존 사실을 확정해 정산 경계로 전달한다(S-GD-JRKHQA).
    /// 전체 스냅샷을 한 번에 보내며, 배열은 외부 변경이 내부 상태로 새지 않도록 복사한다.
    /// </summary>
    public readonly struct CombatRoundResultsReady : IGameEvent
    {
        private readonly CombatantRoundResult[] results;

        public readonly int Round;

        /// <summary>
        /// 이 스냅샷을 확정한 Host 권위 틱(S-GD-7G8ZTA). 틱을 모르는 옛 경계는 <c>null</c> 이다.
        /// 같은 틱의 모든 이벤트를 반영한 뒤에만 채워진다 — 부분 배치 정산의 근거로 쓰지 않는다.
        /// </summary>
        public readonly int? SettledTick;

        public int ResultCount => results?.Length ?? 0;

        public CombatRoundResultsReady(int round, CombatantRoundResult[] results)
            : this(round, results, null)
        {
        }

        public CombatRoundResultsReady(int round, CombatantRoundResult[] results, int? settledTick)
        {
            if (round <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(round));
            }
            if (settledTick.HasValue && settledTick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settledTick));
            }

            Round = round;
            SettledTick = settledTick;
            this.results = results == null
                ? throw new ArgumentNullException(nameof(results))
                : (CombatantRoundResult[])results.Clone();
        }

        public bool TryGetResult(CombatantId combatant, out CombatantRoundResult result)
        {
            if (results != null)
            {
                for (int i = 0; i < results.Length; i++)
                {
                    if (results[i].Combatant == combatant)
                    {
                        result = results[i];
                        return true;
                    }
                }
            }

            result = default;
            return false;
        }

        public CombatantRoundResult[] CopyResults() => results == null
            ? Array.Empty<CombatantRoundResult>()
            : (CombatantRoundResult[])results.Clone();
    }
}
