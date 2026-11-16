using System;
using BettingRoyale.Core;
using BettingRoyale.Data.Scenes;

namespace BettingRoyale.Data.Online
{
    public enum OnlineCoreLoopPhase
    {
        Lobby = 0,
        Loading = 1,
        Preparation = 2,
        Combat = 3,
        Summary = 4,
        Finished = 5,
    }

    /// <summary>온라인 통합 플레이테스트에서 Host가 대기실에서 확정하는 라운드 설정.</summary>
    public readonly struct OnlineCoreLoopRules : IEquatable<OnlineCoreLoopRules>
    {
        public const int MinRounds = 1;
        public const int MaxRounds = 3;
        public const int MinPhaseSeconds = 5;
        public const int MaxPhaseSeconds = 180;
        public const int MaxBlessingSelectionSeconds = 30;

        public OnlineCoreLoopRules(
            int totalRounds,
            int preparationSeconds,
            int combatSeconds,
            int summarySeconds)
        {
            if (totalRounds < MinRounds || totalRounds > MaxRounds)
            {
                throw new ArgumentOutOfRangeException(nameof(totalRounds));
            }
            if (!IsValidDuration(preparationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(preparationSeconds));
            }
            if (!IsValidDuration(combatSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(combatSeconds));
            }
            if (!IsValidDuration(summarySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(summarySeconds));
            }

            TotalRounds = totalRounds;
            PreparationSeconds = preparationSeconds;
            CombatSeconds = combatSeconds;
            SummarySeconds = summarySeconds;
        }

        /// <summary>기획 데이터(constants.TOTAL_ROUNDS·PREPARATION_SECONDS·ROUND_DURATION·SUMMARY_SECONDS)의 규칙. 없으면 코드 대역.</summary>
        public static OnlineCoreLoopRules Default => BettingRoyale.Data.Balance.BalanceCatalogs.Current.Rounds;

        /// <summary>코드 대역 — 3라운드 · 준비 90 · 전투 90 · 정산 10. 데이터 없는 경로만 쓴다.</summary>
        public static OnlineCoreLoopRules CreatePlaceholderDefault() => new(
            totalRounds: 3,
            preparationSeconds: 90,
            combatSeconds: 90,
            summarySeconds: 10);

        public static OnlineCoreLoopRules QuickPlaytest => new(
            totalRounds: 3,
            preparationSeconds: 10,
            combatSeconds: 30,
            summarySeconds: 5);

        public int TotalRounds { get; }
        public int PreparationSeconds { get; }
        public int BlessingSelectionSeconds => Math.Min(
            MaxBlessingSelectionSeconds,
            Math.Max(1, PreparationSeconds / 3));
        public int CombatSeconds { get; }
        public int SummarySeconds { get; }

        public OnlineCoreLoopRules WithRounds(int value) => new(
            Math.Clamp(value, MinRounds, MaxRounds),
            PreparationSeconds,
            CombatSeconds,
            SummarySeconds);

        public OnlineCoreLoopRules WithPreparationSeconds(int value) => new(
            TotalRounds,
            ClampDuration(value),
            CombatSeconds,
            SummarySeconds);

        /// <summary>
        /// ⚠️ <b>더 이상 전투 길이를 정하지 않는다</b> (S-GD-657ZN5). 전투는 시간 만료로 끝나지
        /// 않고 마지막 생존 진영까지 이어지며, 표시 시간은 <b>자기장 전역화까지 남은 시간</b>이다.
        ///
        /// <para>
        /// 이 값을 바꿔도 확정 자기장 단계(18·30·50·60·79·80초, 피해 5·8·15)는 <b>재조정되지 않는다</b> —
        /// 조용히 다시 스케일하면 확정 수치가 사라지고, 가짜 종료 타이머로 되살아난다.
        /// 기존 호출자를 깨뜨리지 않으려고 남겨 두었을 뿐이며 새 코드는 쓰지 않는다.
        /// </para>
        /// </summary>
        [Obsolete(
            "전투는 시간 만료로 끝나지 않는다 (S-GD-657ZN5). 이 값은 확정 자기장 단계에 영향을 주지 않는다.",
            error: false)]
        public OnlineCoreLoopRules WithCombatSeconds(int value) => new(
            TotalRounds,
            PreparationSeconds,
            ClampDuration(value),
            SummarySeconds);

        public bool Equals(OnlineCoreLoopRules other) =>
            TotalRounds == other.TotalRounds
            && PreparationSeconds == other.PreparationSeconds
            && CombatSeconds == other.CombatSeconds
            && SummarySeconds == other.SummarySeconds;

        public override bool Equals(object obj) => obj is OnlineCoreLoopRules other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            TotalRounds,
            PreparationSeconds,
            CombatSeconds,
            SummarySeconds);

        public static bool operator ==(OnlineCoreLoopRules left, OnlineCoreLoopRules right) => left.Equals(right);
        public static bool operator !=(OnlineCoreLoopRules left, OnlineCoreLoopRules right) => !left.Equals(right);

        private static bool IsValidDuration(int value) =>
            value >= MinPhaseSeconds && value <= MaxPhaseSeconds;

        private static int ClampDuration(int value) =>
            Math.Clamp(value, MinPhaseSeconds, MaxPhaseSeconds);
    }

    /// <summary>
    /// Host 가 확정한 <b>이번 라운드</b>의 사실 한 줄 (S15P21A606-170).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 마지막 생존 진영 관찰 중에도 화면에 살아 있는 중간 순위표의 원본이다. 담기는 것은
    /// Host 가 실제로 관측한 사실뿐이다 — 생사·이탈·이번 라운드 킬·어시스트·라운드 번호.
    /// <b>점수와 등수는 없다</b>: 정산 경계 전에 그 둘을 추정하면 화면 등수와 확정 등수가 갈린다
    /// (S-GD-P7SC3Q · D-GD-7ZMMYN).
    /// </para>
    /// <para>
    /// <see cref="HasFacts"/> 가 거짓이면 <b>Host 가 아직 이번 라운드 사실을 싣지 않았다</b>는 뜻이다.
    /// 받는 쪽은 그때 스스로 생사를 추정하지 않는다.
    /// </para>
    /// </remarks>
    public readonly struct OnlineCoreLoopRoundFacts : IEquatable<OnlineCoreLoopRoundFacts>
    {
        public OnlineCoreLoopRoundFacts(int round, bool isAlive, bool isForfeited, int kills, int assists)
        {
            Round = Math.Max(0, round);
            IsAlive = isAlive && !isForfeited;
            IsForfeited = isForfeited;
            Kills = Math.Max(0, kills);
            Assists = Math.Max(0, assists);
        }

        /// <summary>사실이 실린 라운드 번호. 0 이면 실리지 않았다는 뜻이다.</summary>
        public int Round { get; }

        /// <summary>지금 살아 있는가. 빈사·이탈은 죽은 것으로 본다.</summary>
        public bool IsAlive { get; }

        public bool IsForfeited { get; }

        /// <summary>이번 라운드에 Host 가 관측한 킬. 매치 누적이 아니다.</summary>
        public int Kills { get; }

        /// <summary>이번 라운드에 Host 가 관측한 어시스트. 매치 누적이 아니다.</summary>
        public int Assists { get; }

        /// <summary>Host 가 이번 라운드 사실을 실었는가.</summary>
        public bool HasFacts => Round > 0;

        public bool Equals(OnlineCoreLoopRoundFacts other) =>
            Round == other.Round
            && IsAlive == other.IsAlive
            && IsForfeited == other.IsForfeited
            && Kills == other.Kills
            && Assists == other.Assists;

        public override bool Equals(object obj) =>
            obj is OnlineCoreLoopRoundFacts other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Round, IsAlive, IsForfeited, Kills, Assists);
    }

    /// <summary>
    /// Host 가 정한 <b>이번 라운드</b>의 순위·점수 한 줄 (S15P21A606-292 · S-ONL-9D34TP).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 라운드 순위는 <b>Host 만</b> 정한다 — 경제 경계의 <c>RoundSettlementPlanner</c> 가 S-GD-7G8ZTA
    /// 유일 순위 규칙으로 만든 값이다. 받는 쪽은 생사·킬로 순위를 다시 매기지 않는다
    /// (S-GD-P7SC3Q · D-GD-7ZMMYN).
    /// </para>
    /// <para>
    /// 두 단계로 실린다. ① 마지막 생존 진영이 확정되어 관찰 창이 열리면 Host 가 그 순간의 사실로
    /// 순위를 정해 싣는다(<see cref="IsSettled"/> 거짓, <see cref="Score"/> 0). 관찰 창은 공격·피해가
    /// 막혀 있어 생사가 더 바뀌지 않는다. ② 정산 경계에서 확정값으로 덮는다(<see cref="IsSettled"/> 참,
    /// <see cref="Score"/> = 이번 라운드 획득 점수). 관찰 중 이탈 같은 드문 경우에만 ①과 ②가 다를 수 있고,
    /// 그때는 ②가 정본이다.
    /// </para>
    /// </remarks>
    public readonly struct OnlineCoreLoopRoundResult : IEquatable<OnlineCoreLoopRoundResult>
    {
        public OnlineCoreLoopRoundResult(int round, int rank, int score, int kills, int assists, bool isSettled)
        {
            Round = Math.Max(0, round);
            Rank = Math.Max(0, rank);
            Score = isSettled ? Math.Max(0, score) : 0;
            Kills = Math.Max(0, kills);
            Assists = Math.Max(0, assists);
            IsSettled = isSettled;
        }

        /// <summary>결과가 속한 라운드 번호. 0 이면 실리지 않았다는 뜻이다.</summary>
        public int Round { get; }

        /// <summary>Host 가 정한 1부터의 라운드 순위. 0 이면 아직 없다.</summary>
        public int Rank { get; }

        /// <summary>이번 라운드에 얻은 점수(순위 점수 + 킬 + 어시스트). 정산 전에는 0 이다.</summary>
        public int Score { get; }

        /// <summary>이번 라운드 킬. 매치 누적이 아니다.</summary>
        public int Kills { get; }

        /// <summary>이번 라운드 어시스트. 매치 누적이 아니다.</summary>
        public int Assists { get; }

        /// <summary>정산 경계에서 확정된 값인가. 거짓이면 관찰 창에서 먼저 실린 순위다.</summary>
        public bool IsSettled { get; }

        /// <summary>순위가 실려 있는가.</summary>
        public bool HasRank => Round > 0 && Rank > 0;

        public bool Equals(OnlineCoreLoopRoundResult other) =>
            Round == other.Round
            && Rank == other.Rank
            && Score == other.Score
            && Kills == other.Kills
            && Assists == other.Assists
            && IsSettled == other.IsSettled;

        public override bool Equals(object obj) =>
            obj is OnlineCoreLoopRoundResult other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Round, Rank, Score, Kills, Assists, IsSettled);
    }

    /// <summary>
    /// Host 가 확정한 한 참가자의 순위표 한 줄.
    ///
    /// <para>
    /// <b>점수·등수는 Host 의 확정값이다</b> — 정본은 경제 경계의 <c>FinalStandings</c> 이고
    /// (S-GD-P7SC3Q 배점 · S-GD-MNFBV7 동점 9단계 · S-GD-7G8ZTA 라운드 유일 순위),
    /// client 는 <b>스스로 다시 계산하지 않고</b> 받은 값을 그대로 그린다.
    /// 아직 확정 순위가 없으면 <see cref="Rank"/> 가 <see cref="UnrankedRank"/> 다.
    /// </para>
    /// </summary>
    public readonly struct OnlineCoreLoopStanding
    {
        /// <summary>아직 확정 순위가 없다는 뜻. 순위는 1부터라 0 과 충돌하지 않는다.</summary>
        public const int UnrankedRank = 0;

        public OnlineCoreLoopStanding(
            int playerId,
            string nickName,
            int kills,
            int assists,
            int survivedRounds)
            : this(playerId, nickName, kills, assists, survivedRounds, 0, UnrankedRank)
        {
        }

        public OnlineCoreLoopStanding(
            int playerId,
            string nickName,
            int kills,
            int assists,
            int survivedRounds,
            int score,
            int rank)
            : this(playerId, nickName, kills, assists, survivedRounds, score, rank, default)
        {
        }

        /// <param name="roundFacts">
        /// Host 가 확정한 이번 라운드 사실(S15P21A606-170). 싣지 않으면 <c>default</c> 이고
        /// 받는 쪽은 생사를 스스로 추정하지 않는다.
        /// </param>
        public OnlineCoreLoopStanding(
            int playerId,
            string nickName,
            int kills,
            int assists,
            int survivedRounds,
            int score,
            int rank,
            OnlineCoreLoopRoundFacts roundFacts)
            : this(playerId, nickName, kills, assists, survivedRounds, score, rank, roundFacts, default)
        {
        }

        /// <param name="roundResult">
        /// Host 가 정한 이번 라운드 순위·점수(S15P21A606-292). 싣지 않으면 <c>default</c> 이고
        /// 받는 쪽은 순위를 스스로 만들지 않는다.
        /// </param>
        public OnlineCoreLoopStanding(
            int playerId,
            string nickName,
            int kills,
            int assists,
            int survivedRounds,
            int score,
            int rank,
            OnlineCoreLoopRoundFacts roundFacts,
            OnlineCoreLoopRoundResult roundResult)
        {
            RoundFacts = roundFacts;
            RoundResult = roundResult;
            PlayerId = playerId;
            NickName = string.IsNullOrWhiteSpace(nickName) ? $"Player {playerId}" : nickName;
            Kills = Math.Max(0, kills);
            Assists = Math.Max(0, assists);
            SurvivedRounds = Math.Max(0, survivedRounds);
            Score = Math.Max(0, score);
            Rank = Math.Max(UnrankedRank, rank);
        }

        public int PlayerId { get; }
        public string NickName { get; }
        public int Kills { get; }
        public int Assists { get; }
        public int SurvivedRounds { get; }

        /// <summary>Host 가 확정한 누적 점수. 순위 점수 + 킬 + 어시스트다.</summary>
        public int Score { get; }

        /// <summary>Host 가 확정한 1부터의 등수. 확정 전에는 <see cref="UnrankedRank"/> 다.</summary>
        public int Rank { get; }

        /// <summary>확정 등수가 실려 있는가.</summary>
        public bool HasRank => Rank > UnrankedRank;

        /// <summary>
        /// Host 가 관측한 <b>이번 라운드</b> 사실. 실려 있지 않으면 <see cref="OnlineCoreLoopRoundFacts.HasFacts"/>
        /// 가 거짓이다. 점수·등수는 여기서 만들지 않는다 (S-GD-P7SC3Q).
        /// </summary>
        public OnlineCoreLoopRoundFacts RoundFacts { get; }

        /// <summary>
        /// Host 가 정한 이번 라운드 순위·점수. 관찰 창에서 먼저 실리고 정산에서 확정된다.
        /// 실려 있지 않으면 <see cref="OnlineCoreLoopRoundResult.HasRank"/> 가 거짓이다.
        /// </summary>
        public OnlineCoreLoopRoundResult RoundResult { get; }
    }

    /// <summary>Host가 확정하고 모든 피어가 동일하게 표시하는 온라인 코어루프 전체 스냅샷.</summary>
    /// <summary>
    /// 매치·라운드 식별자를 만드는 규칙. <b>여기가 형식의 정본이다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>docs/client/identity-contract.md</c> 2-6·2-7 절의 구현이다. 문자열을 직접 조립하는 곳이
    /// 여러 군데 생기면 Host 로그·서버 제출·클라이언트 표시가 각자 다른 형식을 쓰게 된다 —
    /// 그 순간 두 PC 의 로그를 잇는다는 <see cref="OnlineCoreLoopSnapshot.MatchId"/> 의 목적이 사라진다.
    /// </para>
    /// </remarks>
    public static class MatchIdentity
    {
        /// <summary>
        /// <see cref="OnlineCoreLoopSnapshot.MatchId"/> 의 길이. 하이픈을 포함한 UUID 표준 표기다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>36 인 이유는 서버 계약이다.</b> <c>docs/server/api.md</c> 3-3 절 —
        /// "서버는 UUID 형식(8-4-4-4-12 hex)을 검증하며 위반 시 <c>400 VALIDATION_ERROR</c>".
        /// 하이픈 없는 32자를 보내면 결과 제출이 전부 400 으로 떨어진다(S15P21A606-52).
        /// </para>
        /// <para>
        /// ⚠️ 이 값을 바꾸면 스냅샷 전송 프로토콜의 상한도 함께 바꿔야 한다.
        /// 넘치면 <b>전송이 거부된다</b> — 조용히 잘리지 않게 막아 두었다.
        /// </para>
        /// </remarks>
        public const int MatchIdLength = 36;

        /// <summary>
        /// 매치 식별자를 새로 발급한다. <b>Host 만, 매치당 한 번.</b>
        /// </summary>
        /// <remarks>
        /// <c>"D"</c> 서식이 하이픈을 포함한 8-4-4-4-12 표기(36자)다. 서버가 이 형식을 검증하므로
        /// <c>"N"</c>(32자, 하이픈 없음)을 쓰면 안 된다.
        /// </remarks>
        public static string NewMatchId() => Guid.NewGuid().ToString("D");

        /// <summary>
        /// 라운드 식별자. 형식은 <c>{matchId}:{round}</c> 다 (identity-contract 2-7).
        /// </summary>
        /// <remarks>
        /// 별도 UUID 를 굴리지 않고 파생시키는 이유는 하나다 — 문자열만 보고 어느 매치의
        /// 몇 라운드인지 알 수 있어야 로그를 조인 없이 읽을 수 있다.
        /// </remarks>
        public static string RoundId(string matchId, int round) =>
            string.IsNullOrEmpty(matchId) ? string.Empty : $"{matchId}:{round}";
    }

    public readonly struct OnlineCoreLoopSnapshot
    {
        private readonly OnlineCoreLoopStanding[] standings;

        public OnlineCoreLoopSnapshot(
            OnlineCoreLoopRules rules,
            OnlineCoreLoopPhase phase,
            int currentRound,
            float remainingSeconds,
            OnlineCoreLoopStanding[] standings,
            string matchId = null)
            : this(
                rules,
                phase,
                currentRound,
                remainingSeconds,
                standings,
                matchId,
                MatchSceneCatalog.DefaultPreparation,
                MatchSceneCatalog.DefaultCombat)
        {
        }

        public OnlineCoreLoopSnapshot(
            OnlineCoreLoopRules rules,
            OnlineCoreLoopPhase phase,
            int currentRound,
            float remainingSeconds,
            OnlineCoreLoopStanding[] standings,
            string matchId,
            MatchSceneKey preparationMapKey,
            MatchSceneKey combatMapKey)
            : this(
                rules,
                phase,
                currentRound,
                remainingSeconds,
                standings,
                matchId,
                preparationMapKey,
                combatMapKey,
                isObservingLastSurvivor: false)
        {
        }

        /// <param name="isObservingLastSurvivor">
        /// Host 가 마지막 생존 진영 관찰 상태에 있는가 (S15P21A606-170). 이 값이 참이면
        /// 전투 단계는 그대로지만 공격·피해가 막힌다 — 사실이 이미 확정됐기 때문이다.
        /// </param>
        public OnlineCoreLoopSnapshot(
            OnlineCoreLoopRules rules,
            OnlineCoreLoopPhase phase,
            int currentRound,
            float remainingSeconds,
            OnlineCoreLoopStanding[] standings,
            string matchId,
            MatchSceneKey preparationMapKey,
            MatchSceneKey combatMapKey,
            bool isObservingLastSurvivor)
            : this(
                rules,
                phase,
                currentRound,
                remainingSeconds,
                standings,
                matchId,
                preparationMapKey,
                combatMapKey,
                isObservingLastSurvivor,
                rally: default)
        {
        }

        /// <param name="rally">
        /// Host 가 확정한 집결 현황 (S-GD-6VWPVE). 준비 단계 밖에서는 실리지 않는다 —
        /// 아래에서 단계로 한 번 더 거른다.
        /// </param>
        public OnlineCoreLoopSnapshot(
            OnlineCoreLoopRules rules,
            OnlineCoreLoopPhase phase,
            int currentRound,
            float remainingSeconds,
            OnlineCoreLoopStanding[] standings,
            string matchId,
            MatchSceneKey preparationMapKey,
            MatchSceneKey combatMapKey,
            bool isObservingLastSurvivor,
            PreparationRallyStatus rally,
            float loadingProgress = 0f,
            CombatEntryStatus combatEntry = default)
        {
            // 전환 단계는 제 자리에서만 뜻이 있다 — 페이드 아웃은 준비 단계 끝, 나머지는 전투 단계 앞이다.
            // 새어 나가면 다음 준비 단계의 이동까지 막힌다.
            CombatEntry = (combatEntry.Stage == CombatEntryStage.FadeOut
                    ? phase == OnlineCoreLoopPhase.Preparation
                    : phase == OnlineCoreLoopPhase.Combat)
                ? combatEntry
                : default;
            // 집결은 준비 단계 안에서만 뜻이 있다. 다른 단계로 새어 나가면 전투 중에도 "3 / 4" 가
            // 떠서, 지금 무엇을 기다리는 화면인지 읽는 사람이 알 수 없게 된다.
            Rally = phase == OnlineCoreLoopPhase.Preparation ? rally : default;
            // 관찰은 전투 단계 안에서만 뜻이 있다. 다른 단계로 새어 나가면 준비 단계의 공격까지 막힌다.
            IsObservingLastSurvivor = isObservingLastSurvivor && phase == OnlineCoreLoopPhase.Combat;
            Rules = rules;
            Phase = phase;
            CurrentRound = Math.Clamp(currentRound, 0, rules.TotalRounds);
            RemainingSeconds = Math.Max(0f, remainingSeconds);
            LoadingProgress = phase == OnlineCoreLoopPhase.Loading
                ? Math.Clamp(loadingProgress, 0f, 1f)
                : phase == OnlineCoreLoopPhase.Preparation ? 1f : 0f;
            MatchId = string.IsNullOrEmpty(matchId) ? string.Empty : matchId;
            MatchSceneKey normalizedPreparation = MatchSceneCatalog.TryGetForPhase(
                preparationMapKey, MatchScenePhase.Preparation, out _)
                ? preparationMapKey
                : MatchSceneCatalog.DefaultPreparation;
            MatchSceneKey normalizedCombat = MatchSceneCatalog.TryGetForPhase(
                combatMapKey, MatchScenePhase.Combat, out _)
                ? combatMapKey
                : MatchSceneCatalog.DefaultCombat;
            if (!MatchSceneCatalog.IsValidSelection(normalizedPreparation, normalizedCombat))
            {
                normalizedPreparation = MatchSceneCatalog.DefaultPreparation;
                normalizedCombat = MatchSceneCatalog.DefaultCombat;
            }

            PreparationMapKey = normalizedPreparation;
            CombatMapKey = normalizedCombat;
            this.standings = standings == null
                ? Array.Empty<OnlineCoreLoopStanding>()
                : (OnlineCoreLoopStanding[])standings.Clone();
        }

        public OnlineCoreLoopRules Rules { get; }
        public OnlineCoreLoopPhase Phase { get; }
        public int CurrentRound { get; }
        public float RemainingSeconds { get; }
        /// <summary>Host가 각 사람의 0~100 보고를 평균 낸 매치 진입 진행률.</summary>
        public float LoadingProgress { get; }

        /// <summary>
        /// 이 매치의 식별자. Host 가 매치 시작 시 한 번 만들어 전원에게 전파한다
        /// (identity-contract 2-6). 매치 밖(<see cref="OnlineCoreLoopPhase.Lobby"/>)에서는 빈 문자열이다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>용도 셋.</b> ① 서버 전적 기록 키 ② 두 PC 로그를 잇는 상관 키
        /// ③ 요청·상태의 유효 범위 — 이전 매치의 늦은 응답이 새 매치를 덮지 않게 하는 기준이다.
        /// </para>
        /// <para>
        /// ⚠️ <b>Host 가 만드는 값이라 서버 입장에서 신뢰 대상이 아니다.</b> 서버가 중복·형식을 검증해야 한다.
        /// </para>
        /// </remarks>
        public string MatchId { get; }

        /// <summary>
        /// Host가 매치 시작 전에 확정해 전파한 준비 맵 키. 기본값은 Royal Colosseum이다.
        /// </summary>
        public MatchSceneKey PreparationMapKey { get; }

        /// <summary>
        /// Host가 매치 시작 전에 확정해 전파한 전투 맵 키. 기본값은 Combat_02다.
        /// </summary>
        public MatchSceneKey CombatMapKey { get; }

        /// <summary>현재 라운드의 식별자. <c>{MatchId}:{CurrentRound}</c> (identity-contract 2-7).</summary>
        public string RoundId => MatchIdentity.RoundId(MatchId, CurrentRound);
        public float BlessingSelectionRemainingSeconds => Phase == OnlineCoreLoopPhase.Preparation
            ? Math.Clamp(
                RemainingSeconds - (Rules.PreparationSeconds - Rules.BlessingSelectionSeconds),
                0f,
                Rules.BlessingSelectionSeconds)
            : 0f;
        public int StandingCount => standings?.Length ?? 0;
        public bool IsMatchActive => Phase != OnlineCoreLoopPhase.Lobby;

        /// <summary>
        /// Host 가 마지막 생존 진영을 <b>관찰 중</b>인가 (S15P21A606-170).
        /// </summary>
        /// <remarks>
        /// 전투 단계는 그대로다 — 요약으로 넘어가지도, 전역 정지가 걸리지도 않는다.
        /// 이동·카메라·유령 관전은 계속되고 공격·피해만 막힌다.
        /// </remarks>
        public bool IsObservingLastSurvivor { get; }

        /// <summary>
        /// Host 가 확정한 준비 구역 집결 현황 — "현재 있는 인원 / 총원" (S-GD-6VWPVE).
        /// </summary>
        /// <remarks>
        /// 준비 단계가 아니면 <see cref="PreparationRallyStatus.IsTracking"/> 이 거짓이다.
        /// 받는 쪽은 이 값을 그대로 그리고 <b>스스로 거리를 재지 않는다</b>(S-CLI-9QYFCT 규칙 1).
        /// </remarks>
        public PreparationRallyStatus Rally { get; }

        /// <summary>
        /// Host 가 확정한 준비 → 전투 전환 현황 — 페이드 아웃·검은 화면 대기·페이드 인·3초 카운트다운
        /// (S-GD-4JDCGB · S-GD-7RCHF8). 이 동안에는 이동·공격·피해가 모두 막힌다.
        /// </summary>
        public CombatEntryStatus CombatEntry { get; }

        /// <summary>
        /// 이동은 관찰 중에도 계속된다 — 마지막 생존자가 전장을 돌아보고 관전할 수 있어야 한다.
        /// 전투 진입 절차 중에는 막는다 — 카운트다운이 끝난 뒤에만 조작한다.
        /// </summary>
        public bool CanMove => Phase is OnlineCoreLoopPhase.Preparation or OnlineCoreLoopPhase.Combat
            && !CombatEntry.IsActive;

        // 준비 콜로세움에서는 공격·무기 전환을 허용하되 플레이어 피해는 아래 계약으로 계속 차단한다.
        // 관찰 중에는 전투 행동을 막는다 — 이미 확정된 Host 사실을 늦은 공격이 바꾸지 못하게 한다.
        public bool CanUseCombatActions =>
            !CombatEntry.IsActive
            && (Phase == OnlineCoreLoopPhase.Preparation
                || (Phase == OnlineCoreLoopPhase.Combat && !IsObservingLastSurvivor));

        public bool CanReceiveCombatDamage =>
            Phase == OnlineCoreLoopPhase.Combat && !IsObservingLastSurvivor && !CombatEntry.IsActive;

        /// <summary>
        /// Host 가 이번 라운드 사실을 실은 줄이 하나라도 있는가. 없으면 받는 쪽은 생사·킬을
        /// 스스로 추정하지 않는다 (S-GD-JRKHQA).
        /// </summary>
        public bool HasLiveRoundFacts
        {
            get
            {
                if (standings == null)
                {
                    return false;
                }

                for (var i = 0; i < standings.Length; i++)
                {
                    if (standings[i].RoundFacts.HasFacts)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Host 사실 기준으로 지금 살아 있는 인원. 사실이 실려 있지 않으면 <c>false</c> 다 —
        /// 추정값을 만들지 않는다.
        /// </summary>
        public bool TryCountLiveParticipants(out int alive, out int total)
        {
            alive = 0;
            total = 0;
            if (standings == null)
            {
                return false;
            }

            for (var i = 0; i < standings.Length; i++)
            {
                OnlineCoreLoopRoundFacts facts = standings[i].RoundFacts;
                if (!facts.HasFacts)
                {
                    continue;
                }

                total++;
                if (facts.IsAlive)
                {
                    alive++;
                }
            }

            return total > 0;
        }

        /// <summary>
        /// 이 스냅샷의 <b>현재 라운드</b>에 대해 Host 가 정한 한 참가자의 라운드 결과.
        /// 지난 라운드의 값이나 순위 없는 줄은 거짓이다 — 받는 쪽은 순위를 만들지 않는다.
        /// </summary>
        public bool TryGetRoundResult(int playerId, out OnlineCoreLoopRoundResult result)
        {
            if (standings != null)
            {
                for (var i = 0; i < standings.Length; i++)
                {
                    if (standings[i].PlayerId != playerId)
                    {
                        continue;
                    }

                    OnlineCoreLoopRoundResult candidate = standings[i].RoundResult;
                    if (candidate.HasRank && candidate.Round == CurrentRound)
                    {
                        result = candidate;
                        return true;
                    }

                    break;
                }
            }

            result = default;
            return false;
        }

        public OnlineCoreLoopStanding[] CopyStandings() => standings == null
            ? Array.Empty<OnlineCoreLoopStanding>()
            : (OnlineCoreLoopStanding[])standings.Clone();
    }

    public readonly struct OnlineCoreLoopChanged : IGameEvent
    {
        public OnlineCoreLoopChanged(OnlineCoreLoopSnapshot snapshot) => Snapshot = snapshot;

        public OnlineCoreLoopSnapshot Snapshot { get; }
    }
}
