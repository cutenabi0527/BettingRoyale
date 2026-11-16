using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Economy
{
    /// <summary>
    /// 최종 순위를 정하는 데 필요한 한 플레이어의 사실 전부.
    /// 전투(<see cref="MatchTallyEntry"/>)와 경제(골드)가 여기서 합쳐진다.
    /// </summary>
    public readonly struct PlayerMatchFacts
    {
        public readonly int PlayerId;
        public readonly MatchTallyEntry Tally;

        /// <summary>매치 종료 시 남은 골드. 동점 처리 4단계 (S-GD-MNFBV7).</summary>
        public readonly int GoldRemaining;

        /// <summary>매치 동안 들어온 골드 누계. 동점 처리 5단계. 쓴다고 줄지 않는다.</summary>
        public readonly int GoldEarnedTotal;

        /// <summary>
        /// 대기실에 들어온 순서. 동점 처리 <b>마지막 9단계</b> (S-GD-MQJPFK).
        /// Host 가 매치 시작 때 확정한다. <b>먼저 들어온 쪽(작은 값)이 앞선다.</b>
        /// 아직 확정되지 않았으면 <see cref="UnassignedJoinOrder"/> 다.
        ///
        /// <para>
        /// ⚠️ <b><see cref="PlayerId"/> 로 대신하지 않는다.</b> Photon 이 참가 순서대로 주긴 하지만
        /// <c>docs/client/identity-contract.md</c> 가 <b>번호 재사용을 가정</b>한다 — 나간 사람의
        /// 번호가 다음 참가자에게 가면 늦게 온 사람이 "가장 먼저 들어온 사람" 이 된다.
        /// </para>
        /// </summary>
        public readonly int LobbyJoinOrder;

        /// <summary>접속 순서를 아직 확정하지 않았다는 뜻. 순서는 1부터라 0 과 충돌하지 않는다.</summary>
        public const int UnassignedJoinOrder = 0;

        public PlayerMatchFacts(int playerId, MatchTallyEntry tally, int goldRemaining, int goldEarnedTotal)
            : this(playerId, tally, goldRemaining, goldEarnedTotal, UnassignedJoinOrder)
        {
        }

        public PlayerMatchFacts(
            int playerId, MatchTallyEntry tally, int goldRemaining, int goldEarnedTotal, int lobbyJoinOrder)
        {
            if (goldRemaining < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(goldRemaining), goldRemaining, "잔액은 음수일 수 없습니다.");
            }

            if (goldEarnedTotal < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(goldEarnedTotal), goldEarnedTotal, "누계는 음수일 수 없습니다.");
            }

            if (lobbyJoinOrder < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lobbyJoinOrder), lobbyJoinOrder,
                    "대기실 접속 순서는 음수일 수 없습니다. 미확정은 0 입니다.");
            }

            PlayerId = playerId;
            Tally = tally;
            GoldRemaining = goldRemaining;
            GoldEarnedTotal = goldEarnedTotal;
            LobbyJoinOrder = lobbyJoinOrder;
        }
    }

    /// <summary>한 플레이어의 최종 결과. <see cref="Rank"/> 가 서버에 제출되는 값이다.</summary>
    public readonly struct FinalStanding
    {
        /// <summary>1부터 시작하는 최종 순위. <c>docs/server/api.md</c> 의 <c>rank</c> 가 이 값이다.</summary>
        public readonly int Rank;

        public readonly int PlayerId;

        /// <summary>최종 점수. <b>매치 밖으로 나가지 않는다</b> — 순위를 정하는 데만 쓴다.</summary>
        public readonly int Score;

        /// <summary>순위 점수만. 화면이 성분을 분해해 보여줄 때 쓴다.</summary>
        public readonly int PlacementScore;

        public readonly int KillScore;
        public readonly int AssistScore;

        public readonly int Kills;
        public readonly int Assists;

        internal FinalStanding(
            int rank, int playerId, int placementScore, int killScore, int assistScore, int kills, int assists)
        {
            Rank = rank;
            PlayerId = playerId;
            PlacementScore = placementScore;
            KillScore = killScore;
            AssistScore = assistScore;
            Score = placementScore + killScore + assistScore;
            Kills = kills;
            Assists = assists;
        }

        public override string ToString() =>
            $"{Rank}위 P{PlayerId} {Score}점 (순위 {PlacementScore} + 킬 {KillScore} + 어시 {AssistScore})";
    }

    /// <summary>
    /// 라운드별 점수 배분과 최종 등수를 정한다 (Jira S15P21A606-31 완료 조건 6).
    ///
    /// <para>
    /// <see cref="MatchRoundTally"/> 가 *"O-023 이 닫히면 그때 정렬을 붙인다"* 로 남겨둔 자리다.
    /// <b>O-023 은 D-070 으로, O-024 는 D-075 로 닫혔다.</b> 배점은
    /// <c>S-GD-P7SC3Q</c>, 동점 처리는 <c>S-GD-MNFBV7</c> 다.
    /// </para>
    ///
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </para>
    /// </summary>
    public static class FinalStandings
    {
        /// <summary>
        /// 최종 순위를 매긴다. 반환 배열은 <b>1위부터 정렬</b>돼 있고 <c>Rank</c> 가 채워져 있다.
        /// </summary>
        /// <param name="facts">참가자 전원. <b>이탈자도 포함한다</b> (S-GD-QJA6NY).</param>
        /// <param name="config">배점. <c>null</c> 이면 <see cref="ScoringConfig.Default"/>.</param>
        /// <param name="playerCount">
        /// 매치 시작 계정 수 (S-GD-QJA6NY). 배점표를 앞에서 이만큼 자른다.
        /// <c>facts.Count</c> 와 다를 수 있다 — 이탈자가 결과에서 빠진 경우가 그렇다.
        /// </param>
        public static FinalStanding[] Resolve(
            IReadOnlyList<PlayerMatchFacts> facts, ScoringConfig config, int playerCount)
        {
            if (facts == null)
            {
                throw new ArgumentNullException(nameof(facts));
            }

            config ??= ScoringConfig.Default;

            if (playerCount < ScoringConfig.MinPlayerCount || playerCount > config.MaxPlayerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerCount), playerCount,
                    $"인원은 {ScoringConfig.MinPlayerCount}~{config.MaxPlayerCount} 이어야 합니다. " +
                    "이 값은 매치 시작 계정 수이고 매치 내내 고정입니다 (S-GD-QJA6NY).");
            }

            var scored = new Scored[facts.Count];
            for (var i = 0; i < facts.Count; i++)
            {
                scored[i] = Score(facts[i], config, playerCount);
            }

            Array.Sort(scored, Compare);

            var result = new FinalStanding[scored.Length];
            for (var i = 0; i < scored.Length; i++)
            {
                var s = scored[i];
                result[i] = new FinalStanding(
                    rank: i + 1,
                    playerId: s.Facts.PlayerId,
                    placementScore: s.PlacementScore,
                    killScore: s.KillScore,
                    assistScore: s.AssistScore,
                    kills: s.Facts.Tally.Kills,
                    assists: s.Facts.Tally.Assists);
            }

            return result;
        }

        /// <summary>
        /// 한 플레이어의 점수를 성분별로 계산한다.
        ///
        /// <para>
        /// <b>순위 점수는 라운드마다 붙는다</b> — 3라운드면 세 번 더해진다 (S-GD-P7SC3Q).
        /// 순위를 모르는 라운드(<see cref="RoundOutcome.UnknownPlacement"/>)는 <b>꼴등 점수</b>를 준다.
        /// 이탈자에게 남은 라운드마다 꼴등 점수를 주는 규칙(S-GD-QJA6NY)이 여기서 자동으로 성립한다 —
        /// 이탈자의 그 라운드 결과가 안 왔거나 순위가 없기 때문이다.
        /// </para>
        /// </summary>
        private static Scored Score(PlayerMatchFacts facts, ScoringConfig config, int playerCount)
        {
            config.TryGetLastPlacePoints(playerCount, out var lastPlacePoints);

            var placementScore = 0;
            var placements = facts.Tally.Placements;
            var placedRounds = 0;

            for (var i = 0; i < placements.Count; i++)
            {
                if (placements[i].ScoredAsLastPlace)
                {
                    // 이탈자는 유일한 표시 순위를 받아도 배점은 꼴등이다 (S-GD-QJA6NY / S-GD-7G8ZTA).
                    placementScore += lastPlacePoints;
                    placedRounds++;
                }
                else if (config.TryGetPlacementPoints(placements[i].Placement, playerCount, out var points))
                {
                    placementScore += points;
                    placedRounds++;
                }
                else
                {
                    // 인원 밖 순위가 들어온 경우다. 조용히 0 을 주면 원인을 못 찾으므로 꼴등으로 본다.
                    placementScore += lastPlacePoints;
                    placedRounds++;
                }
            }

            // 결과가 왔지만 순위가 없던 라운드 — 이탈·환경 사망 등. 꼴등 점수를 준다.
            var unplacedRounds = facts.Tally.CountedRounds - placedRounds;
            if (unplacedRounds > 0)
            {
                placementScore += lastPlacePoints * unplacedRounds;
            }

            // 킬 점수는 라운드별 배율(저점 매수)을 곱한 킬 수로 매긴다(S-GD-TMPBEF). 동점 처리는 실제 킬 수를 본다.
            var killScore = facts.Tally.ScoredKills * config.KillPoints;
            var assistScore =
                facts.Tally.MeleeAssists * config.MeleeAssistPoints +
                facts.Tally.RangedAssists * config.RangedAssistPoints;

            return new Scored(facts, placementScore, killScore, assistScore);
        }

        /// <summary>
        /// 동점 처리 9단계 (S-GD-MNFBV7). 앞 단계에서 갈리면 뒤는 보지 않는다.
        ///
        /// <list type="number">
        /// <item>최종 점수 높은 순</item>
        /// <item>킬 많은 순</item>
        /// <item>어시스트 많은 순</item>
        /// <item>매치 종료 시 남은 골드 많은 순</item>
        /// <item>매치 동안 모은 골드 누계 많은 순</item>
        /// <item>마지막 라운드 순위 높은 순 (그 다음 라운드로 거슬러 올라간다)</item>
        /// <item><b>대기실에 먼저 들어온 순</b> (S-GD-MQJPFK)</item>
        /// </list>
        ///
        /// <para>
        /// 접속 순서는 매치 안에서 유일하므로 <b>마지막 단계에서 반드시 갈린다 — 공동 순위는 없다.</b>
        /// 서버가 <c>rank</c> 를 유일하게 요구하기 때문이다(<c>docs/server/api.md</c>).
        /// ⚠️ <b>라운드 안 동시 탈락도 이제 공동 순위가 없다</b> — S-GD-7G8ZTA 가 S-GD-P8V682 의
        /// 공동 순위 조항을 대체했고, 같은 권위 틱은 킬 → 어시스트 → 실제 적 피해 → 입장 순서로
        /// 유일하게 갈린다(<c>RoundPlacementResolver</c>).
        /// </para>
        ///
        /// <para>
        /// 다만 Host 가 아직 접속 순서를 실어 나르지 않으므로
        /// (<see cref="PlayerMatchFacts.UnassignedJoinOrder"/>) 지금은 그 단계를 건너뛰고
        /// <c>PlayerId</c> 오름차순으로 떨어진다. 결과는 대개 같지만 <c>PlayerId</c> 는
        /// <b>재사용될 수 있어 접속 순서를 보장하지 않는다.</b>
        /// <b>그 fallback 은 스펙이 아니라 결정론을 위한 임시 처리다</b> — Host Mode 에서 모든 피어가
        /// 같은 순위를 봐야 하므로 무작위나 순서 없는 정렬을 쓸 수 없다.
        /// </para>
        /// </summary>
        private static int Compare(Scored left, Scored right)
        {
            var byScore = right.Total.CompareTo(left.Total);
            if (byScore != 0)
            {
                return byScore;
            }

            var byKills = right.Facts.Tally.Kills.CompareTo(left.Facts.Tally.Kills);
            if (byKills != 0)
            {
                return byKills;
            }

            var byAssists = right.Facts.Tally.Assists.CompareTo(left.Facts.Tally.Assists);
            if (byAssists != 0)
            {
                return byAssists;
            }

            var byRemaining = right.Facts.GoldRemaining.CompareTo(left.Facts.GoldRemaining);
            if (byRemaining != 0)
            {
                return byRemaining;
            }

            var byEarned = right.Facts.GoldEarnedTotal.CompareTo(left.Facts.GoldEarnedTotal);
            if (byEarned != 0)
            {
                return byEarned;
            }

            var byRounds = CompareRoundsFromLast(left.Facts.Tally.Placements, right.Facts.Tally.Placements);
            if (byRounds != 0)
            {
                return byRounds;
            }

            // 9단계 — 대기실에 먼저 들어온 순 (S-GD-MNFBV7 / S-GD-MQJPFK).
            // 접속 순서는 매치 안에서 유일하므로, 둘 다 확정돼 있으면 여기서 반드시 갈린다.
            // 한쪽만 배정된 과도기에는 반쪽 데이터로 가르지 않고 건너뛴다.
            if (left.Facts.LobbyJoinOrder != PlayerMatchFacts.UnassignedJoinOrder
                && right.Facts.LobbyJoinOrder != PlayerMatchFacts.UnassignedJoinOrder)
            {
                var byJoinOrder = left.Facts.LobbyJoinOrder.CompareTo(right.Facts.LobbyJoinOrder);
                if (byJoinOrder != 0)
                {
                    return byJoinOrder;
                }
            }

            // 접속 순서가 없어 여기까지 온 경우. <b>스펙이 아니라</b> 정렬을 결정론적으로 만들기
            // 위한 임시 처리다 — Host 가 순서를 실어 보내기 시작하면 위에서 갈린다.
            return left.Facts.PlayerId.CompareTo(right.Facts.PlayerId);
        }

        /// <summary>
        /// 라운드 순위를 <b>늦은 라운드부터</b> 비교한다 (6·7·8단계).
        /// 순위는 낮을수록 좋다. 한쪽만 그 라운드 순위가 없으면 <b>있는 쪽이 앞선다.</b>
        /// </summary>
        private static int CompareRoundsFromLast(
            IReadOnlyList<RoundPlacement> left, IReadOnlyList<RoundPlacement> right)
        {
            var maxRound = 0;
            for (var i = 0; i < left.Count; i++)
            {
                if (left[i].Round > maxRound) maxRound = left[i].Round;
            }

            for (var i = 0; i < right.Count; i++)
            {
                if (right[i].Round > maxRound) maxRound = right[i].Round;
            }

            for (var round = maxRound; round >= 1; round--)
            {
                var hasLeft = TryFindPlacement(left, round, out var l);
                var hasRight = TryFindPlacement(right, round, out var r);

                if (!hasLeft && !hasRight)
                {
                    continue;
                }

                if (hasLeft != hasRight)
                {
                    return hasLeft ? -1 : 1;
                }

                if (l != r)
                {
                    return l.CompareTo(r);
                }
            }

            return 0;
        }

        private static bool TryFindPlacement(IReadOnlyList<RoundPlacement> placements, int round, out int placement)
        {
            for (var i = 0; i < placements.Count; i++)
            {
                if (placements[i].Round == round)
                {
                    placement = placements[i].Placement;
                    return true;
                }
            }

            placement = 0;
            return false;
        }

        private readonly struct Scored
        {
            public readonly PlayerMatchFacts Facts;
            public readonly int PlacementScore;
            public readonly int KillScore;
            public readonly int AssistScore;

            public Scored(PlayerMatchFacts facts, int placementScore, int killScore, int assistScore)
            {
                Facts = facts;
                PlacementScore = placementScore;
                KillScore = killScore;
                AssistScore = assistScore;
            }

            public int Total => PlacementScore + KillScore + AssistScore;
        }
    }
}
