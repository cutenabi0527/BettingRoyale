namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 25칸 배치 (S-GD-49WM7S 확정).
    ///
    /// <para>
    /// <b>레퍼런스는 게임 Rust 의 밴딧캠프 룰렛이고 칸 수는 사용자가 확인했다</b>(2026-09-08).
    /// 다만 <b>값이 코드에 있는 것은 임시다</b> — 확정값은 데이터로 들어와야 한다(D-041).
    /// </para>
    ///
    /// <code>
    ///   숫자   칸    확률     회수    기대 회수율
    ///    1     12   48.0%   × 2      96.0%
    ///    3      6   24.0%   × 4      96.0%
    ///    5      4   16.0%   × 6      96.0%
    ///   10      2    8.0%   × 11     88.0%
    ///   20      1    4.0%   × 21     84.0%
    ///   합계   25
    /// </code>
    ///
    /// <para>
    /// <b>이 배분은 고른 것이 아니라 유일하다.</b> "어떤 숫자도 기대 회수율 100% 를 넘지
    /// 않는다" 는 제약만 걸면 각 숫자의 칸 수 상한이 <c>25/(N+1)</c> 로 정해지는데,
    /// 그 상한들의 합이 <b>정확히 25</b> 다(12+6+4+2+1). 그래서 모든 숫자가 상한을 그대로
    /// 쓸 수밖에 없고 다른 배분은 존재하지 않는다.
    /// <b>칸 수를 바꾸면 이 성질이 깨진다</b> — 이전의 46칸은 상한 합이 47 이라 한 칸을
    /// 임의로 깎아야 했다(S-GD-EJA6YE, superseded).
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class PlaceholderRouletteWheel
    {
        public const int SlotCount = 25;

        /// <summary>걸 수 있는 숫자. 배당은 각각 × (N+1) 이다.</summary>
        public static readonly int[] Numbers = { 1, 3, 5, 10, 20 };

        /// <summary>
        /// 칸 배치. <b>3D 에셋과 같은 순서다</b> — 상단(0°)에서 시계 방향이고
        /// <c>RusticRoulette_Slots.json</c> 의 <c>displayValue</c> 배열과 칸마다 일치한다
        /// (S-CLI-PCSBD2, 칸당 14.4°).
        ///
        /// <para>
        /// <b>순서를 모델과 공유하는 것은 의도다.</b> 적중 칸 번호가 곧 모델의 회전 각도
        /// (<c>칸 번호 × 360/25</c>)라서 화면이 멈춘 자리와 정산 결과가 어긋날 수 없다.
        /// 두 벌로 두면 한쪽만 고쳐서 조용히 어긋난다.
        /// </para>
        ///
        /// <para>⚠️ 확률은 순서가 아니라 <b>개수</b>가 정한다.</para>
        /// </summary>
        public static readonly int[] Layout =
        {
            1, 3, 1, 5, 1, 3, 1, 10, 1, 3, 1, 5, 1,
            3, 1, 20, 1, 3, 1, 5, 1, 3, 1, 10, 5,
        };

        public static RouletteWheel Create() => new RouletteWheel(Layout);
    }
}
