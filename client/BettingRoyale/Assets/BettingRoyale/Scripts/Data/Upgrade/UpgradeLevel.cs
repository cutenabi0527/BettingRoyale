using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화 한 단계 (S-GD-A2YDDJ). 기획서 「강화 스펙」 표의 한 줄이다.
    ///
    /// <code>
    /// | 단계 | 공격력 | 방어력 관통 | 공격 속도 |
    /// |   3  |   30   |      1      |   +15%    |   ← 검
    /// </code>
    ///
    /// <para>
    /// <b>⚠️ 스탯 값은 0강 대비 <u>누적</u> 증가분이다.</b> 단계별 증분이 아니다 —
    /// 3강의 값은 "2강에서 얼마나 더" 가 아니라 "0강에서 얼마나" 다. 그래서 <b>단계가 오를 때
    /// 이전 단계 기여를 걷어내고 새로 얹는다.</b> 더하면 값이 세 배가 된다.
    /// </para>
    ///
    /// <para>
    /// 스탯 키는 축복 정본 <c>stats</c> 시트와 같은 id 를 쓴다. 강화는 별도 계산 체계가 아니라
    /// <b>같은 스탯 시트에 기여를 얹는 또 하나의 공급원</b>이다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class UpgradeLevel
    {
        private readonly StatContribution[] contributions;

        /// <param name="level">1부터 시작하는 단계. 0강은 표에 없다.</param>
        /// <param name="costGold">이 단계로 올리는 데 드는 골드.</param>
        /// <param name="stats">0강 대비 누적 증가분. 스탯 키는 축복과 같은 id 를 쓴다.</param>
        /// <remarks>성공률을 받지 않는 형태는 <b>100% 표</b>다 — 확률이 없던 시절의 표·테스트 픽스처용이다.</remarks>
        public UpgradeLevel(int level, int costGold, IReadOnlyList<StatContribution> stats)
            : this(level, costGold, stats, MaxSuccessRatePercent)
        {
        }

        /// <param name="level">1부터 시작하는 단계. 0강은 표에 없다.</param>
        /// <param name="costGold">이 단계로 올리는 데 드는 골드.</param>
        /// <param name="stats">0강 대비 누적 증가분. 스탯 키는 축복과 같은 id 를 쓴다.</param>
        /// <param name="successRatePercent">
        /// 직전 단계에서 <b>이 단계로</b> 올리는 시도의 성공률(0~100) — 엑셀 <c>upgrade_levels.success_rate</c>
        /// (S15P21A606-304). 실패하면 비용만 나가고 단계는 그대로다(S-GD-3PGNS5 · S-GD-A2YDDJ).
        /// </param>
        public UpgradeLevel(int level, int costGold, IReadOnlyList<StatContribution> stats, int successRatePercent)
        {
            if (level < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "강화 단계는 1부터입니다.");
            }

            if (costGold < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(costGold), costGold, "비용은 음수일 수 없습니다.");
            }

            if (successRatePercent < 0 || successRatePercent > MaxSuccessRatePercent)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(successRatePercent), successRatePercent, "성공률은 0~100 입니다.");
            }

            Level = level;
            CostGold = costGold;
            SuccessRatePercent = successRatePercent;

            if (stats == null || stats.Count == 0)
            {
                contributions = Array.Empty<StatContribution>();
                return;
            }

            contributions = new StatContribution[stats.Count];

            for (int i = 0; i < stats.Count; i++)
            {
                contributions[i] = stats[i];
            }
        }

        public int Level { get; }

        public int CostGold { get; }

        /// <summary>성공률의 상한. 이 값이면 굴리지 않고 성공한다.</summary>
        public const int MaxSuccessRatePercent = 100;

        /// <summary>
        /// 직전 단계에서 이 단계로 올리는 시도의 성공률(0~100). <b>판정은 Host 만 한다</b> —
        /// <see cref="UpgradeState.TryUpgrade"/> 가 주입받은 난수로 굴린다.
        /// </summary>
        public int SuccessRatePercent { get; }

        /// <summary>
        /// 표의 비율(0~1)을 백분율로 옮긴다. <c>0.9f * 100</c> 이 <c>89.99…</c> 가 되는 부동소수 오차를
        /// 버림으로 자르면 90% 가 89% 가 되므로 <b>반올림</b>한다.
        /// </summary>
        public static int PercentFromRatio(float ratio)
        {
            if (float.IsNaN(ratio) || ratio <= 0f)
            {
                return 0;
            }

            if (ratio >= 1f)
            {
                return MaxSuccessRatePercent;
            }

            return (int)Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>이 단계의 스탯 기여. <b>0강 대비 누적값</b>이다. <c>null</c> 이 아니다.</summary>
        public IReadOnlyList<StatContribution> Stats => contributions;

        public override string ToString() =>
            $"{Level}강 · {CostGold}G · 성공률 {SuccessRatePercent}% · 스탯 {contributions.Length}종";
    }

    /// <summary>
    /// 한 부위(또는 한 무기 종류)의 강화 표 전체 (S-GD-A2YDDJ).
    ///
    /// <para>
    /// <b>여기에 수치가 없다.</b> 단계 목록을 받아 들고 있을 뿐이고 값은 <c>upgrades.json</c> 이
    /// 준다(D-041). 임시 표는 <c>Placeholder*</c> 에 격리한다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class UpgradeTrack
    {
        private readonly UpgradeLevel[] levels;

        public UpgradeTrack(IReadOnlyList<UpgradeLevel> levels)
        {
            if (levels == null)
            {
                throw new ArgumentNullException(nameof(levels));
            }

            this.levels = new UpgradeLevel[levels.Count];

            for (int i = 0; i < levels.Count; i++)
            {
                var level = levels[i];

                if (level == null)
                {
                    throw new ArgumentException("강화 단계에 null 이 있습니다.", nameof(levels));
                }

                // 표의 순서가 곧 단계다. 어긋나면 "3강을 샀는데 2강 값이 붙는" 일이 조용히 생긴다.
                if (level.Level != i + 1)
                {
                    throw new ArgumentException(
                        $"강화 표가 1강부터 순서대로여야 합니다. {i} 번째가 {level.Level}강입니다.",
                        nameof(levels));
                }

                this.levels[i] = level;
            }
        }

        /// <summary>최대 단계. 0강에서 여기까지 올릴 수 있다.</summary>
        public int MaxLevel => levels.Length;

        /// <summary>그 단계의 표. <b>0강이나 범위 밖이면 <c>false</c></b>.</summary>
        public bool TryGet(int level, out UpgradeLevel data)
        {
            if (level < 1 || level > levels.Length)
            {
                data = null;
                return false;
            }

            data = levels[level - 1];
            return true;
        }

        /// <summary>0강에서 <paramref name="targetLevel"/> 까지 올리는 데 든 골드의 합. 화면이 예산을 보이는 데 쓴다.</summary>
        public int CumulativeCost(int targetLevel)
        {
            int total = 0;

            for (int i = 0; i < levels.Length && i < targetLevel; i++)
            {
                total = checked(total + levels[i].CostGold);
            }

            return total;
        }

        public override string ToString() => $"강화 표 최대 {levels.Length}강";
    }
}
