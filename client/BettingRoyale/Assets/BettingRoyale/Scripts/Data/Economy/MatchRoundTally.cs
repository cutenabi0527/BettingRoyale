using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Economy
{
    /// <summary>
    /// 한 플레이어가 매치 동안 쌓은 전투 사실의 누적 (Jira S15P21A606-31 완료 조건 6).
    /// <b>점수도 순위도 아니다</b> — 그걸 정하는 규칙은 아직 스펙에 없다.
    /// </summary>
    public readonly struct MatchTallyEntry
    {
        /// <summary><c>Scripts/Network/</c> 의 <c>PlayerSlot.PlayerId</c> 와 같은 값.</summary>
        public readonly int PlayerId;

        public readonly int Kills;

        /// <summary>
        /// 킬 점수를 매길 때 쓰는 킬 수 — 라운드마다 그 라운드의 킬 점수 배율(저점 매수)을 곱해 더한 값이다
        /// (S-GD-TMPBEF). 배율이 없으면 <see cref="Kills"/> 와 같다. <b>동점 처리의 "킬 많은 순" 은 이 값이 아니라
        /// <see cref="Kills"/> 를 본다</b> — 실제로 쓰러뜨린 수는 바뀌지 않는다.
        /// </summary>
        public readonly int ScoredKills;

        /// <summary>어시스트 총합.</summary>
        public readonly int Assists;

        /// <summary>그중 원거리 어시스트 (S-GD-P7SC3Q). 근접은 <c>Assists - RangedAssists</c> 다.</summary>
        public readonly int RangedAssists;

        /// <summary>살아서 끝낸 라운드 수.</summary>
        public readonly int SurvivedRounds;

        /// <summary>결과가 반영된 라운드 수. 3라운드 매치라면 끝에 3이어야 한다.</summary>
        public readonly int CountedRounds;

        /// <summary>
        /// 라운드 번호 → 그 라운드의 생존 순위. <b>라운드 번호 오름차순으로 정렬돼 있다.</b>
        /// 최종 순위 동점 처리의 라운드 순위 단계가 이것을 늦은 라운드부터 본다 (S-GD-MNFBV7).
        /// 순위를 모르고 들어온 라운드는 담기지 않는다.
        /// </summary>
        public readonly IReadOnlyList<RoundPlacement> Placements;

        public MatchTallyEntry(
            int playerId,
            int kills,
            int assists,
            int rangedAssists,
            int survivedRounds,
            int countedRounds,
            IReadOnlyList<RoundPlacement> placements)
            : this(playerId, kills, assists, rangedAssists, survivedRounds, countedRounds, placements, kills)
        {
        }

        public MatchTallyEntry(
            int playerId,
            int kills,
            int assists,
            int rangedAssists,
            int survivedRounds,
            int countedRounds,
            IReadOnlyList<RoundPlacement> placements,
            int scoredKills)
        {
            PlayerId = playerId;
            Kills = kills;
            ScoredKills = scoredKills;
            Assists = assists;
            RangedAssists = rangedAssists;
            SurvivedRounds = survivedRounds;
            CountedRounds = countedRounds;
            Placements = placements ?? Array.Empty<RoundPlacement>();
        }

        /// <summary>근접 어시스트.</summary>
        public int MeleeAssists => Assists - RangedAssists;

        public override string ToString() =>
            $"P{PlayerId} {Kills}K {Assists}A 생존{SurvivedRounds} ({CountedRounds}R 반영)";
    }

    /// <summary>
    /// 라운드 전투 결과를 <b>중복 없이</b> 누적한다 (Jira S15P21A606-31 완료 조건 6).
    ///
    /// <b>왜 중복 방지가 별도 관심사인가</b> — Host Mode 에서 라운드 결과는 <b>두 번 이상 도착할 수 있다.</b>
    /// 재전송, 재접속 후 상태 재동기화, 늦게 들어온 관전자에게 다시 쏘는 경우가 전부 그렇다.
    /// 그때 그냥 더하면 킬 수가 조용히 부풀고, <b>최종 순위가 틀리는데 아무도 원인을 못 찾는다.</b>
    /// 그래서 "라운드 N 은 이미 반영했다" 를 이 클래스가 기억한다.
    ///
    /// <b>최종 순위를 여기서 정하지 않는다</b> — <c>docs/decisions-later.md</c> 의 <b>O-023</b> 이 미결이다.
    /// 티켓은 "최종 순서 <b>입력</b>을 제공" 이라고만 요구한다.
    ///
    /// S-GD-6XNW29 는 "승패는 <b>누적 전투 점수</b>로 정해진다" 고 하는데 <b>배점이 없고</b>,
    /// 정작 코드는 <c>Scripts/Network/OnlineCoreLoopCoordinator.cs</c> 에서 점수 없이
    /// <b>사전식 비교</b>(킬 &gt; 어시 &gt; 생존)로 돌고 있다. 스펙과 코드가 서로 다른 것을 말한다.
    /// 둘은 결과가 갈린다 — 킬 3·어시 0 과 킬 2·어시 5 의 순서가 안에 따라 뒤집힌다.
    ///
    /// 임의로 한쪽을 고르면 아무도 승인하지 않은 규칙이 사실상의 스펙으로 굳는다
    /// (이 저장소가 <c>ChestConfig</c> 에 <c>Default</c> 를 두지 않은 것과 같은 이유).
    /// <b>O-023 이 닫히면 그때 정렬을 붙인다.</b> 2026-09-01 회의 안건이고,
    /// 축4(-35)의 서버 제출 마감이 2026-09-02 다.
    ///
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class MatchRoundTally
    {
        private sealed class Row
        {
            public int Kills { get; set; }
            public int ScoredKills { get; set; }
            public int Assists { get; set; }
            public int RangedAssists { get; set; }
            public int SurvivedRounds { get; set; }
            public int CountedRounds { get; set; }

            /// <summary>라운드 번호 → 순위. 순위를 모르고 온 라운드는 담기지 않는다.</summary>
            public SortedDictionary<int, int> Placements { get; } = new();

            /// <summary>
            /// 표시 순위는 받았지만 <b>점수는 꼴등</b>이어야 하는 라운드(S-GD-QJA6NY 이탈 배점).
            /// 유일한 표시 순위(S-GD-7G8ZTA)와 배점 정책을 분리해 담는다.
            /// </summary>
            public HashSet<int> ForfeitedRounds { get; } = new();

            public RoundPlacement[] BuildPlacements()
            {
                var result = new RoundPlacement[Placements.Count];
                var i = 0;
                foreach (var pair in Placements)
                {
                    result[i++] = new RoundPlacement(pair.Key, pair.Value, ForfeitedRounds.Contains(pair.Key));
                }

                return result;
            }
        }

        private readonly Dictionary<int, Row> rows = new();
        private readonly HashSet<int> countedRounds = new();

        /// <summary>(라운드, 플레이어) → 그 라운드의 킬 점수 배율. 없으면 1 이다.</summary>
        private readonly Dictionary<(int Round, int PlayerId), float> killScoreMultipliers = new();

        /// <summary>
        /// 완결 커밋(<see cref="TryApplyCompleteRound"/>)으로 받아들인 라운드의 <b>영수증</b>.
        /// 라운드 번호 → 그때 받아들인 사실의 사본.
        ///
        /// <para>
        /// 재전송이 "같은 사실" 인지 말하려면 <b>무엇을 반영했는지</b> 가 남아 있어야 한다.
        /// 라운드 번호만 기억하면 서로 다른 배치가 같은 라운드로 두 번 올 때 구별할 수 없다.
        /// </para>
        /// </summary>
        private readonly Dictionary<int, RoundOutcome[]> strictReceipts = new();

        /// <summary>결과가 반영된 라운드 번호들. 중복 반영을 막는 근거다.</summary>
        public IReadOnlyCollection<int> CountedRounds => countedRounds;

        /// <summary>
        /// 그 라운드에서 이 플레이어의 킬 점수 배율을 정한다 — 저점 매수의 "이번 라운드 킬 점수 N배"(S-GD-TMPBEF).
        /// <b>그 라운드를 반영하기 전에</b> 불러야 한다. 이미 반영된 라운드에는 효과가 없다(재전송은 누계를 다시 만들지 않는다).
        /// </summary>
        /// <remarks>1 이하·NaN 은 배율 없음(1)으로 본다 — 킬 점수를 깎는 축복은 없다.</remarks>
        public void SetKillScoreMultiplier(int round, int playerId, float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 1f)
            {
                killScoreMultipliers.Remove((round, playerId));
                return;
            }

            killScoreMultipliers[(round, playerId)] = multiplier;
        }

        /// <summary>지금까지 집계된 플레이어 수.</summary>
        public int PlayerCount => rows.Count;

        /// <summary>
        /// 한 라운드의 전투 결과를 누적한다.
        ///
        /// <b>같은 라운드가 두 번 오면 두 번째는 무시한다</b> — 예외를 던지지 않는다.
        /// 재전송은 오류가 아니라 정상 동작이기 때문이다. 무시했는지는 반환값으로 안다.
        /// </summary>
        /// <param name="round">1부터 시작하는 라운드 번호.</param>
        /// <param name="results">
        /// 그 라운드의 참여자별 사실. <c>Data.Combat.CombatantRoundResult</c> 를
        /// 어댑터가 <see cref="RoundOutcome"/> 로 옮겨 넘긴다 —
        /// 경제 도메인이 전투 도메인을 참조하지 않게 하기 위해서다.
        /// </param>
        /// <returns>실제로 반영했으면 true. 이미 반영된 라운드라 건너뛰었으면 false.</returns>
        public bool TryApplyRound(int round, IReadOnlyList<RoundOutcome> results)
        {
            if (round <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(round), round, "라운드는 1부터 시작합니다.");
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (!countedRounds.Add(round))
            {
                return false;
            }

            Accumulate(round, results);
            return true;
        }

        /// <summary>
        /// 한 라운드의 <b>완결된</b> 결과를 커밋한다 (S-GD-JRKHQA 완결 발행은 한 번).
        ///
        /// <para>
        /// <see cref="TryApplyRound"/> 와의 차이는 <b>무엇을 받아들일지</b> 다. 옛 경로는 들어온 줄을
        /// 그대로 더한다 — 명부의 절반만 담긴 배치, 순위가 겹친 배치, 순위를 모르는 배치도 통과한다.
        /// 그건 옛 경계가 그런 배치밖에 만들 수 없던 시절의 계약이라 <b>바꾸지 않는다.</b>
        /// 이 경로는 Host 가 한 라운드를 완결해서 발행할 때만 쓴다. 그래서 더 엄하다 —
        /// 명부 전원이 정확히 한 줄씩, 순위는 1..N 유일(S-GD-7G8ZTA), 통계는 앞뒤가 맞아야 한다.
        /// </para>
        ///
        /// <para>
        /// <b>재전송</b>은 오류가 아니다. 단, "같은 사실" 만 통과시킨다 — 줄 순서는 달라도 되지만
        /// 한 필드라도(이탈 표시까지) 다르면 거절한다. 서로 다른 두 배치가 같은 라운드로 커밋되면
        /// 어느 쪽이 반영됐는지 아무도 모르는 상태가 되기 때문이다.
        /// 거절한 경우 <b>누적을 한 글자도 건드리지 않는다.</b>
        /// </para>
        /// </summary>
        /// <param name="round">1부터 시작하는 라운드 번호.</param>
        /// <param name="results">명부 전원의 라운드 사실. 순서는 상관없다.</param>
        /// <param name="expectedPlayerIds">
        /// 그 라운드의 시작 명부(S-GD-QJA6NY 의 고정된 N). 이탈자도 들어 있어야 한다.
        /// </param>
        /// <returns>
        /// 처음 커밋에 성공했거나 <b>같은 사실</b>의 재전송이면 true.
        /// 검증에 걸렸거나 이미 커밋된 라운드와 사실이 어긋나면 false.
        /// </returns>
        public bool TryApplyCompleteRound(
            int round,
            IReadOnlyList<RoundOutcome> results,
            IReadOnlyCollection<int> expectedPlayerIds)
        {
            if (round <= 0 || results == null || expectedPlayerIds == null)
            {
                return false;
            }

            // 검증은 전부 누적을 건드리기 전에 끝낸다 — 반쯤 반영된 라운드는 되돌릴 수 없다.
            if (!TryAcceptCompleteRound(results, expectedPlayerIds, out RoundOutcome[] accepted))
            {
                return false;
            }

            if (strictReceipts.TryGetValue(round, out RoundOutcome[] committed))
            {
                // 재전송. 같은 사실이면 조용히 통과시키고 두 번 더하지 않는다.
                return SameFacts(committed, accepted);
            }

            if (countedRounds.Contains(round))
            {
                // 옛 경로로 이미 반영된 라운드다. 무엇이 반영됐는지 증거가 없으니 "같은 사실" 이라고
                // 말할 수 없다 — 통과시키면 호출자는 자기 배치가 반영됐다고 믿는다.
                return false;
            }

            countedRounds.Add(round);
            Accumulate(round, accepted);

            // 사본을 둔다. 호출자가 넘긴 배열을 나중에 고쳐도 영수증은 그대로여야 한다.
            strictReceipts[round] = accepted;
            return true;
        }

        /// <summary>
        /// 완결 배치의 검증. 통과하면 <paramref name="accepted"/> 에 <b>내부 사본</b>을 담아 준다.
        /// 아무것도 변경하지 않는다.
        /// </summary>
        private static bool TryAcceptCompleteRound(
            IReadOnlyList<RoundOutcome> results,
            IReadOnlyCollection<int> expectedPlayerIds,
            out RoundOutcome[] accepted)
        {
            accepted = Array.Empty<RoundOutcome>();

            var roster = new HashSet<int>();
            foreach (int playerId in expectedPlayerIds)
            {
                if (!roster.Add(playerId))
                {
                    // 명부에 같은 사람이 두 번 있다 — N 이 몇인지부터 말이 안 된다.
                    return false;
                }
            }

            if (roster.Count == 0 || results.Count != roster.Count)
            {
                return false;
            }

            var seenPlayers = new HashSet<int>();
            var takenPlacements = new bool[roster.Count + 1];
            var copy = new RoundOutcome[results.Count];

            for (var i = 0; i < results.Count; i++)
            {
                RoundOutcome outcome = results[i];

                // 명부 밖 사람도, 두 줄로 들어온 사람도 받지 않는다.
                if (!roster.Contains(outcome.PlayerId) || !seenPlayers.Add(outcome.PlayerId))
                {
                    return false;
                }

                // 순위는 1..N 안에서 유일해야 한다 (S-GD-7G8ZTA). 0(순위 모름)도 완결이 아니다.
                if (outcome.Placement < 1
                    || outcome.Placement > roster.Count
                    || takenPlacements[outcome.Placement])
                {
                    return false;
                }

                takenPlacements[outcome.Placement] = true;

                // 기본값 구조체나 어댑터 버그로 생성자 검증을 우회한 줄을 여기서 다시 막는다.
                if (outcome.Kills < 0
                    || outcome.Assists < 0
                    || outcome.RangedAssists < 0
                    || outcome.RangedAssists > outcome.Assists)
                {
                    return false;
                }

                copy[i] = outcome;
            }

            // 명부 크기만큼 서로 다른 사람이 들어왔으므로 명부와 정확히 같은 집합이고,
            // 순위도 1..N 을 빠짐없이 한 번씩 쓴 것이 된다.
            accepted = copy;
            return true;
        }

        /// <summary>두 배치가 <b>줄 순서와 무관하게</b> 같은 사실인가.</summary>
        private static bool SameFacts(RoundOutcome[] committed, RoundOutcome[] candidate)
        {
            if (committed.Length != candidate.Length)
            {
                return false;
            }

            for (var i = 0; i < candidate.Length; i++)
            {
                var matched = false;
                for (var j = 0; j < committed.Length; j++)
                {
                    if (committed[j].PlayerId != candidate[i].PlayerId)
                    {
                        continue;
                    }

                    // 커밋된 배치도 사람마다 한 줄이므로 짝은 하나뿐이다.
                    matched = SameFact(committed[j], candidate[i]);
                    break;
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>한 줄의 <b>모든</b> 필드를 본다 — 이탈 표시(S-GD-QJA6NY)까지 사실의 일부다.</summary>
        private static bool SameFact(RoundOutcome a, RoundOutcome b) =>
            a.PlayerId == b.PlayerId
            && a.Kills == b.Kills
            && a.Assists == b.Assists
            && a.RangedAssists == b.RangedAssists
            && a.Survived == b.Survived
            && a.Placement == b.Placement
            && a.Forfeited == b.Forfeited;

        /// <summary>
        /// 라운드 하나를 실제로 누적한다. 중복 방지 판단은 <b>부르는 쪽</b>이 이미 끝냈다.
        /// 두 경로(<see cref="TryApplyRound"/>·<see cref="TryApplyCompleteRound"/>)가
        /// 같은 누적 규칙을 쓰게 하려고 한 곳에 둔다.
        /// </summary>
        private void Accumulate(int round, IReadOnlyList<RoundOutcome> results)
        {
            for (var i = 0; i < results.Count; i++)
            {
                var outcome = results[i];
                if (!rows.TryGetValue(outcome.PlayerId, out var row))
                {
                    row = new Row();
                    rows[outcome.PlayerId] = row;
                }

                row.Kills += outcome.Kills;
                row.ScoredKills += killScoreMultipliers.TryGetValue((round, outcome.PlayerId), out float multiplier)
                    ? (int)Math.Floor(outcome.Kills * (double)multiplier)
                    : outcome.Kills;
                row.Assists += outcome.Assists;
                row.RangedAssists += outcome.RangedAssists;
                row.CountedRounds++;

                if (outcome.Placement != RoundOutcome.UnknownPlacement)
                {
                    // 같은 라운드가 두 번 오는 경우는 부르는 쪽에서 이미 막혔다.
                    row.Placements[round] = outcome.Placement;
                    if (outcome.Forfeited)
                    {
                        // 이탈자도 유일한 표시 순위를 받지만 배점은 꼴등이다.
                        row.ForfeitedRounds.Add(round);
                    }
                }

                if (outcome.Survived)
                {
                    row.SurvivedRounds++;
                }
            }
        }

        /// <summary>한 플레이어의 누적. 아직 결과가 없으면 전부 0 인 항목을 돌려준다.</summary>
        public MatchTallyEntry GetEntry(int playerId) =>
            rows.TryGetValue(playerId, out var row)
                ? new MatchTallyEntry(
                    playerId, row.Kills, row.Assists, row.RangedAssists,
                    row.SurvivedRounds, row.CountedRounds, row.BuildPlacements(), row.ScoredKills)
                : new MatchTallyEntry(playerId, 0, 0, 0, 0, 0, Array.Empty<RoundPlacement>());

        /// <summary>
        /// 최종 순위를 정하는 쪽(<c>-35</c>)이 쓸 입력. <b>정렬돼 있지 않다</b> —
        /// 정렬 기준이 스펙에 없어서 여기서 정하지 않는다.
        /// </summary>
        public MatchTallyEntry[] BuildStandingInputs()
        {
            var result = new MatchTallyEntry[rows.Count];
            var i = 0;
            foreach (var pair in rows)
            {
                var row = pair.Value;
                result[i++] = new MatchTallyEntry(
                    pair.Key, row.Kills, row.Assists, row.RangedAssists,
                    row.SurvivedRounds, row.CountedRounds, row.BuildPlacements(), row.ScoredKills);
            }

            return result;
        }

        /// <summary>
        /// 새 매치를 시작한다 (S-GD-RFXF8X). 누적과 반영 기록, <b>완결 커밋 영수증</b>까지 모두 비운다.
        /// 영수증이 남으면 새 매치의 1라운드가 앞 매치의 사실과 비교돼 조용히 거절된다.
        /// </summary>
        public void ResetForNewMatch()
        {
            rows.Clear();
            countedRounds.Clear();
            strictReceipts.Clear();
            killScoreMultipliers.Clear();
        }

        public override string ToString() =>
            $"{rows.Count}명 / {countedRounds.Count}라운드 반영";
    }

    /// <summary>
    /// 한 플레이어의 라운드 전투 사실. <c>Data.Combat.CombatantRoundResult</c> 와 같은 모양이지만
    /// <b>일부러 따로 둔다</b> — 경제 도메인이 전투 도메인을 참조하기 시작하면 둘이 붙어버리고,
    /// 전투 계약이 바뀔 때마다 경제 테스트가 깨진다 (D-044 의 경계).
    /// 어댑터가 경계에서 옮긴다. <c>CombatantId.Value</c> 가 <see cref="PlayerId"/> 가 된다.
    /// </summary>
    /// <summary>한 라운드에서 한 플레이어가 받은 생존 순위.</summary>
    public readonly struct RoundPlacement
    {
        /// <summary>1부터 시작하는 라운드 번호.</summary>
        public readonly int Round;

        /// <summary>1등이 1인 그 라운드의 생존 순위. 공동 순위는 없다(S-GD-7G8ZTA).</summary>
        public readonly int Placement;

        /// <summary>
        /// 표시 순위와 별개로 <b>점수만 꼴등</b>으로 주는 라운드(S-GD-QJA6NY).
        /// 중도 이탈자가 여기에 해당한다.
        /// </summary>
        public readonly bool ScoredAsLastPlace;

        public RoundPlacement(int round, int placement)
            : this(round, placement, scoredAsLastPlace: false)
        {
        }

        public RoundPlacement(int round, int placement, bool scoredAsLastPlace)
        {
            Round = round;
            Placement = placement;
            ScoredAsLastPlace = scoredAsLastPlace;
        }

        public override string ToString() =>
            $"{Round}R {Placement}등{(ScoredAsLastPlace ? " (꼴등 배점)" : string.Empty)}";
    }

    public readonly struct RoundOutcome
    {
        /// <summary>순위를 모른다는 뜻. 점수 계산에서 <b>꼴등으로 취급</b>한다.</summary>
        public const int UnknownPlacement = 0;

        public readonly int PlayerId;
        public readonly int Kills;

        /// <summary>어시스트 <b>총합</b>. <see cref="RangedAssists"/> 를 포함한다.</summary>
        public readonly int Assists;

        /// <summary>
        /// 그중 <b>원거리 무기로 낸</b> 어시스트 (S-GD-P7SC3Q).
        /// 근접 어시스트는 <c>Assists - RangedAssists</c> 다.
        ///
        /// <para>
        /// ⚠️ 오늘은 근접·원거리 어시스트 점수가 둘 다 3점이라 <b>이 값이 결과를 바꾸지 않는다.</b>
        /// 그래도 경계가 나눠 나르는 이유는 나중에 원거리를 2점으로 내릴 때
        /// <c>Data/Combat/</c>(D-044 의 공유 계층)까지 다시 고치지 않기 위해서다.
        /// </para>
        /// </summary>
        public readonly int RangedAssists;

        public readonly bool Survived;

        /// <summary>
        /// 그 라운드의 생존 순위. 1등이 1이다. 모르면 <see cref="UnknownPlacement"/>.
        ///
        /// <para>
        /// ⚠️ <b>이 값이 최종 점수의 가장 큰 성분이다</b> (S-GD-P7SC3Q).
        /// 전투 경계가 <c>bool Survived</c> 만 주던 시절에는 계산이 불가능했다 (O-025).
        /// </para>
        /// </summary>
        public readonly int Placement;

        /// <summary>
        /// 중도 이탈 등으로 <b>배점만 꼴등</b>으로 처리해야 하는가(S-GD-QJA6NY).
        ///
        /// <para>
        /// S-GD-7G8ZTA 가 표시 순위의 유일성을 요구하면서 필요해진 사실이다. 예전에는 이탈자의
        /// <see cref="Placement"/> 가 <see cref="UnknownPlacement"/> 라 자동으로 꼴등 점수를 받았지만,
        /// 이제는 이탈자도 1..N 안의 유일한 순위를 받는다. 그 순위를 그대로 점수로 환산하면
        /// <b>이탈 배점 정책이 조용히 사라진다.</b> 그래서 순위와 배점을 나눠 나른다.
        /// </para>
        /// </summary>
        public readonly bool Forfeited;

        /// <summary>순위·어시스트 분류를 모르는 옛 경계용. 새 코드는 아래 생성자를 쓴다.</summary>
        public RoundOutcome(int playerId, int kills, int assists, bool survived)
            : this(playerId, kills, assists, rangedAssists: 0, survived, UnknownPlacement)
        {
        }

        public RoundOutcome(
            int playerId, int kills, int assists, int rangedAssists, bool survived, int placement)
            : this(playerId, kills, assists, rangedAssists, survived, placement, forfeited: false)
        {
        }

        public RoundOutcome(
            int playerId, int kills, int assists, int rangedAssists, bool survived, int placement,
            bool forfeited)
        {
            if (kills < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(kills), kills, "킬 수는 음수일 수 없습니다.");
            }

            if (assists < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(assists), assists, "어시스트 수는 음수일 수 없습니다.");
            }

            if (rangedAssists < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rangedAssists), rangedAssists, "원거리 어시스트 수는 음수일 수 없습니다.");
            }

            if (rangedAssists > assists)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rangedAssists), rangedAssists,
                    $"원거리 어시스트({rangedAssists})가 어시스트 총합({assists})보다 많을 수 없습니다.");
            }

            if (placement < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(placement), placement, "순위는 1부터 시작합니다. 모르면 0 을 넘깁니다.");
            }

            PlayerId = playerId;
            Kills = kills;
            Assists = assists;
            RangedAssists = rangedAssists;
            Survived = survived;
            Placement = placement;
            Forfeited = forfeited;
        }

        /// <summary>근접 무기로 낸 어시스트.</summary>
        public int MeleeAssists => Assists - RangedAssists;
    }
}
