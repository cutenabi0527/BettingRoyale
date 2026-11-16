using BettingRoyale.Core;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>씬 로딩 뒤 생긴 화면이 이미 받은 바퀴 표시 정보를 요청한다. 베팅/정산 명령이 아니다.</summary>
    public readonly struct RouletteWheelRequested : IGameEvent { }

    // 이 파일의 명령은 화면 → 도메인 방향이다. 규약은 docs/client/ui-domain-contract.md §4·§5.
    //
    // ⚠️ 명령에는 "누가 보냈는가" 를 담지 않는다 (§5). 칸이 있는 한 남의 id 를 적어
    //    남의 골드를 걸 수 있다. 누가 보냈는지는 어댑터가 연결 정보에서 판단한다.
    //
    // ⚠️ IGameEvent 를 구현한다 (§4). EventBus.Publish<T> 에 제약이 있어서
    //    이게 없으면 화면이 명령을 보낼 방법이 아예 없다.

    /// <summary>
    /// 룰렛의 숫자 하나에 건다 (S-GD-49WM7S, S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>거는 순간 골드가 빠진다.</b> 정산 때가 아니다 — 걸어 두고 도망갈 수 있으면
    /// 베팅이 공짜가 된다.
    /// </para>
    ///
    /// <para>
    /// <b>가진 것보다 많이 걸면 거절이 아니라 가진 만큼으로 깎인다.</b> "전부 걸기" 를 눌렀을 때
    /// 거절당하는 것이 더 이상하기 때문이다. 그래서 <b>실제로 걸린 금액은 여기 적은 값과 다를 수
    /// 있고</b>, 확정 금액은 <see cref="RouletteBetPlaced"/> 로 돌아온다.
    /// 화면은 <b>보낸 값이 아니라 돌아온 값</b>을 표시해야 한다.
    /// </para>
    ///
    /// <para>
    /// 같은 숫자에 또 걸면 <b>더해진다</b>. 여러 숫자에 동시에 걸 수 있다.
    /// </para>
    /// </summary>
    public readonly struct RouletteBetCommand : IGameEvent
    {
        /// <summary>
        /// 걸 숫자. <see cref="RouletteWheelAnnounced"/> 로 받은 목록에 있는 값만 유효하다.
        /// <b>칸 번호가 아니라 칸에 적힌 숫자다</b> — 같은 숫자가 여러 칸에 있다.
        /// </summary>
        public readonly int Number;

        /// <summary>
        /// 걸 금액. 최소 베팅 미만이면 거절되고, 잔액보다 크면 잔액만큼으로 깎인다.
        /// </summary>
        public readonly int Amount;

        public RouletteBetCommand(int number, int amount)
        {
            Number = number;
            Amount = amount;
        }
    }

    /// <summary>
    /// 이번 판에 건 것을 <b>전부</b> 물린다 (S-GD-7VKZA5 베팅 규칙 6). 전액 돌려받는다.
    ///
    /// <para>
    /// <b>숫자를 골라 일부만 물릴 수는 없다.</b> 부분 취소를 넣으면 "얼마가 남았는지" 를
    /// 화면과 도메인이 각각 세게 되고, 회전 직전의 한 프레임에서 그 둘이 갈린다.
    /// </para>
    ///
    /// <para><b>회전이 시작되면 물릴 수 없다.</b> 결과가 이미 정해졌기 때문이다.</para>
    /// </summary>
    public readonly struct RouletteWithdrawCommand : IGameEvent
    {
    }
}
