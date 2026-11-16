using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 추첨 규칙의 설정 모델 (S-GD-S46S28, S-GD-SHJGNB).
    /// 경제와 마찬가지로 수치를 코드에 박지 않고 이 객체를 통해서만 읽는다.
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class BlessingConfig
    {
        /// <summary>준비 단계마다 제시되는 후보 수 (S-GD-S46S28).</summary>
        public const int DefaultCandidateCount = 3;

        /// <summary>무료로 잠글 수 있는 최대 개수 (S-GD-S46S28).</summary>
        public const int DefaultMaxFreeLocks = 2;

        /// <summary>같은 준비 단계에서 재추첨할 때마다 비용에 곱해지는 배수 (S-GD-SHJGNB).</summary>
        public const int DefaultRerollCostMultiplier = 2;

        private readonly IReadOnlyList<int> rerollBaseCostByRound;
        private readonly IReadOnlyList<BlessingTier> tierByRound;

        public BlessingConfig(
            int candidateCount,
            int maxFreeLocks,
            IReadOnlyList<int> rerollBaseCostByRound,
            IReadOnlyList<BlessingTier> tierByRound,
            int rerollCostMultiplier)
        {
            if (candidateCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(candidateCount), candidateCount, "후보 수는 1 이상이어야 합니다.");
            }

            if (maxFreeLocks < 0 || maxFreeLocks >= candidateCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxFreeLocks), maxFreeLocks,
                    "잠금 가능 수는 0 이상이고 후보 수보다 적어야 합니다. 전부 잠그면 재추첨이 의미를 잃습니다.");
            }

            if (rerollCostMultiplier < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rerollCostMultiplier), rerollCostMultiplier, "재추첨 배수는 1 이상이어야 합니다.");
            }

            CandidateCount = candidateCount;
            MaxFreeLocks = maxFreeLocks;
            RerollCostMultiplier = rerollCostMultiplier;
            this.rerollBaseCostByRound = rerollBaseCostByRound
                ?? throw new ArgumentNullException(nameof(rerollBaseCostByRound));
            this.tierByRound = tierByRound
                ?? throw new ArgumentNullException(nameof(tierByRound));

            if (tierByRound.Count != rerollBaseCostByRound.Count)
            {
                // 둘 다 "라운드마다 하나씩" 인 표다. 길이가 다르면 어느 한쪽이 라운드를 빠뜨린 것이고,
                // 그 상태로 두면 재추첨 비용은 있는데 등급이 없는 라운드가 생긴다.
                throw new ArgumentException(
                    $"라운드별 표의 길이가 다릅니다 — 재추첨 비용 {rerollBaseCostByRound.Count}개, " +
                    $"등급 {tierByRound.Count}개. 둘 다 라운드 수와 같아야 합니다.",
                    nameof(tierByRound));
            }

            for (var i = 0; i < tierByRound.Count; i++)
            {
                if (tierByRound[i] < BlessingTier.Silver || tierByRound[i] > BlessingTier.Platinum)
                {
                    // default(BlessingTier) 는 0 이다. 등급을 빠뜨린 표가 여기서 잡힌다.
                    throw new ArgumentOutOfRangeException(
                        nameof(tierByRound), tierByRound[i],
                        $"{i + 1}라운드의 등급이 유효하지 않습니다.");
                }
            }
        }

        public int CandidateCount { get; }

        public int MaxFreeLocks { get; }

        public int RerollCostMultiplier { get; }

        /// <summary>재추첨 비용과 등급이 정의된 마지막 라운드 번호. 스펙상 3이다 (S-GD-6H2Q5Q).</summary>
        public int DefinedRoundCount => rerollBaseCostByRound.Count;

        /// <summary>
        /// 스펙 기본값 — 후보 3개 / 2개 잠금 / 100·200·300G / 2배 / 실버·골드·플래티넘.
        /// </summary>
        /// <summary>기획 데이터(constants.CANDIDATE_COUNT·FREE_LOCK_COUNT·REROLL_COST_R1~3·REROLL_COST_MULTIPLIER)의 설정. 없으면 코드 대역.</summary>
        public static BlessingConfig Default => BettingRoyale.Data.Balance.BalanceCatalogs.Current.Blessing;

        /// <summary>코드 대역 — 후보 3 · 잠금 2 · 재추첨 100/200/300 · ×2.</summary>
        public static BlessingConfig CreatePlaceholderDefault() => new(
            DefaultCandidateCount,
            DefaultMaxFreeLocks,
            new[] { 100, 200, 300 },
            new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum },
            DefaultRerollCostMultiplier);

        /// <summary>
        /// 이 라운드에 제시할 축복의 등급을 구한다 (S-GD-HE87JY).
        ///
        /// <b>등급은 뽑는 것이 아니라 라운드로 정해진다</b> — 1라운드 실버, 2라운드 골드, 3라운드 플래티넘.
        /// 뒤로 갈수록 강한 축복을 줘서 초반에 벌어진 격차를 뒤집을 여지를 남기는 것이 목적이다.
        /// 그래서 <b>등급별 등장 확률 같은 것은 없다.</b> 확률표를 찾고 있었다면 여기서 멈추면 된다.
        /// </summary>
        /// <param name="round">1부터 시작하는 라운드 번호.</param>
        /// <returns>
        /// 해당 라운드의 등급이 정의돼 있으면 true.
        /// 한 매치는 3라운드이므로(S-GD-6H2Q5Q) 정상 흐름에서는 항상 true 다.
        /// </returns>
        public bool TryGetTierForRound(int round, out BlessingTier tier)
        {
            if (round < 1 || round > tierByRound.Count)
            {
                tier = default;
                return false;
            }

            tier = tierByRound[round - 1];
            return true;
        }

        /// <summary>
        /// 재추첨 비용을 구한다 (S-GD-SHJGNB). 라운드별 최초 비용에 같은 준비 단계의 재추첨 횟수만큼 배수를 곱한다.
        /// </summary>
        /// <param name="round">1부터 시작하는 라운드 번호.</param>
        /// <param name="rerollIndexInRound">이번 준비 단계에서 이미 재추첨한 횟수(0 이면 첫 재추첨).</param>
        /// <param name="cost">계산된 비용.</param>
        /// <returns>
        /// 해당 라운드의 최초 비용이 정의돼 있으면 true.
        /// <b>정의되지 않은 라운드에서 false 를 돌려준다</b> — S-GD-SHJGNB 은 4라운드 이상의 비용을 정하지 않았다.
        /// 임의로 외삽하지 않는다.
        /// </returns>
        public bool TryGetRerollCost(int round, int rerollIndexInRound, out int cost)
        {
            cost = 0;

            if (round < 1 || round > rerollBaseCostByRound.Count)
            {
                return false;
            }

            if (rerollIndexInRound < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rerollIndexInRound), rerollIndexInRound, "재추첨 횟수는 음수일 수 없습니다.");
            }

            var value = (long)rerollBaseCostByRound[round - 1];
            for (var i = 0; i < rerollIndexInRound; i++)
            {
                value *= RerollCostMultiplier;
                if (value > int.MaxValue)
                {
                    // 배수가 계속 곱해지므로 20여 회면 int 를 넘는다. 조용히 넘치지 않게 막는다.
                    throw new InvalidOperationException(
                        $"재추첨 비용이 int 범위를 넘었습니다 (라운드 {round}, {rerollIndexInRound}회차).");
                }
            }

            cost = (int)value;
            return true;
        }
    }
}
