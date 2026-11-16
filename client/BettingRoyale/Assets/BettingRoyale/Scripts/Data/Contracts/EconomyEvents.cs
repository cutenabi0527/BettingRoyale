using BettingRoyale.Core;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>골드가 왜 변했는가. UI 가 연출을 고르고 로그가 원인을 남기는 데 쓴다.</summary>
    public enum GoldChangeReason
    {
        /// <summary>라운드 시작 기본 지급 (S-GD-2CFK2Z).</summary>
        RoundGrant = 0,

        /// <summary>플레이 중 획득 (킬 보상, 상자 보상 등).</summary>
        Earned = 1,

        /// <summary>지출 (축복 재추첨 등).</summary>
        Spent = 2,

        /// <summary>라운드 정산으로 이월됐다 (S-GD-P5RFZM).</summary>
        CarriedOver = 3,

        /// <summary>새 매치 시작으로 초기화됐다 (S-GD-RFXF8X).</summary>
        MatchReset = 4,

        /// <summary>
        /// <b>낸 돈이 되돌아왔다</b> — 돌지 않은 판의 룰렛 베팅 환불 (S-GD-7VKZA5).
        ///
        /// <b><see cref="Earned"/> 와 구분하는 이유</b>: 환불은 번 것이 아니라 안 쓴 것으로
        /// 되돌리는 것이라 <c>Wallet.TotalInflow</c> 를 올리지 않는다. 그 값이 최종 순위
        /// 동점 처리 5단계 "매치 동안 모은 골드" 다 (S-GD-MNFBV7). 화면 문구도 달라야 한다 —
        /// <b>환불을 "획득" 으로 보이면 손해를 이득으로 읽는다.</b>
        ///
        /// <b>⚠️ 값은 뒤에만 붙인다.</b> 이 열거형은 회선을 넘는다.
        /// </summary>
        Refunded = 5,
    }

    /// <summary>
    /// 한 플레이어의 골드가 변했다.
    ///
    /// <b>변화량이 아니라 확정 잔액을 함께 싣는다.</b> 이벤트 하나를 놓쳐도 화면이 실제와 어긋나지 않게 하기 위해서다
    /// (<c>client/AGENTS.md</c> 7절: "델타가 아니라 전체 목록"과 같은 이유).
    /// <see cref="Delta"/> 는 연출용이지 계산용이 아니다.
    /// </summary>
    public readonly struct GoldChanged : IGameEvent
    {
        /// <summary><c>Scripts/Network/</c> 의 <c>PlayerSlot.PlayerId</c> 와 같은 값.</summary>
        public readonly int PlayerId;

        /// <summary>
        /// 이 이벤트가 <b>지금 화면을 보고 있는 사람의 것인가</b>
        /// (<c>docs/client/ui-domain-contract.md</c> §7). <b>이 필드의 설명이 정본이고
        /// 다른 이벤트의 <c>IsMine</c> 은 여기를 가리킨다.</b>
        ///
        /// <b>플레이어별 이벤트는 방 전원분이 발행된다.</b> 화면은 핸들러 첫 줄에서
        /// <c>if (!e.IsMine) return;</c> 로 거른 뒤에만 그린다.
        /// 거르지 않으면 <b>남의 골드가 내 HUD 에 찍힌다</b> — 발행자가 하나뿐인 동안에는
        /// 멀쩡해 보이다가, 실제 어댑터가 붙는 순간 틀리기 시작한다. 컴파일러도 lint 도 못 잡는다.
        ///
        /// <b>왜 도메인이 "내 것만" 발행하지 않는가</b>: 관전이 있다. 빈사 상태에서 남을 볼 때는
        /// <b>남의 값을 그려야</b> 하므로, 도메인이 미리 걸러버리면 그때 그릴 것이 없다.
        /// 거르는 판단은 화면이 한다.
        ///
        /// <b>누가 채우는가</b>: 어댑터다 (Jira S15P21A606-26). 도메인은 누가 로컬 플레이어인지 모르고,
        /// 알 필요도 없다 — <c>Scripts/Data/</c> 는 세션을 모른다.
        /// </summary>
        public readonly bool IsMine;

        /// <summary>변화 후 확정 잔액. 화면은 이 값을 그대로 그린다.</summary>
        public readonly int Gold;

        /// <summary>이번 변화량. 획득은 양수, 지출은 음수. <b>연출용</b>.</summary>
        public readonly int Delta;

        public readonly GoldChangeReason Reason;

        /// <summary>
        /// 이월 한도 (S-GD-P5RFZM). 화면의 "이월 한도 N G" 에 그대로 쓴다.
        /// <see cref="IsCarryOverUnlimited"/> 가 true 면 이 값은 의미가 없다.
        ///
        /// <b>매번 실어 보내는 것이 낭비처럼 보이지만 의도한 것이다.</b> 이 이벤트 하나만 받아도
        /// 준비 화면이 완전히 그려져야 한다 — 한도를 별도 이벤트로 빼면 그 이벤트를 놓친 화면이
        /// "이월 한도 —" 인 채로 남고, 그건 재현이 안 되는 종류의 버그다
        /// (<c>client/AGENTS.md</c> 7절 "델타가 아니라 전체" 와 같은 이유).
        /// </summary>
        public readonly int CarryOverCap;

        /// <summary>
        /// 무제한 이월인가 (S-GD-P5RFZM). <b>한도값에 특수값을 넣는 대신 별도 플래그로 둔다.</b>
        ///
        /// ⚠️ 어댑터 주의 — <c>CarryOverLimit.Gold</c> 는 무제한일 때 <b>예외를 던진다.</b>
        /// 반드시 <c>IsUnlimited</c> 를 먼저 보고 분기해야 한다. 이 이벤트를 만드는 코드가
        /// 무심코 <c>limit.Gold</c> 를 읽으면 무제한 설정에서 터진다.
        /// </summary>
        public readonly bool IsCarryOverUnlimited;

        public GoldChanged(
            int playerId,
            bool isMine,
            int gold,
            int delta,
            GoldChangeReason reason,
            int carryOverCap,
            bool isCarryOverUnlimited)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Gold = gold;
            Delta = delta;
            Reason = reason;
            CarryOverCap = carryOverCap;
            IsCarryOverUnlimited = isCarryOverUnlimited;
        }
    }

    /// <summary>
    /// 라운드의 <b>골드가</b> 정산됐다 (S-GD-P5RFZM). 넘어간 금액과 <b>사라진 금액</b>을 함께 싣는다 —
    /// UI 가 "N G 소멸" 을 보여주려면 소멸액이 필요하고, 잔액 차이로 역산하게 두면 계산이 두 곳으로 갈린다.
    ///
    /// <b>⚠️ 이름이 <c>RoundSettled</c> 였다 (2026-08-31 개명).</b>
    /// <c>Scripts/UI/ResultViewEvents.cs</c> 에 같은 이름의 <b>다른</b> 이벤트가 있었다 — 그쪽은 순위표다.
    /// C# 은 네임스페이스가 다르면 같은 이름을 허용하고, <c>EventBus</c> 는 타입으로 구독하므로
    /// <b>둘은 영원히 만나지 않는데 컴파일러는 아무 말도 하지 않는다.</b>
    ///
    /// <b>왜 UI 가 아니라 이쪽을 바꿨나</b>: 규약(<c>docs/client/ui-domain-contract.md</c> §10)은
    /// "겹치면 UI 가 다른 이름을 쓴다" 이지만, 그건 <b>앞으로 생길 충돌</b>에 대한 규칙이다.
    /// 이미 있던 이 충돌은 이쪽 참조자가 <b>0</b>, UI 쪽은 발행자가 1 이라 이쪽을 바꾸는 것이 쌌고,
    /// 무엇보다 <b>이 이름이 더 정확하다</b> — 이 이벤트는 라운드 결과가 아니라 골드 정산이다.
    /// </summary>
    public readonly struct RoundGoldSettled : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>화면을 보는 사람의 것인가. 규약은 <see cref="GoldChanged.IsMine"/> 참조 (§7).</summary>
        public readonly bool IsMine;

        /// <summary>
        /// 1부터 시작하는, <b>방금 끝난</b> 라운드 번호 — <b>정산 대상 라운드</b>다.
        ///
        /// <para>
        /// ⚠️ <b>이 이벤트는 다음 라운드 준비가 열릴 때 온다.</b> 2라운드 준비 화면에서
        /// <c>Round == 1</c> 인 이벤트를 받는다 — 그 돈은 1라운드에서 남은 것이기 때문이다.
        /// 화면 문구는 <b>"1라운드 정산"</b> 이지 "2라운드 시작" 이 아니다.
        /// </para>
        ///
        /// <para>
        /// <b>1라운드 준비에서는 이 이벤트가 오지 않는다.</b> 정산할 이전 라운드가 없다.
        /// 도메인 쪽이 <c>TryBuildRoundGoldSettled</c> 로 그 사실을 드러낸다.
        /// </para>
        /// </summary>
        public readonly int Round;

        /// <summary>다음 라운드로 넘어간 골드.</summary>
        public readonly int CarriedGold;

        /// <summary>이월 한도를 넘어 사라진 골드.</summary>
        public readonly int ForfeitedGold;

        public RoundGoldSettled(int playerId, bool isMine, int round, int carriedGold, int forfeitedGold)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Round = round;
            CarriedGold = carriedGold;
            ForfeitedGold = forfeitedGold;
        }
    }

    /// <summary>새 매치가 시작되어 골드와 축복이 초기화됐다 (S-GD-RFXF8X).</summary>
    public readonly struct MatchEconomyReset : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>화면을 보는 사람의 것인가. 규약은 <see cref="GoldChanged.IsMine"/> 참조 (§7).</summary>
        public readonly bool IsMine;

        public MatchEconomyReset(int playerId, bool isMine)
        {
            PlayerId = playerId;
            IsMine = isMine;
        }
    }
}
