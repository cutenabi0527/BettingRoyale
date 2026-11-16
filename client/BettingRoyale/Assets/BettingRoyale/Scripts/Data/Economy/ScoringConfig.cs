using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Economy
{
    /// <summary>
    /// 최종 점수의 배점 (S-GD-P7SC3Q, D-075).
    ///
    /// <code>
    /// 최종 점수 = Σ(라운드별 생존 순위 점수) + (총 킬 × KillPoints) + Σ(어시스트 × 분류별 점수)
    /// </code>
    ///
    /// <para>
    /// <b>숫자를 코드에 상수로 박지 않는다.</b> <see cref="Default"/> 는 있지만 이것은
    /// "현재 확정된 값" 이지 불변식이 아니다 — 데이터 파이프라인이 붙으면 그쪽에서 주입한다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>어시스트 점수를 근접·원거리로 나눠 갖는다.</b> 오늘은 둘 다 3점이라 결과가 같지만,
    /// 기획이 *"나중에 밸런스 보고 원거리는 2점으로 내릴 수도 있다"* 고 했다. 값이 갈릴 때
    /// 경계를 다시 고치지 않도록 지금 자리를 만들어 둔다 (S-GD-P7SC3Q).
    /// </para>
    ///
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class ScoringConfig
    {
        /// <summary>배점표가 가질 수 있는 최소 인원. 2인 매치가 하한이다.</summary>
        public const int MinPlayerCount = 2;

        private readonly int[] placementPoints;

        public ScoringConfig(
            IReadOnlyList<int> placementPoints,
            int killPoints,
            int meleeAssistPoints,
            int rangedAssistPoints)
        {
            if (placementPoints == null)
            {
                throw new ArgumentNullException(nameof(placementPoints));
            }

            if (placementPoints.Count < MinPlayerCount)
            {
                throw new ArgumentException(
                    $"배점표는 최소 {MinPlayerCount}칸이어야 합니다 (받은 값 {placementPoints.Count}칸).",
                    nameof(placementPoints));
            }

            var copy = new int[placementPoints.Count];
            for (var i = 0; i < placementPoints.Count; i++)
            {
                if (placementPoints[i] < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(placementPoints), placementPoints[i], "순위 점수는 음수일 수 없습니다.");
                }

                // 1등이 꼴등보다 낮은 표는 순위를 뒤집으므로 조용히 받지 않는다.
                if (i > 0 && placementPoints[i] > placementPoints[i - 1])
                {
                    throw new ArgumentException(
                        $"배점표는 내림차순이어야 합니다 — {i}번째({placementPoints[i]})가 " +
                        $"{i - 1}번째({placementPoints[i - 1]})보다 큽니다.",
                        nameof(placementPoints));
                }

                copy[i] = placementPoints[i];
            }

            if (killPoints < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(killPoints), killPoints, "킬 점수는 음수일 수 없습니다.");
            }

            if (meleeAssistPoints < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(meleeAssistPoints), meleeAssistPoints, "어시스트 점수는 음수일 수 없습니다.");
            }

            if (rangedAssistPoints < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rangedAssistPoints), rangedAssistPoints, "어시스트 점수는 음수일 수 없습니다.");
            }

            this.placementPoints = copy;
            KillPoints = killPoints;
            MeleeAssistPoints = meleeAssistPoints;
            RangedAssistPoints = rangedAssistPoints;
        }

        /// <summary>
        /// 확정값 (S-GD-P7SC3Q) — 생존 80·65·50·40·35·30·25·20 / 킬 10 / 어시스트 3.
        /// 어시스트는 근접·원거리가 아직 같은 값이다.
        /// </summary>
        /// <summary>기획 데이터(constants.SCORE_SURVIVAL·SCORE_KILL·SCORE_ASSIST)의 배점. 없으면 코드 대역.</summary>
        public static ScoringConfig Default => BettingRoyale.Data.Balance.BalanceCatalogs.Current.Scoring;

        /// <summary>코드 대역 — 80·65·50·40·35·30·25·20 / 킬 10 / 어시스트 3 (D-070).</summary>
        public static ScoringConfig CreatePlaceholderDefault() => new ScoringConfig(
            new[] { 80, 65, 50, 40, 35, 30, 25, 20 },
            killPoints: 10,
            meleeAssistPoints: 3,
            rangedAssistPoints: 3);

        /// <summary>배점표 전체. 8인 기준 8칸이다.</summary>
        public IReadOnlyList<int> PlacementPoints => placementPoints;

        /// <summary>이 표가 감당할 수 있는 최대 인원.</summary>
        public int MaxPlayerCount => placementPoints.Length;

        public int KillPoints { get; }

        public int MeleeAssistPoints { get; }

        public int RangedAssistPoints { get; }

        /// <summary>
        /// 한 라운드의 순위 점수를 구한다.
        ///
        /// <para>
        /// <b>인원이 표보다 적으면 표를 앞에서 자른다</b> (S-GD-P7SC3Q, O-024 를 닫은 결정).
        /// 4인 매치는 80·65·50·40 을 쓴다. 뒤에서 자르거나 선형으로 다시 만들지 않는다 —
        /// 그러면 "1·2·3등 격차를 크게" 라는 곡선 의도가 사라진다.
        /// </para>
        ///
        /// <para>
        /// 점수는 매치 밖으로 나가지 않으므로(서버는 <c>rank</c> 만 받는다) <b>절대값은 무의미하고
        /// 참가자 간 차이만 의미가 있다.</b> 앞에서 자르면 남은 간격이 전부 보존된다.
        /// </para>
        /// </summary>
        /// <param name="placement">1부터 시작하는 그 라운드의 생존 순위.</param>
        /// <param name="playerCount">매치 시작 계정 수 (S-GD-QJA6NY). 매치 내내 고정이다.</param>
        public bool TryGetPlacementPoints(int placement, int playerCount, out int points)
        {
            points = 0;

            if (playerCount < MinPlayerCount || playerCount > placementPoints.Length)
            {
                return false;
            }

            if (placement < 1 || placement > playerCount)
            {
                return false;
            }

            points = placementPoints[placement - 1];
            return true;
        }

        /// <summary>
        /// 이탈자가 받는 점수 (S-GD-QJA6NY) — 그 표의 꼴등 점수다.
        /// 8인이면 20점이고, 4인이면 40점이다.
        /// </summary>
        public bool TryGetLastPlacePoints(int playerCount, out int points) =>
            TryGetPlacementPoints(playerCount, playerCount, out points);

        /// <summary>어시스트 점수. 분류를 모르면 근접 값을 쓴다 — 둘이 같은 동안은 차이가 없다.</summary>
        public int AssistPointsFor(bool isRanged) => isRanged ? RangedAssistPoints : MeleeAssistPoints;

        public override string ToString() =>
            $"순위 {placementPoints.Length}칸(최대 {placementPoints[0]}) / 킬 {KillPoints} / " +
            $"어시 근접 {MeleeAssistPoints}·원거리 {RangedAssistPoints}";
    }
}
