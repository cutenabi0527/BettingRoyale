using System;

namespace BettingRoyale.Data.Roulette
{
    /// <summary>요청 한 칸에 실려 온 것이 무엇인가.</summary>
    public enum RouletteRequestKind
    {
        /// <summary>이번 판에 건 것을 전부 물린다. 숫자도 금액도 없다.</summary>
        Withdraw = 0,

        /// <summary>숫자 하나에 금액을 건다.</summary>
        Bet = 1,
    }

    /// <summary>
    /// <b>룰렛 요청 두 값을 정수 한 칸에 눌러 담는다</b> (S-GD-49WM7S, S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>왜 필요한가</b>: 경제 요청 전송 구조(<c>EconomyCommandRequest</c>)가 나르는 값은
    /// <c>int</c> <b>한 칸뿐</b>이다. 축복은 후보 번호 하나면 됐지만 룰렛은
    /// <b>"어떤 숫자에" + "얼마를"</b> 두 값이 필요하다.
    /// </para>
    ///
    /// <para>
    /// <b>왜 구조체를 늘리지 않는가</b>: 그 파일은 축1(<c>Scripts/Network/</c>)이 만진다.
    /// 칸을 늘리면 프로토콜 버전이 올라가고 <b>버전이 안 맞는 피어는 요청을 조용히 전부 버린다</b>
    /// — 양쪽 빌드를 동시에 갈아야 한다. 눌러 담으면 그 비용 없이
    /// <c>EconomyCommandKind</c> 값 하나만 추가하면 된다.
    /// </para>
    ///
    /// <code>
    ///  31                     5   4       0
    ///  ┌──────────────────────┬────────────┐
    ///  │        금액 (26bit)   │ 숫자 (5bit) │
    ///  └──────────────────────┴────────────┘
    ///     숫자 0 = 물리기 (건 숫자는 1 이상이므로 겹치지 않는다)
    /// </code>
    ///
    /// <para>
    /// <b>비트 장난을 여기 한 곳에 가둔다.</b> 어댑터도 화면도 이 규칙을 몰라야 한다 —
    /// 두 군데서 풀면 한쪽만 고쳐서 조용히 어긋난다. 그래서 이 클래스에 테스트가 붙어 있다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class RouletteRequestPayload
    {
        /// <summary>숫자에 쓰는 하위 비트 수.</summary>
        public const int NumberBits = 5;

        /// <summary>담을 수 있는 가장 큰 숫자. 확정 배치의 최대값은 20 이라 여유가 있다.</summary>
        public const int MaxNumber = (1 << NumberBits) - 1;

        /// <summary>
        /// 담을 수 있는 가장 큰 금액 (67,108,863).
        ///
        /// <b>부호 비트는 쓰지 않는다</b> — 음수가 되면 받는 쪽이 금액을 음수로 읽어
        /// <b>거는 순간 골드가 늘어난다.</b> 그래서 상한을 넘으면 담지 않고 던진다.
        /// </summary>
        public const int MaxAmount = int.MaxValue >> NumberBits;

        /// <summary>숫자 자리에 0 이 들어오면 "물리기" 다. 걸 수 있는 숫자는 1 이상이다.</summary>
        private const int WithdrawNumber = 0;

        /// <summary>
        /// 베팅 요청을 한 칸으로 만든다.
        ///
        /// <b>담을 수 없는 값이면 던진다.</b> 조용히 잘라내면 사용자가 건 금액과 실제로 걸린
        /// 금액이 달라지는데, 그것은 화면에도 로그에도 안 남는다.
        /// </summary>
        public static int PackBet(int number, int amount)
        {
            if (number < 1 || number > MaxNumber)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(number), number, $"베팅 숫자는 1..{MaxNumber} 여야 합니다. 0 은 물리기입니다.");
            }

            if (amount < 1 || amount > MaxAmount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, $"베팅 금액은 1..{MaxAmount} 여야 합니다.");
            }

            return (amount << NumberBits) | number;
        }

        /// <summary>물리기 요청. 실을 값이 없으므로 언제나 0 이다.</summary>
        public static int PackWithdraw() => WithdrawNumber;

        /// <summary>
        /// 받은 칸을 푼다. <b>회선에서 온 값이므로 무엇이든 올 수 있다</b> —
        /// 풀 수 없으면 <c>false</c> 이고 부르는 쪽은 그 요청을 버린다.
        /// </summary>
        public static bool TryUnpack(int packed, out RouletteRequestKind kind, out int number, out int amount)
        {
            kind = RouletteRequestKind.Withdraw;
            number = 0;
            amount = 0;

            // 음수는 담긴 적이 없는 값이다. 통과시키면 amount 가 음수가 된다.
            if (packed < 0)
            {
                return false;
            }

            int packedNumber = packed & MaxNumber;
            int packedAmount = packed >> NumberBits;

            if (packedNumber == WithdrawNumber)
            {
                // 물리기에는 금액이 없다. 금액이 실려 있으면 우리가 담은 값이 아니다.
                if (packedAmount != 0)
                {
                    return false;
                }

                kind = RouletteRequestKind.Withdraw;
                return true;
            }

            if (packedAmount < 1)
            {
                return false;
            }

            kind = RouletteRequestKind.Bet;
            number = packedNumber;
            amount = packedAmount;
            return true;
        }
    }
}
