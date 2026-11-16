using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 라운드 원천 사실(킬·어시스트·실제 적 피해·빈사)을 <b>권위 틱 배치 단위로</b> 모으는 정본.
    /// <c>UnityEngine</c> 을 참조하지 않는 순수 도메인이라 씬 없이 결정적으로 검증한다(D-060).
    ///
    /// <para>
    /// 확정 스펙: 순위 원천 사실은 S-GD-7G8ZTA(권위 틱·실제 적 피해), 자기장 빈사 귀속은
    /// S-GD-87YH9R(10초 창의 마지막 유효 공격자가 킬, 나머지 후보가 어시스트)다.
    /// <b>여기서 Placement·점수를 계산하지 않는다</b> — 경제 경계의
    /// <c>Data/Economy/RoundPlacementResolver</c> 와 <c>RoundSettlementPlanner</c> 가 계산한다.
    /// </para>
    ///
    /// <para>
    /// Gameplay 의 <c>CombatRoundResultState</c> 는 이 원장을 감싸 <c>DamageApplied</c>·
    /// <c>CombatantDowned</c> 이벤트를 받는 어댑터다 — 판정 코드는 여기 한 벌뿐이다.
    /// </para>
    /// </summary>
    public sealed class CombatRoundFactLedger : ICombatRoundFactRecorder
    {
        private sealed class MutableResult
        {
            public int Kills { get; set; }
            public int Assists { get; set; }
            public bool IsDowned { get; set; }
            public bool AttributionPending { get; set; }

            /// <summary>첫 유효 빈사에만 부여되는 라운드 내 탈락 순번(O-025). 생존자는 null 이다.</summary>
            public int? EliminationOrder { get; set; }

            /// <summary>첫 유효 빈사가 확정된 Host 권위 틱(S-GD-7G8ZTA). 틱을 모르면 null 이다.</summary>
            public int? EliminationTick { get; set; }

            /// <summary>적 체력을 실제로 깎은 누계(S-GD-7G8ZTA). float 합산 오차를 줄이려 double로 센다.</summary>
            public double EnemyDamageDealt { get; set; }
        }

        /// <summary>한 대상에게 들어간 마지막 유효 적 피해의 시각과 관측 순번.</summary>
        private struct Contribution
        {
            public CombatantId Attacker;
            public double OccurredAt;

            /// <summary>같은 시각 동점을 가르는 관측 순번. 늦게 기록된 쪽이 크다(S-GD-87YH9R).</summary>
            public long Sequence;
        }

        private readonly double assistWindowSeconds;
        private readonly Func<CombatantId, CombatantId, bool> isEnemy;
        private readonly List<CombatantId> participantOrder = new();
        private readonly Dictionary<CombatantId, MutableResult> results = new();

        // 순위에 쓰이는 판정은 Dictionary 순회에 기대지 않는다. 대상별 기여는 관측 순서를 지키는 List다.
        private readonly Dictionary<CombatantId, List<Contribution>> contributions = new();

        private int round;
        private bool isActive;
        private bool isSettled;
        private int eliminationSequence;
        private long observationSequence;
        private int? openTick;
        private int? lastClosedTick;
        private int? lastRecordedTick;
        private bool strictBatchMode;

        public CombatRoundFactLedger(double assistWindowSeconds)
            : this(assistWindowSeconds, null)
        {
        }

        /// <param name="assistWindowSeconds">어시스트·자기장 귀속 창(S-GD-NMFBE9의 확정 10초). 경계 포함이다.</param>
        /// <param name="enemyFilter">
        /// (공격자, 대상)이 <b>적대 관계</b>인지 판정한다. null이면 자기 자신만 제외한다.
        /// 아군 피해·준비 허수아비·미적용 피해를 어댑터가 여기서 거를 수 있다(S-GD-7G8ZTA).
        /// </param>
        public CombatRoundFactLedger(
            double assistWindowSeconds, Func<CombatantId, CombatantId, bool> enemyFilter)
        {
            if (!IsFinite(assistWindowSeconds) || assistWindowSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(assistWindowSeconds),
                    "Assist window must be a finite positive value.");
            }

            this.assistWindowSeconds = assistWindowSeconds;
            isEnemy = enemyFilter;
        }

        /// <summary>
        /// <b>엄격 배치 모드</b>. 제품 Host 경로가 켠다 — 열린 권위 틱 밖의 사실을 아예 받지 않고,
        /// 이미 닫힌 틱 번호로 들어온 늦은 이벤트도 거절한다(S-GD-7G8ZTA).
        ///
        /// <para>
        /// 기본값은 꺼짐이다. 틱을 모르고 시간만 주는 <b>문서화된 옛 호출자</b>
        /// (CombatLab Direct/Local 경로)를 깨뜨리지 않기 위해서다.
        /// </para>
        /// </summary>
        public bool IsStrictBatchMode => strictBatchMode;

        /// <summary>엄격 배치 모드를 켠다. 라운드 도중에는 바꾸지 않는다.</summary>
        public void EnableStrictBatchMode() => strictBatchMode = true;

        /// <summary>권위 틱이 열려 있는 동안이다. 열려 있으면 정산할 수 없다(S-GD-7G8ZTA).</summary>
        public bool IsTickOpen => openTick.HasValue;

        /// <summary>열려 있는 권위 틱. 없으면 null 이다.</summary>
        public int? OpenTick => openTick;

        /// <summary>마지막으로 <b>전부</b> 반영이 끝난 권위 틱. 정산·승패 판정은 이 뒤에만 한다.</summary>
        public int? LastCompletedTick => lastClosedTick;

        /// <summary>지금 정산해도 되는가. 틱 처리 도중에는 false 다 — 조기 정산을 막는다.</summary>
        public bool CanFinalize => isActive && !isSettled && !openTick.HasValue;

        public void BeginRound(int roundNumber, IEnumerable<CombatantId> participants)
        {
            if (roundNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(roundNumber));
            }
            if (participants == null)
            {
                throw new ArgumentNullException(nameof(participants));
            }

            round = roundNumber;
            isActive = true;
            isSettled = false;
            // 탈락 순번은 라운드 안에서만 뜻이 있다. 새 라운드는 1부터 다시 센다.
            eliminationSequence = 0;
            observationSequence = 0;
            // 앞 라운드의 틱 경계가 다음 라운드로 새지 않는다.
            openTick = null;
            lastClosedTick = null;
            lastRecordedTick = null;
            participantOrder.Clear();
            results.Clear();
            contributions.Clear();

            foreach (CombatantId participant in participants)
            {
                RegisterParticipant(participant);
            }
        }

        /// <summary>라운드 도중 늦게 스폰된 참여자를 결과 스냅샷에 포함한다.</summary>
        public bool RegisterParticipant(CombatantId participant)
        {
            if (!isActive || isSettled || results.ContainsKey(participant))
            {
                return false;
            }

            results.Add(participant, new MutableResult());
            participantOrder.Add(participant);
            return true;
        }

        /// <summary>
        /// 한 Host 권위 틱의 처리를 연다. 닫히기 전에는 <see cref="TrySettle(double, out CombatRoundResultsReady)"/>
        /// 가 실패한다 — 자기장 마지막 루프의 첫 치명타에서 우승을 확정하지 않기 위해서다(S-GD-7G8ZTA).
        /// 틱 번호는 단조 증가해야 하며, 과거 틱을 다시 열면 false 를 돌려준다.
        /// </summary>
        public bool BeginAuthorityTick(int tick)
        {
            if (!isActive || isSettled || tick < 0 || openTick.HasValue)
            {
                return false;
            }
            if (lastClosedTick.HasValue && tick <= lastClosedTick.Value)
            {
                return false;
            }

            openTick = tick;
            return true;
        }

        /// <summary>열린 틱의 모든 이벤트 반영을 끝낸다. 이 뒤에야 생존 판정·정산을 할 수 있다.</summary>
        public bool EndAuthorityTick()
        {
            if (!openTick.HasValue)
            {
                return false;
            }

            lastClosedTick = openTick;
            openTick = null;
            return true;
        }

        /// <summary>
        /// 실제로 적용된 적 피해를 기록한다. <paramref name="amount"/> 는 이미
        /// <c>min(남은 체력, 요청 피해)</c> 라 요청 피해를 다시 세지 않는다(S-GD-7G8ZTA).
        /// </summary>
        public bool RecordDamageFact(
            CombatantId attacker,
            CombatantId target,
            WeaponType weapon,
            float amount,
            CombatDamageSource source,
            double occurredAt,
            int? authorityTick)
        {
            if (!IsAcceptableTick(authorityTick))
            {
                return false;
            }

            int? tick = ResolveTick(authorityTick);
            if (!CanRecord(occurredAt)
                || !IsCombatantDamage(source)
                || attacker == target
                || float.IsNaN(amount)
                || float.IsInfinity(amount)
                || amount <= 0f
                || (isEnemy != null && !isEnemy(attacker, target)))
            {
                return false;
            }

            RegisterParticipant(attacker);
            RegisterParticipant(target);
            if (results[target].IsDowned)
            {
                return false;
            }

            lastRecordedTick = tick ?? lastRecordedTick;
            results[attacker].EnemyDamageDealt += amount;
            AddContribution(attacker, target, occurredAt);
            return true;
        }

        /// <summary>
        /// 보호막이 한 대를 전부 받은 타격을 기록한다. 킬·어시스트 후보로만 세고 실제 적 피해량에는 넣지 않는다 —
        /// 체력이 줄지 않았다(S-GD-7G8ZTA). 보호막만 깎아도 어시스트다(사용자 확정 2026-09-18, S-GD-Y534KH).
        /// </summary>
        public bool RecordShieldFact(
            CombatantId attacker,
            CombatantId target,
            CombatDamageSource source,
            double occurredAt,
            int? authorityTick)
        {
            if (!IsAcceptableTick(authorityTick))
            {
                return false;
            }

            int? tick = ResolveTick(authorityTick);
            if (!CanRecord(occurredAt)
                || !IsCombatantDamage(source)
                || attacker == target
                || (isEnemy != null && !isEnemy(attacker, target)))
            {
                return false;
            }

            RegisterParticipant(attacker);
            RegisterParticipant(target);
            if (results[target].IsDowned)
            {
                return false;
            }

            lastRecordedTick = tick ?? lastRecordedTick;
            AddContribution(attacker, target, occurredAt);
            return true;
        }

        /// <summary>
        /// 사람이 준 피해인가 — 칼·화살·투척물(<see cref="CombatDamageSource.Weapon"/>), 축복 폭발
        /// (<see cref="CombatDamageSource.Blessing"/>), 독(<see cref="CombatDamageSource.Dot"/>).
        /// 자기장만 빠진다. 축복이 보는 처치·어시스트와 같은 기준이다(사용자 확정 2026-09-18, S-GD-Y534KH).
        /// </summary>
        private static bool IsCombatantDamage(CombatDamageSource source) =>
            source == CombatDamageSource.Weapon
            || source == CombatDamageSource.Blessing
            || source == CombatDamageSource.Dot;

        private void AddContribution(CombatantId attacker, CombatantId target, double occurredAt)
        {
            if (!contributions.TryGetValue(target, out List<Contribution> targetContributions))
            {
                targetContributions = new List<Contribution>();
                contributions.Add(target, targetContributions);
            }

            observationSequence++;
            var entry = new Contribution
            {
                Attacker = attacker,
                OccurredAt = occurredAt,
                Sequence = observationSequence,
            };

            // 같은 공격자의 여러 타격은 마지막 유효 타격만 남긴다. 어시스트는 1회다.
            for (int i = 0; i < targetContributions.Count; i++)
            {
                if (targetContributions[i].Attacker == attacker)
                {
                    targetContributions[i] = entry;
                    return;
                }
            }

            targetContributions.Add(entry);
        }

        /// <summary>빈사 하나를 기록한다. 자기장 빈사도 이 경로로 들어온다(S-GD-87YH9R).</summary>
        public bool RecordDownedFact(
            CombatantId combatant,
            CombatantId attacker,
            CombatDamageSource source,
            double occurredAt,
            int? authorityTick)
        {
            if (!IsAcceptableTick(authorityTick))
            {
                return false;
            }

            int? tick = ResolveTick(authorityTick);
            if (!CanRecord(occurredAt))
            {
                return false;
            }

            RegisterParticipant(combatant);
            // 축복 폭발·독도 사람이 준 피해다. 다만 신용 한도의 미룬 몫처럼 공격자가 없는(음수·0) 축복 피해는
            // 명부에 없는 사람을 만들지 않는다 — 아래에서 자기장처럼 귀속한다.
            bool noAttacker = source != CombatDamageSource.Weapon && attacker.Value <= 0;
            if (IsCombatantDamage(source) && attacker != combatant && !noAttacker)
            {
                RegisterParticipant(attacker);
            }

            MutableResult targetResult = results[combatant];
            if (targetResult.IsDowned)
            {
                // 중복 빈사는 순번·틱·집계를 바꾸지 않는다(S-GD-7G8ZTA).
                return false;
            }

            targetResult.IsDowned = true;
            // 첫 유효 빈사에만 순번을 부여한다(O-025). 중복 빈사는 위에서 걸러지므로 번호를 소비하지 않는다.
            // 원인(무기·환경)과 무관한 원시 사실이므로 아래 환경 빈사 분기보다 먼저 기록한다.
            eliminationSequence++;
            targetResult.EliminationOrder = eliminationSequence;
            targetResult.EliminationTick = tick;
            lastRecordedTick = tick ?? lastRecordedTick;

            if (source == CombatDamageSource.Zone || (IsCombatantDamage(source) && noAttacker))
            {
                // S-GD-87YH9R: 10초 창 안의 마지막 유효 적 공격자가 킬, 나머지 후보가 어시스트.
                // 후보가 없으면 순수 환경사이며 귀속 미결이 아니다. 공격자가 없는 축복 피해(신용 한도의 미룬 몫)도
                // 같은 방식이다(사용자 확정 2026-09-18, S-GD-Y534KH).
                ResolveZoneAttribution(combatant, occurredAt);
                return true;
            }

            if (!IsCombatantDamage(source))
            {
                // 낙사 등 아직 확정되지 않은 환경 빈사는 귀속 미결로 남긴다(S-GD-JRKHQA).
                targetResult.AttributionPending = true;
                return true;
            }

            MutableResult killerResult = null;
            // 적 판정을 통과하지 못한 공격자에게는 킬을 주지 않는다(S-GD-7G8ZTA) —
            // 아군 피해·허수아비·명부 밖 전투원은 실제 피해 집계에서도 이미 빠져 있다.
            // 그 경우 원인 불명 빈사로 두지 않고 10초 창의 유효 기여자에게 어시스트만 준다.
            bool hasKiller = attacker != combatant
                && (isEnemy == null || isEnemy(attacker, combatant))
                && results.TryGetValue(attacker, out killerResult);
            if (hasKiller)
            {
                killerResult.Kills++;
            }

            AwardAssists(combatant, occurredAt, hasKiller ? attacker : (CombatantId?)null);
            return true;
        }

        /// <summary>
        /// 자기장 빈사의 킬·어시스트를 정한다(S-GD-87YH9R).
        /// 후보는 사망 시점 기준 10초 <b>이내(경계 포함)</b> 에 실제 피해를 준 적 전원이고,
        /// 킬은 그중 가장 최근에 때린 사람이다. 같은 시각이면 늦게 기록된 쪽이 킬을 가져간다.
        /// 자기장 피해는 애초에 후보로 기록되지 않으므로 마지막 타격자 판정에 끼어들지 않는다.
        /// 마지막 타격자가 이미 빈사 상태여도 킬을 가져간다.
        /// </summary>
        private void ResolveZoneAttribution(CombatantId victim, double occurredAt)
        {
            if (!contributions.TryGetValue(victim, out List<Contribution> candidates))
            {
                return;
            }

            bool hasKiller = false;
            Contribution best = default;
            for (int i = 0; i < candidates.Count; i++)
            {
                Contribution candidate = candidates[i];
                if (!IsWithinWindow(candidate.OccurredAt, occurredAt)
                    || candidate.Attacker == victim
                    || !results.ContainsKey(candidate.Attacker))
                {
                    continue;
                }

                if (!hasKiller
                    || candidate.OccurredAt > best.OccurredAt
                    || (candidate.OccurredAt == best.OccurredAt && candidate.Sequence > best.Sequence))
                {
                    hasKiller = true;
                    best = candidate;
                }
            }

            if (!hasKiller)
            {
                // 10초 안에 아무도 안 때렸다 — 킬도 어시스트도 없는 순수 환경사다. 미결로 두지 않는다.
                return;
            }

            results[best.Attacker].Kills++;
            AwardAssists(victim, occurredAt, best.Attacker);
        }

        private void AwardAssists(CombatantId victim, double occurredAt, CombatantId? killer)
        {
            if (!contributions.TryGetValue(victim, out List<Contribution> candidates))
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Contribution candidate = candidates[i];
                if (candidate.Attacker == victim
                    || (killer.HasValue && candidate.Attacker == killer.Value)
                    || !IsWithinWindow(candidate.OccurredAt, occurredAt)
                    || !results.TryGetValue(candidate.Attacker, out MutableResult helperResult))
                {
                    continue;
                }

                helperResult.Assists++;
            }
        }

        /// <summary>정확히 창 경계의 피해도 포함한다(S-GD-NMFBE9 확정 10초).</summary>
        private bool IsWithinWindow(double contributedAt, double downedAt)
        {
            double age = downedAt - contributedAt;
            return age >= 0d && age <= assistWindowSeconds;
        }

        public bool TrySettle(double occurredAt, out CombatRoundResultsReady settled)
        {
            if (!CanRecord(occurredAt) || openTick.HasValue)
            {
                // 틱 처리 중 조기 정산을 막는다(S-GD-7G8ZTA).
                settled = default;
                return false;
            }

            isSettled = true;
            participantOrder.Sort((left, right) => left.Value.CompareTo(right.Value));
            settled = new CombatRoundResultsReady(
                round, BuildResultSnapshot(), lastClosedTick ?? lastRecordedTick);
            return true;
        }

        /// <summary>
        /// 아직 봉인하지 않은 <b>지금까지의</b> 사실 사본. 중간 순위표 표시 전용이다(S15P21A606-170).
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>정산이 아니다.</b> <see cref="isSettled"/> 를 세우지 않고 참여자 순서도 바꾸지 않는다 —
        /// 읽기만 하는 호출이라 다음 <see cref="TrySettle(double, out CombatRoundResultsReady)"/> 의
        /// 결과가 이 호출 때문에 달라지지 않는다. 정산 경계는 여전히 하나뿐이다(S-GD-JRKHQA).
        /// </para>
        /// <para>
        /// 봉인 뒤에도 같은 사실을 그대로 비춘다 — 봉인된 라운드는 더 이상 사실을 받지 않으므로
        /// 봉인 스냅샷과 같은 값이다.
        /// </para>
        /// </remarks>
        public bool TryCopyLiveFacts(out CombatantRoundResult[] facts)
        {
            if (!isActive)
            {
                facts = Array.Empty<CombatantRoundResult>();
                return false;
            }

            facts = BuildResultSnapshot();
            return true;
        }

        /// <summary>
        /// 지금까지의 집계를 전투원 식별자 오름차순으로 옮긴다. 정산·중간 표시가 <b>같은 코드</b>를
        /// 쓰므로 둘의 사실이 갈라질 수 없다.
        /// </summary>
        private CombatantRoundResult[] BuildResultSnapshot()
        {
            var ordered = new List<CombatantId>(participantOrder);
            ordered.Sort((left, right) => left.Value.CompareTo(right.Value));
            var snapshot = new CombatantRoundResult[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                CombatantId participant = ordered[i];
                MutableResult result = results[participant];
                snapshot[i] = new CombatantRoundResult(
                    participant,
                    result.Kills,
                    result.Assists,
                    survived: !result.IsDowned,
                    attributionPending: result.AttributionPending,
                    eliminationOrder: result.EliminationOrder,
                    eliminationTick: result.IsDowned ? result.EliminationTick : null,
                    enemyDamageDealt: (float)result.EnemyDamageDealt);
            }

            return snapshot;
        }

        private int? ResolveTick(int? explicitTick) => explicitTick ?? openTick;

        /// <summary>
        /// 이 틱 번호의 사실을 지금 받아도 되는가(S-GD-7G8ZTA).
        ///
        /// <list type="bullet">
        /// <item>음수 틱은 언제나 거절한다.</item>
        /// <item>열린 틱과 다른 번호는 거절한다 — 배치 경계가 깨진다.</item>
        /// <item>
        /// 이미 닫힌 틱 번호로 뒤늦게 들어온 사실은 거절한다. <b>완료된 배치를 바꾸면
        /// 이미 계산한 생존 판정과 순위가 소급해서 달라진다.</b>
        /// </item>
        /// <item>
        /// 엄격 배치 모드에서는 열린 틱이 없으면 아무것도 받지 않는다. 기본 모드는
        /// 틱 없이 시간만 주는 옛 호출자를 그대로 받는다.
        /// </item>
        /// </list>
        /// </summary>
        private bool IsAcceptableTick(int? authorityTick)
        {
            if (authorityTick.HasValue)
            {
                if (authorityTick.Value < 0)
                {
                    return false;
                }

                if (openTick.HasValue)
                {
                    return authorityTick.Value == openTick.Value;
                }

                if (lastClosedTick.HasValue && authorityTick.Value <= lastClosedTick.Value)
                {
                    return false;
                }
            }

            return !strictBatchMode || openTick.HasValue;
        }

        private bool CanRecord(double occurredAt) =>
            isActive && !isSettled && IsFinite(occurredAt) && occurredAt >= 0d;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
