using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 한 매치의 <b>고정 명부</b> 한 줄. Host 가 매치를 시작할 때 확정하고 매치 내내 바꾸지 않는다
    /// (S-GD-QJA6NY 의 "N 은 매치 시작 계정 수", S-GD-7G8ZTA 의 입장 순서).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><see cref="Combatant"/> 와 <see cref="PlayerId"/> 는 다른 값일 수 있다.</b>
    /// 전투원 식별자는 Network 어댑터가 세션 <c>PlayerId</c> 에서 파생시키며(오프셋이 붙는다),
    /// 그 변환 규칙은 <c>Scripts/Network/</c> 소유다. 여기서 <c>CombatantId.Value</c> 를
    /// <c>PlayerId</c> 로 간주하지 않는다 — <c>docs/client/identity-contract.md</c>.
    /// </para>
    /// <para>
    /// <see cref="LobbyJoinOrder"/> 는 Host 가 <b>실제 참가 이벤트 순간</b>에 매기는 단조 증가 서수다.
    /// <c>PlayerId</c> 정렬은 입장 순서가 아니다 — Fusion 은 빈 슬롯 번호를 재사용한다.
    /// </para>
    /// </remarks>
    public readonly struct CombatRosterMember
    {
        public readonly CombatantId Combatant;
        public readonly int PlayerId;
        public readonly int LobbyJoinOrder;

        public CombatRosterMember(CombatantId combatant, int playerId, int lobbyJoinOrder)
        {
            if (lobbyJoinOrder <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lobbyJoinOrder),
                    lobbyJoinOrder,
                    "대기실 입장 순서는 1부터 시작하는 확정값이어야 합니다 (S-GD-7G8ZTA).");
            }

            Combatant = combatant;
            PlayerId = playerId;
            LobbyJoinOrder = lobbyJoinOrder;
        }

        /// <summary>
        /// 확정된 명부 한 줄인가. <c>default(CombatRosterMember)</c> 나 배열 기본값은 생성자 검증을
        /// 거치지 않고 <see cref="LobbyJoinOrder"/> 가 0 이므로, 명부를 받는 쪽이 다시 확인해야 한다
        /// (S-GD-7G8ZTA — 입장 순서 미확정을 받아들이면 PlayerId 로 순위를 가르게 된다).
        /// </summary>
        public bool IsValid => LobbyJoinOrder > 0;

        public override string ToString() =>
            $"P{PlayerId} (combatant {Combatant.Value}, join {LobbyJoinOrder})";
    }

    /// <summary>
    /// 라운드 원천 사실을 모으는 기록기의 <b>Data 계약</b>.
    /// 구현 정본은 <c>Gameplay/Combat/CombatRoundResultState</c> 이고,
    /// Network 는 이 인터페이스로만 본다 (D-009 의 격리 경계).
    /// </summary>
    public interface ICombatRoundFactRecorder
    {
        bool IsTickOpen { get; }

        int? OpenTick { get; }

        int? LastCompletedTick { get; }

        bool CanFinalize { get; }

        void BeginRound(int roundNumber, IEnumerable<CombatantId> participants);

        bool BeginAuthorityTick(int tick);

        bool EndAuthorityTick();

        /// <summary>
        /// 실제로 적용된 피해 하나를 기록한다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>이벤트 구조체가 아니라 값으로 받는다.</b> <c>DamageApplied</c> 는 <c>UnityEngine</c> 에
        /// 의존하는 파일(<c>CombatEvents.cs</c>)에 있어 순수 도메인 경계가 참조할 수 없다(D-060).
        /// 어댑터가 이벤트를 여기서 값으로 풀어 넘긴다.
        /// </remarks>
        /// <param name="amount">이미 <c>min(남은 체력, 요청 피해)</c> 인 <b>실제로 깎인 양</b>이다.</param>
        bool RecordDamageFact(
            CombatantId attacker,
            CombatantId target,
            WeaponType weapon,
            float amount,
            CombatDamageSource source,
            double occurredAt,
            int? authorityTick);

        /// <summary>
        /// 보호막이 한 대를 전부 받아 체력이 깎이지 않은 타격을 기록한다. 킬·어시스트 기여로만 세고
        /// 실제 적 피해량(S-GD-7G8ZTA)에는 넣지 않는다 — 보호막만 깎아도 어시스트다(사용자 확정 2026-09-18, S-GD-Y534KH).
        /// </summary>
        bool RecordShieldFact(
            CombatantId attacker,
            CombatantId target,
            CombatDamageSource source,
            double occurredAt,
            int? authorityTick);

        /// <summary>빈사 하나를 기록한다. 자기장 빈사도 이 경로로 들어온다.</summary>
        bool RecordDownedFact(
            CombatantId combatant,
            CombatantId attacker,
            CombatDamageSource source,
            double occurredAt,
            int? authorityTick);

        bool TrySettle(double occurredAt, out CombatRoundResultsReady settled);

        /// <summary>
        /// 아직 봉인하지 않은 <b>현재까지의</b> 사실 사본. 중간(라운드 진행 중) 순위표 표시 전용이다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>정산이 아니다.</b> 원장을 봉인하지 않고 아무 상태도 바꾸지 않는다. 확정 스냅샷은
        /// <see cref="TrySettle"/> 하나뿐이며(S-GD-JRKHQA — 정산 경계에서 한 번), 이 값으로
        /// 점수·등수를 만들지 않는다(S-GD-P7SC3Q · D-GD-7ZMMYN).
        /// </para>
        /// <para>
        /// <see cref="CombatantRoundResult.Survived"/> 는 "지금까지 빈사가 아니다" 라는 <b>진행 중</b>
        /// 사실이고, 탈락 순번·틱은 이미 관측된 값만 실린다.
        /// </para>
        /// </remarks>
        /// <returns>라운드가 열려 있어 사본을 만들었으면 true.</returns>
        bool TryCopyLiveFacts(out CombatantRoundResult[] facts);
    }

    /// <summary>
    /// 기록기 구현을 <b>씬·컴포넌트 없이</b> 얻는 자리. Gameplay 가 부팅에서 등록하고
    /// Network 는 여기서 받아 쓴다 — <c>FindObjectsByType</c> 로 개발 씬 컴포넌트를 찾지 않는다
    /// (S-CLI-RQB4QD).
    /// </summary>
    public static class CombatRoundFactRecorders
    {
        private static Func<double, Func<CombatantId, CombatantId, bool>, ICombatRoundFactRecorder> factory;

        /// <summary>등록된 구현이 있는가.</summary>
        public static bool HasFactory => factory != null;

        public static void Register(
            Func<double, Func<CombatantId, CombatantId, bool>, ICombatRoundFactRecorder> value) =>
            factory = value ?? throw new ArgumentNullException(nameof(value));

        /// <summary>테스트·도메인 리로드 경계에서 등록을 비운다.</summary>
        public static void Clear() => factory = null;

        public static bool TryCreate(
            double assistWindowSeconds,
            Func<CombatantId, CombatantId, bool> enemyFilter,
            out ICombatRoundFactRecorder recorder)
        {
            if (factory == null)
            {
                recorder = null;
                return false;
            }

            recorder = factory(assistWindowSeconds, enemyFilter);
            return recorder != null;
        }
    }

    /// <summary>
    /// 라운드 진행 중 순위표 한 줄. <b>Host 가 확정한 사실만</b> 담고 점수·등수는 담지 않는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 마지막 생존 진영 관찰 상태(S15P21A606-170)에서 HUD 가 그리는 중간 순위표의 원본이다.
    /// 점수·등수는 정산 경계(<c>FinalStandings</c>)가 확정하기 전에는 존재하지 않으므로 여기에 없다 —
    /// client 도 Host 도 이 값으로 등수를 추정하지 않는다(S-GD-P7SC3Q · D-GD-7ZMMYN).
    /// </para>
    /// </remarks>
    public readonly struct CombatRoundLiveStanding
    {
        public CombatRoundLiveStanding(
            int playerId,
            CombatantId combatant,
            bool isAlive,
            bool isForfeited,
            int kills,
            int assists)
        {
            PlayerId = playerId;
            Combatant = combatant;
            IsAlive = isAlive;
            IsForfeited = isForfeited;
            Kills = Math.Max(0, kills);
            Assists = Math.Max(0, assists);
        }

        public int PlayerId { get; }
        public CombatantId Combatant { get; }

        /// <summary>지금 살아 있는가. 빈사·이탈은 죽은 것으로 본다.</summary>
        public bool IsAlive { get; }

        public bool IsForfeited { get; }

        /// <summary>이번 라운드에 관측된 킬 수. 누적 매치 집계가 아니다.</summary>
        public int Kills { get; }

        /// <summary>이번 라운드에 관측된 어시스트 수. 누적 매치 집계가 아니다.</summary>
        public int Assists { get; }

        public override string ToString() =>
            $"P{PlayerId} {(IsForfeited ? "이탈" : IsAlive ? "생존" : "빈사")} K{Kills} A{Assists}";
    }

    /// <summary>
    /// 마지막 생존 진영 <b>관찰 창</b>의 확정 길이(S15P21A606-170).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>전투 제한 시간이 아니다</b>(S-GD-657ZN5). 라운드 종료 사실은 이미 권위 틱 경계에서
    /// 확정됐고, 이 창은 그 확정 뒤 <b>요약으로 넘어가기 전까지</b> 마지막 생존자가 전장을 계속
    /// 보고 움직일 수 있는 시간일 뿐이다. 창 안에서는 공격·피해가 막혀 사실이 바뀌지 않는다.
    /// </para>
    /// <para>
    /// Direct CombatLab · Local Play · 제품 Host 가 <b>같은 값</b>을 쓴다 — 경로마다 다른 길이를
    /// 쓰면 같은 상황을 QA 가 세 번 다르게 보고하게 된다.
    /// </para>
    /// </remarks>
    public static class CombatRoundObservationWindow
    {
        /// <summary>확정 관찰 창(초).</summary>
        public const float DefaultSeconds = 5f;

        /// <summary>쓸 수 있는 창 길이인가. 0 이하·NaN·무한대는 창을 열 근거가 못 된다.</summary>
        public static bool IsValidWindow(float seconds) =>
            !float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds > 0f;

        /// <summary>진행시킬 수 있는 delta 인가. 0·음수·NaN·무한대는 버린다.</summary>
        public static bool IsAdvanceableDelta(float deltaSeconds) =>
            !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds) && deltaSeconds > 0f;
    }

    /// <summary>
    /// Host 가 전투 진입에서 무장하고 라운드가 끝나면 해제하는 <b>씬 독립 관측 런타임</b>
    /// (S-GD-7G8ZTA · S-CLI-RQB4QD).
    ///
    /// <para>
    /// 명부 밖 전투원(다른 씬의 더미, client 사본, 준비 허수아비)은 절대 집계에 들어가지 않는다.
    /// 권위 틱 경계 안에서만 사실을 받아들이며, 틱이 열려 있는 동안에는 정산하지 않는다.
    /// </para>
    /// <para>
    /// 진영 해석은 <see cref="ICombatSideResolver"/> 로 주입한다. 주입하지 않으면 개인전
    /// (<see cref="IndividualCombatSideResolver"/> — 전투원 하나가 곧 한 진영)이다. 팀전 해석기를
    /// 넣으면 같은 팀끼리의 피해가 실제 적 피해로 새지 않고, 생존 진영도 사람 수가 아니라
    /// <b>서로 다른 진영 수</b>로 센다 (S-GD-657ZN5 — 마지막 생존 "진영").
    /// </para>
    /// </summary>
    public sealed class MatchRoundObservationRuntime
    {
        private readonly ICombatRoundFactRecorder recorder;
        private readonly ICombatSideResolver sideResolver;
        private readonly float observationWindowSeconds;
        private readonly List<CombatRosterMember> roster = new List<CombatRosterMember>();
        private readonly Dictionary<int, int> rosterIndexByPlayerId = new Dictionary<int, int>();
        private readonly Dictionary<int, int> rosterIndexByCombatantId = new Dictionary<int, int>();
        private readonly HashSet<int> downedPlayerIds = new HashSet<int>();
        private readonly HashSet<int> forfeitedPlayerIds = new HashSet<int>();

        /// <summary>진영 계산용 재사용 버퍼. 틱마다 새 리스트를 만들지 않는다.</summary>
        private readonly List<CombatSideRules.MemberStatus> sideStatusBuffer =
            new List<CombatSideRules.MemberStatus>();

        /// <summary>기록기가 봉인한 그 스냅샷 원본. 하류가 받아갈 때까지 그대로 다시 내준다.</summary>
        private CombatRoundResultsReady settledSnapshot;

        public MatchRoundObservationRuntime(ICombatRoundFactRecorder recorder)
            : this(recorder, null)
        {
        }

        /// <param name="sideResolver">
        /// 팀전 진영 해석기. <c>null</c> 이면 개인전(<see cref="IndividualCombatSideResolver"/>).
        /// </param>
        public MatchRoundObservationRuntime(
            ICombatRoundFactRecorder recorder, ICombatSideResolver sideResolver)
            : this(recorder, sideResolver, CombatRoundObservationWindow.DefaultSeconds)
        {
        }

        /// <param name="observationWindowSeconds">
        /// 마지막 생존 진영 관찰 창(S15P21A606-170). 유효하지 않으면 확정 기본값을 쓴다 —
        /// 0 이나 NaN 을 받아들이면 관찰이 열리자마자 닫혀 예전의 즉시 요약과 구분되지 않는다.
        /// </param>
        public MatchRoundObservationRuntime(
            ICombatRoundFactRecorder recorder,
            ICombatSideResolver sideResolver,
            float observationWindowSeconds)
        {
            this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            this.sideResolver = sideResolver ?? IndividualCombatSideResolver.Instance;
            observationWindowSeconds = CombatRoundObservationWindow.IsValidWindow(observationWindowSeconds)
                ? observationWindowSeconds
                : CombatRoundObservationWindow.DefaultSeconds;
            this.observationWindowSeconds = observationWindowSeconds;
        }

        /// <summary>지금 사실을 받아들이는가. 해제 상태에서는 모든 관측을 버린다.</summary>
        public bool IsArmed { get; private set; }

        /// <summary>무장한 매치. 다른 매치의 늦은 이벤트를 거르는 기준이다.</summary>
        public string MatchId { get; private set; } = string.Empty;

        public int Round { get; private set; }

        public bool IsTickOpen => recorder.IsTickOpen;

        public int? OpenTick => recorder.OpenTick;

        public int? LastCompletedTick => recorder.LastCompletedTick;

        /// <summary>매치 시작 인원 N. 중도 이탈로 줄지 않는다 (S-GD-QJA6NY).</summary>
        public int RosterSize => roster.Count;

        public IReadOnlyList<CombatRosterMember> Roster => roster;

        /// <summary>
        /// 라운드 스냅샷이 봉인됐는가. 봉인 뒤에는 새 사실을 받지 않고,
        /// <see cref="TrySettle"/> 는 같은 스냅샷을 다시 내준다.
        /// </summary>
        public bool IsSettled { get; private set; }

        /// <summary>봉인된 스냅샷의 확정 권위 틱. 아직 봉인 전이면 <c>null</c> 이다.</summary>
        public int? SettledTick => IsSettled ? settledSnapshot.SettledTick : null;

        /// <summary>
        /// 명부가 처음부터 갖고 있던 <b>서로 다른 진영</b> 수. 개인전에서는 명부 인원과 같다.
        /// 중도 이탈·빈사로 줄지 않는다 (S-GD-QJA6NY).
        /// </summary>
        public int ConfiguredSideCount => CountDistinctSides(BuildSideStatuses(), aliveOnly: false);

        /// <summary>
        /// 아직 살아 있는 <b>서로 다른 진영</b> 수. 개인전이면 진영 = 사람이고,
        /// 팀전 해석기를 주입하면 같은 팀 생존자 여럿이 한 진영으로 묶인다.
        /// 이탈자는 살아 있는 진영으로 세지 않는다 — 조작할 사람이 없다.
        /// </summary>
        public int SurvivingSideCount => CountDistinctSides(BuildSideStatuses(), aliveOnly: true);

        /// <summary>
        /// 마지막 생존 진영이 <b>확정된 사실</b>인가. 틱이 닫힌 뒤에만 참이다
        /// (S-GD-657ZN5 의 종료 조건 · S-GD-7G8ZTA 의 틱 경계).
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>이 값은 "지금 요약으로 넘어가라" 가 아니다.</b> 호출자는 이것을 관찰 상태
        /// (<see cref="IsObservingLastSurvivor"/>) 진입 조건으로 쓰고, 실제 정산은 각 실행 경로가
        /// 가진 라운드 종료 경계에서 한 번 한다(S15P21A606-170). 생존 진영이 0 이면(같은 틱 전멸)
        /// 관찰할 대상이 없으므로 호출자가 곧바로 정산해도 된다 — <see cref="SurvivingSideCount"/> 로 가른다.
        /// </para>
        /// </remarks>
        public bool HasLastSurvivingSide =>
            IsArmed
            && !IsSettled
            && !recorder.IsTickOpen
            && recorder.CanFinalize
            && CombatSideRules.ShouldEndCombat(BuildSideStatuses());

        /// <summary>
        /// 마지막 생존 진영을 <b>관찰 중</b>인가. 전투 단계는 그대로 살아 있고 이동·관전은 계속되며,
        /// 여기서는 새 사실을 받지 않는다(S15P21A606-170).
        /// </summary>
        public bool IsObservingLastSurvivor { get; private set; }

        /// <summary>관찰로 들어간 시점의 마지막 확정 권위 틱. 관찰 전에는 <c>null</c> 이다.</summary>
        public int? ObservationEnteredTick { get; private set; }

        /// <summary>이 관측이 쓰는 관찰 창 길이(초).</summary>
        public float ObservationWindowSeconds => observationWindowSeconds;

        /// <summary>관찰을 연 뒤 흘려보낸 시간. 관찰 전에는 0 이다.</summary>
        public float ObservationElapsedSeconds { get; private set; }

        /// <summary>관찰이 끝나기까지 남은 시간. 관찰 중이 아니면 0 이다.</summary>
        public float ObservationRemainingSeconds => IsObservingLastSurvivor
            ? Math.Max(0f, observationWindowSeconds - ObservationElapsedSeconds)
            : 0f;

        /// <summary>관찰 창을 다 썼는가. 관찰 중이 아니면 거짓이다.</summary>
        public bool IsObservationComplete =>
            IsObservingLastSurvivor && ObservationElapsedSeconds >= observationWindowSeconds;

        /// <summary>
        /// 지금 전투 피해·공격 결과를 받아도 되는가. 관찰 상태와 봉인 뒤에는 거짓이다 —
        /// 관찰 중의 입력이 확정 사실을 바꾸지 못하게 하는 <b>도메인 쪽 게이트</b>다.
        /// </summary>
        public bool CanApplyCombatDamage => IsArmed && !IsSettled && !IsObservingLastSurvivor;

        /// <summary>
        /// 지금 관찰을 열어야 하는가. 마지막 생존 진영이 <b>남아 있을 때만</b> 참이다 —
        /// 같은 틱 전멸(생존 진영 0)은 관찰할 대상이 없으므로 호출자가 곧바로 정산한다.
        /// </summary>
        public bool RequiresLastSurvivorObservation =>
            HasLastSurvivingSide && !IsObservingLastSurvivor && SurvivingSideCount > 0;

        /// <summary>
        /// 마지막 생존 진영 관찰을 연다. 조건을 만족하는 <b>첫 호출만</b> true 다(멱등).
        /// </summary>
        /// <remarks>
        /// 관찰은 사실을 바꾸지 않는다 — 집계·명부·봉인 여부를 그대로 둔 채 입력 게이트만 닫는다.
        /// </remarks>
        public bool TryBeginLastSurvivorObservation()
        {
            if (!RequiresLastSurvivorObservation)
            {
                return false;
            }

            IsObservingLastSurvivor = true;
            ObservationElapsedSeconds = 0f;
            ObservationEnteredTick = recorder.LastCompletedTick;
            return true;
        }

        /// <summary>
        /// 관찰 창을 진행시킨다. 관찰 중이 아니거나 유효하지 않은 delta 면 아무것도 하지 않는다.
        /// </summary>
        /// <returns>이 호출로 관찰 창이 끝났으면 true. 이미 끝나 있었다면 false 다(한 번만 참).</returns>
        public bool AdvanceObservation(float deltaSeconds)
        {
            if (!IsObservingLastSurvivor
                || IsObservationComplete
                || !CombatRoundObservationWindow.IsAdvanceableDelta(deltaSeconds))
            {
                return false;
            }

            ObservationElapsedSeconds = Math.Min(
                observationWindowSeconds, ObservationElapsedSeconds + deltaSeconds);
            return IsObservationComplete;
        }

        /// <summary>
        /// 남은 관찰 창을 지금 닫는다. 개발용 SKIP·매치 취소처럼 <b>명시적인</b> 경계에서만 쓴다.
        /// </summary>
        /// <returns>열려 있던 관찰을 이 호출이 닫았으면 true.</returns>
        public bool CompleteObservationNow()
        {
            if (!IsObservingLastSurvivor || IsObservationComplete)
            {
                return false;
            }

            ObservationElapsedSeconds = observationWindowSeconds;
            return true;
        }

        /// <summary>
        /// 지금이 <b>정산 경계</b>인가. 라운드 종료 사실이 확정됐고, 관찰할 대상이 없거나
        /// 관찰 창을 다 썼을 때만 참이다(S15P21A606-170).
        /// </summary>
        /// <remarks>
        /// <see cref="ShouldSettle"/> 은 "라운드가 끝났다는 <b>사실</b>" 이고 이 값은
        /// "그래서 지금 확정해도 되는가" 다. 둘을 나눠 두지 않으면 마지막 생존자가 확정되는
        /// 순간 요약으로 넘어가 관찰이 사라진다.
        /// </remarks>
        public bool IsSettlementBoundaryReached
        {
            get
            {
                if (!ShouldSettle)
                {
                    return false;
                }

                if (IsSettled || SurvivingSideCount == 0)
                {
                    return true;
                }

                return IsObservationComplete;
            }
        }

        /// <summary>
        /// 진행 중인 사실을 중간 순위표 한 벌로 옮긴다. <b>정산하지 않는다</b> —
        /// 점수·등수는 만들지 않으며(S-GD-P7SC3Q), 봉인 뒤에는 봉인된 사실을 그대로 비춘다.
        /// </summary>
        public bool TryBuildLiveStandings(out CombatRoundLiveStanding[] standings)
        {
            if (!IsArmed || !recorder.TryCopyLiveFacts(out CombatantRoundResult[] facts))
            {
                standings = Array.Empty<CombatRoundLiveStanding>();
                return false;
            }

            var rows = new CombatRoundLiveStanding[roster.Count];
            for (var i = 0; i < roster.Count; i++)
            {
                CombatRosterMember member = roster[i];
                var kills = 0;
                var assists = 0;
                bool downed = downedPlayerIds.Contains(member.PlayerId);
                for (var j = 0; j < facts.Length; j++)
                {
                    if (facts[j].Combatant != member.Combatant)
                    {
                        continue;
                    }

                    kills = facts[j].Kills;
                    assists = facts[j].Assists;
                    downed = downed || !facts[j].Survived;
                    break;
                }

                bool forfeited = forfeitedPlayerIds.Contains(member.PlayerId);
                rows[i] = new CombatRoundLiveStanding(
                    member.PlayerId,
                    member.Combatant,
                    isAlive: !downed && !forfeited,
                    isForfeited: forfeited,
                    kills,
                    assists);
            }

            standings = rows;
            return true;
        }

        /// <summary>
        /// 지금 정산해도 되는가. <b>틱이 닫힌 뒤</b>에만 참이며, 생존 진영이 1 이하일 때만 참이다
        /// (S-GD-657ZN5 · S-GD-7G8ZTA). 시간 만료로는 참이 되지 않는다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 진영이 하나뿐인 명부(1명 단독 검증 세션, 전원 같은 팀)에서는 자동 종료하지 않는다 —
        /// 시작하자마자 "마지막 생존 진영" 이 되어 라운드가 즉시 끝난다. 그 경우는 개발용 SKIP 으로
        /// 끝낸다. 판정 본체는 <see cref="CombatSideRules.ShouldEndCombat"/> 와 같은 규칙이다.
        /// </para>
        /// <para>
        /// 이미 봉인된 스냅샷이 남아 있으면 계속 참이다 — 하류 경제 반영이 거절돼 재시도할 때
        /// 호출자의 <c>if (ShouldSettle) TrySettle(...)</c> 고리가 끊기지 않게 한다.
        /// </para>
        /// </remarks>
        public bool ShouldSettle
        {
            get
            {
                if (!IsArmed)
                {
                    return false;
                }

                if (IsSettled)
                {
                    return true;
                }

                return !recorder.IsTickOpen
                    && recorder.CanFinalize
                    && CombatSideRules.ShouldEndCombat(BuildSideStatuses());
            }
        }

        /// <summary>
        /// 전투 진입에서 명부를 고정하고 관측을 연다. 같은 라운드로 다시 부르면 그대로 유지한다.
        /// </summary>
        /// <returns>새로 무장했으면 true. 입력이 잘못됐으면 false 이고 상태를 바꾸지 않는다.</returns>
        public bool Arm(string matchId, int round, IReadOnlyList<CombatRosterMember> members)
        {
            if (string.IsNullOrEmpty(matchId) || round <= 0 || members == null || members.Count == 0)
            {
                return false;
            }

            if (IsArmed && MatchId == matchId && Round == round)
            {
                return false;
            }

            var seenPlayers = new HashSet<int>();
            var seenCombatants = new HashSet<int>();
            var seenJoinOrders = new HashSet<int>();
            for (var i = 0; i < members.Count; i++)
            {
                CombatRosterMember member = members[i];
                if (!member.IsValid)
                {
                    // default(CombatRosterMember) 는 생성자 검증을 건너뛴 채 입장 순서 0 으로 들어온다.
                    // 그대로 무장하면 순위 마지막 단계가 PlayerId 정렬로 무너진다 (S-GD-7G8ZTA).
                    return false;
                }

                if (!seenPlayers.Add(member.PlayerId)
                    || !seenCombatants.Add(member.Combatant.Value)
                    || !seenJoinOrders.Add(member.LobbyJoinOrder))
                {
                    // 중복 명부로 무장하면 순위가 유일해지지 않는다. 조용히 반쪽으로 시작하지 않는다.
                    return false;
                }
            }

            Disarm();

            MatchId = matchId;
            Round = round;
            for (var i = 0; i < members.Count; i++)
            {
                CombatRosterMember member = members[i];
                rosterIndexByPlayerId[member.PlayerId] = roster.Count;
                rosterIndexByCombatantId[member.Combatant.Value] = roster.Count;
                roster.Add(member);
            }

            var participants = new CombatantId[roster.Count];
            for (var i = 0; i < roster.Count; i++)
            {
                participants[i] = roster[i].Combatant;
            }

            recorder.BeginRound(round, participants);
            IsArmed = true;
            return true;
        }

        /// <summary>
        /// 관측을 끊고 명부·집계·봉인된 스냅샷을 비운다.
        /// 라운드 전환·매치 종료·연결 해제·씬 로드에서 부른다.
        /// </summary>
        /// <remarks>
        /// 여기서 스냅샷을 버리므로, 앞 라운드의 사실이 다음 무장에 새지 않는다. 하류 경제 반영이
        /// 아직 끝나지 않았다면 Disarm 하지 않는다 — 재시도할 원본이 사라진다.
        /// </remarks>
        public void Disarm()
        {
            IsArmed = false;
            IsSettled = false;
            settledSnapshot = default;
            // 관찰 상태도 라운드 안에서만 뜻이 있다. 남겨 두면 다음 라운드가 공격이 막힌 채 시작한다.
            IsObservingLastSurvivor = false;
            ObservationEnteredTick = null;
            ObservationElapsedSeconds = 0f;
            MatchId = string.Empty;
            Round = 0;
            roster.Clear();
            rosterIndexByPlayerId.Clear();
            rosterIndexByCombatantId.Clear();
            downedPlayerIds.Clear();
            forfeitedPlayerIds.Clear();
            sideStatusBuffer.Clear();
        }

        public bool TryGetPlayerId(CombatantId combatant, out int playerId)
        {
            if (rosterIndexByCombatantId.TryGetValue(combatant.Value, out int index))
            {
                playerId = roster[index].PlayerId;
                return true;
            }

            playerId = 0;
            return false;
        }

        public bool IsRosterCombatant(CombatantId combatant) =>
            rosterIndexByCombatantId.ContainsKey(combatant.Value);

        /// <summary>주입된 해석기로 이 전투원의 진영 식별자를 얻는다. 개인전이면 전투원 자신이다.</summary>
        public int ResolveSideId(CombatantId combatant) => sideResolver.ResolveSideId(combatant);

        /// <summary>
        /// 두 전투원이 <b>명부 안의 서로 다른 진영</b>인가. 기록기의 적 판정에 그대로 넘긴다 —
        /// 아군 피해·허수아비·다른 씬 더미가 킬·어시스트·실제 피해에 섞이지 않는다.
        /// 개인전 해석기에서는 "명부 안의 서로 다른 사람" 과 같은 뜻이다.
        /// </summary>
        public bool AreEnemies(CombatantId attacker, CombatantId target) =>
            attacker != target
            && IsRosterCombatant(attacker)
            && IsRosterCombatant(target)
            && ResolveSideId(attacker) != ResolveSideId(target);

        /// <summary>
        /// 중도 이탈을 표시한다. 표시 순위는 그대로 유일하게 받고 <b>배점만 꼴등</b>이다
        /// (S-GD-QJA6NY). 명부에서 빼지 않으므로 N 은 줄지 않는다.
        /// 봉인된 뒤에는 받지 않는다 — 확정된 스냅샷과 어긋난 사실이 된다.
        /// </summary>
        public bool MarkForfeited(int playerId) =>
            IsArmed
            && !IsSettled
            && rosterIndexByPlayerId.ContainsKey(playerId)
            && forfeitedPlayerIds.Add(playerId);

        public bool IsForfeited(int playerId) => forfeitedPlayerIds.Contains(playerId);

        public bool BeginAuthorityTick(int tick) =>
            IsArmed && !IsSettled && recorder.BeginAuthorityTick(tick);

        public bool EndAuthorityTick() => IsArmed && !IsSettled && recorder.EndAuthorityTick();

        /// <summary>
        /// 실제로 적용된 피해를 관측한다. 열린 권위 틱 안에서만, 명부 안의 두 사람 사이에서만 받는다.
        /// 봉인 뒤와 <b>관찰 중</b>에는 받지 않는다 — 확정된 사실이 뒤늦게 달라지지 않게 한다.
        /// </summary>
        public bool ObserveDamage(
            CombatantId attacker,
            CombatantId target,
            WeaponType weapon,
            float amount,
            CombatDamageSource source,
            double occurredAt)
        {
            if (!CanApplyCombatDamage || !recorder.IsTickOpen)
            {
                return false;
            }

            if (!IsRosterCombatant(attacker) || !IsRosterCombatant(target))
            {
                return false;
            }

            return recorder.RecordDamageFact(
                attacker, target, weapon, amount, source, occurredAt, recorder.OpenTick);
        }

        /// <summary>
        /// 보호막이 전부 받은 타격을 관측한다(<see cref="ICombatRoundFactRecorder.RecordShieldFact"/>).
        /// 받는 조건은 <see cref="ObserveDamage"/> 와 같다.
        /// </summary>
        public bool ObserveShieldHit(
            CombatantId attacker,
            CombatantId target,
            CombatDamageSource source,
            double occurredAt)
        {
            if (!CanApplyCombatDamage || !recorder.IsTickOpen)
            {
                return false;
            }

            if (!IsRosterCombatant(attacker) || !IsRosterCombatant(target))
            {
                return false;
            }

            return recorder.RecordShieldFact(attacker, target, source, occurredAt, recorder.OpenTick);
        }

        /// <summary>
        /// 빈사를 관측한다. 자기장 빈사(<see cref="CombatDamageSource.Zone"/>)도 여기로 온다.
        /// </summary>
        public bool ObserveDowned(
            CombatantId combatant,
            CombatantId attacker,
            CombatDamageSource source,
            double occurredAt)
        {
            if (!CanApplyCombatDamage || !recorder.IsTickOpen || !IsRosterCombatant(combatant))
            {
                return false;
            }

            if (!recorder.RecordDownedFact(combatant, attacker, source, occurredAt, recorder.OpenTick))
            {
                return false;
            }

            if (TryGetPlayerId(combatant, out int playerId))
            {
                downedPlayerIds.Add(playerId);
            }

            return true;
        }

        /// <summary>
        /// 라운드를 확정한다. 성공하면 관측은 <b>해제되지 않고</b> 봉인된다 — 같은 라운드를
        /// 두 번 확정하지 못하도록 기록기가 스스로 막는다. 호출자는 반영이 끝난 뒤 Disarm 한다.
        /// </summary>
        /// <remarks>
        /// 봉인된 뒤 다시 부르면 <b>기록기를 다시 부르지 않고</b> 처음 받은 스냅샷을 그대로 돌려준다.
        /// 하류 경제 반영이 거절돼 재시도해도 사실을 다시 계산하거나 새 사실을 섞지 않는다
        /// (S-GD-7G8ZTA — 한 Host 틱 전체를 한 번만 집계한다). 스냅샷은 배열을 복사해 들고 있는
        /// 불변 값이라, 호출자가 결과를 만져도 다음 재생이 달라지지 않는다.
        /// </remarks>
        public bool TrySettle(double occurredAt, out CombatRoundResultsReady results)
        {
            if (!IsArmed)
            {
                results = default;
                return false;
            }

            if (IsSettled)
            {
                results = settledSnapshot;
                return true;
            }

            if (recorder.IsTickOpen)
            {
                results = default;
                return false;
            }

            if (!recorder.TrySettle(occurredAt, out CombatRoundResultsReady sealedResults))
            {
                results = default;
                return false;
            }

            settledSnapshot = sealedResults;
            IsSettled = true;
            results = settledSnapshot;
            return true;
        }

        /// <summary>
        /// 봉인된 스냅샷을 시각만 없이 다시 본다. 아직 봉인 전이면 false 이고 기록기를 건드리지 않는다.
        /// </summary>
        public bool TryPeekSettled(out CombatRoundResultsReady results)
        {
            if (IsArmed && IsSettled)
            {
                results = settledSnapshot;
                return true;
            }

            results = default;
            return false;
        }

        /// <summary>명부를 <see cref="CombatSideRules"/> 가 읽는 진영 상태 목록으로 옮긴다.</summary>
        private IReadOnlyList<CombatSideRules.MemberStatus> BuildSideStatuses()
        {
            sideStatusBuffer.Clear();
            for (var i = 0; i < roster.Count; i++)
            {
                CombatRosterMember member = roster[i];
                bool alive = !downedPlayerIds.Contains(member.PlayerId)
                    && !forfeitedPlayerIds.Contains(member.PlayerId);
                sideStatusBuffer.Add(
                    new CombatSideRules.MemberStatus(ResolveSideId(member.Combatant), alive));
            }

            return sideStatusBuffer;
        }

        private static int CountDistinctSides(
            IReadOnlyList<CombatSideRules.MemberStatus> statuses, bool aliveOnly)
        {
            var sides = 0;
            for (var i = 0; i < statuses.Count; i++)
            {
                CombatSideRules.MemberStatus status = statuses[i];
                if (aliveOnly && !status.IsAlive)
                {
                    continue;
                }

                var counted = false;
                for (var j = 0; j < i; j++)
                {
                    CombatSideRules.MemberStatus earlier = statuses[j];
                    if (earlier.SideId == status.SideId && (!aliveOnly || earlier.IsAlive))
                    {
                        counted = true;
                        break;
                    }
                }

                if (!counted)
                {
                    sides++;
                }
            }

            return sides;
        }
    }
}
