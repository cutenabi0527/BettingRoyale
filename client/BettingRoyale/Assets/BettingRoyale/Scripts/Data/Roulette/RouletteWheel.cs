using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 룰렛 바퀴의 칸 배치와 배당 (S-GD-49WM7S).
    ///
    /// <para>
    /// <b>칸 배치가 곧 확률표다.</b> 25칸 중 하나를 균일하게 뽑고 그 칸에 적힌 숫자가 적중
    /// 숫자다. 별도의 확률 표를 두지 않는 이유는, 두 벌이 되면 한쪽만 고쳐서 조용히 어긋나기
    /// 때문이다.
    /// </para>
    ///
    /// <para>
    /// <b>이 클래스에는 일부러 <c>Default</c> 가 없다.</b> 확정값은 코드 상수가 아니라
    /// 데이터로 들어온다(D-041). 임시 배치는 <see cref="PlaceholderRouletteWheel"/> 에 있다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class RouletteWheel
    {
        private readonly int[] layout;
        private readonly Dictionary<int, int> slotCountByNumber;

        /// <param name="layout">
        /// 칸마다 적힌 숫자. 배열 길이가 칸 수다.
        /// <b>배치 순서는 화면이 회전을 그리는 데 쓰므로 그대로 보존한다.</b>
        /// </param>
        public RouletteWheel(IReadOnlyList<int> layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (layout.Count < 1)
            {
                throw new ArgumentException("바퀴에 칸이 하나도 없습니다.", nameof(layout));
            }

            this.layout = new int[layout.Count];
            slotCountByNumber = new Dictionary<int, int>();

            for (int i = 0; i < layout.Count; i++)
            {
                int number = layout[i];

                if (number < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(layout), number, $"칸 {i} 의 숫자가 1 보다 작습니다. 배당은 숫자 × (N+1) 이므로 1 이상이어야 합니다.");
                }

                this.layout[i] = number;
                slotCountByNumber.TryGetValue(number, out int count);
                slotCountByNumber[number] = count + 1;
            }

            RequireNoNumberBeatsTheHouse();
        }

        /// <summary>칸 수. 난수의 범위이자 확률의 분모다.</summary>
        public int SlotCount => layout.Length;

        /// <summary>걸 수 있는 숫자들. 배치에 실제로 있는 것만이다.</summary>
        public IReadOnlyCollection<int> Numbers => slotCountByNumber.Keys;

        /// <summary>칸 배치 그대로. 화면이 회전을 그리는 데 쓴다.</summary>
        public IReadOnlyList<int> Layout => layout;

        /// <summary>그 숫자에 걸 수 있는가.</summary>
        public bool Accepts(int number) => slotCountByNumber.ContainsKey(number);

        /// <summary>그 숫자가 적힌 칸 수. 0 이면 배치에 없는 숫자다.</summary>
        public int SlotsFor(int number) =>
            slotCountByNumber.TryGetValue(number, out int count) ? count : 0;

        /// <summary>
        /// 적중했을 때 <b>총 회수액</b>. 건 금액 × (숫자 + 1) 이다 —
        /// 건 금액의 <c>N</c> 배를 <b>추가로</b> 받으므로 되돌려받는 것까지 합쳐 (N+1) 배다.
        /// </summary>
        public static int PayoutFor(int number, int stake) => checked(stake * (number + 1));

        /// <summary>이 칸이 나왔을 때의 적중 숫자.</summary>
        public int NumberAt(int slot)
        {
            if (slot < 0 || slot >= layout.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "칸 번호가 범위를 벗어났습니다.");
            }

            return layout[slot];
        }

        /// <summary>
        /// <b>어떤 숫자도 기대 회수율 100% 를 넘지 못한다</b> (S-GD-49WM7S).
        ///
        /// <para>
        /// 넘는 숫자가 하나라도 있으면 <b>거기에만 걸어도 골드가 무한히 는다.</b>
        /// 수치가 데이터로 들어오므로(D-041) 사람이 잘못 넣을 수 있고, 그때 게임이
        /// 조용히 망가지는 대신 <b>만들 때 터지게</b> 한다.
        /// </para>
        ///
        /// <para>
        /// 정수로만 비교한다 — <c>칸수 × (숫자+1) &gt; 전체 칸수</c> 이면 기대값이 1 을 넘는다.
        /// 부동소수로 재면 경계에서 기기마다 결과가 갈릴 수 있다.
        /// </para>
        /// </summary>
        private void RequireNoNumberBeatsTheHouse()
        {
            foreach (var pair in slotCountByNumber)
            {
                long expected = (long)pair.Value * (pair.Key + 1);

                if (expected > layout.Length)
                {
                    throw new ArgumentException(
                        $"숫자 {pair.Key} 의 기대 회수율이 100% 를 넘습니다 " +
                        $"({pair.Value}칸 × {pair.Key + 1}배 = {expected} > {layout.Length}칸). " +
                        "그 숫자에만 걸어도 골드가 무한히 늘어납니다.",
                        nameof(layout));
                }
            }
        }

        public override string ToString() => $"룰렛 {layout.Length}칸 · 숫자 {slotCountByNumber.Count}종";
    }
}
