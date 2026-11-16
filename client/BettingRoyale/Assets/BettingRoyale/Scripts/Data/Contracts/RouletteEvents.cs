using System;
using BettingRoyale.Core;
using BettingRoyale.Data.Roulette;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// 배당표 한 줄. 화면이 확률판을 그리는 데 쓴다 (S-GD-49WM7S).
    /// </summary>
    public readonly struct RouletteNumberView
    {
        /// <summary>걸 수 있는 숫자.</summary>
        public readonly int Number;

        /// <summary>이 숫자가 적힌 칸 수. 확률은 <c>칸 수 ÷ 전체 칸 수</c> 다.</summary>
        public readonly int SlotCount;

        /// <summary>적중 시 <b>총 회수 배수</b>. <c>숫자 + 1</c> 이다 — 건 돈이 포함된 값이다.</summary>
        public readonly int PayoutMultiplier;

        public RouletteNumberView(int number, int slotCount, int payoutMultiplier)
        {
            Number = number;
            SlotCount = slotCount;
            PayoutMultiplier = payoutMultiplier;
        }
    }

    /// <summary>
    /// <b>이 매치의 바퀴가 이렇게 생겼다</b> (S-GD-49WM7S).
    ///
    /// <para>
    /// <b>준비 단계가 열릴 때 한 번만 발행한다.</b> 바퀴는 매치 중에 바뀌지 않으므로
    /// 상태 이벤트에 매번 실어 보내면 25칸 배열이 계속 회선을 탄다.
    /// </para>
    ///
    /// <para>
    /// <b>방 전체 정보라 <c>PlayerId</c>·<c>IsMine</c> 이 없다.</b> 방에 바퀴가 하나뿐이고
    /// 모두가 같은 것을 본다 (S-GD-7VKZA5).
    /// </para>
    ///
    /// <para><b>배열은 절대 <c>null</c> 이 아니다</b> — 비면 길이 0 이다.</para>
    /// </summary>
    public readonly struct RouletteWheelAnnounced : IGameEvent
    {
        /// <summary>
        /// 칸 배치 그대로. <b>상단에서 시계 방향</b>이고 3D 에셋의 슬롯 순서와 같다.
        /// 화면은 <c>적중 칸 × 360 / 길이</c> 로 회전 각도를 구한다.
        /// </summary>
        public readonly int[] Layout;

        /// <summary>배당표. 순서는 정해져 있지 않다.</summary>
        public readonly RouletteNumberView[] Numbers;

        /// <summary>최소 베팅 금액. 화면이 슬라이더 하한을 잡는 데 쓴다.</summary>
        public readonly int MinBet;

        public RouletteWheelAnnounced(int[] layout, RouletteNumberView[] numbers, int minBet)
        {
            Layout = layout ?? Array.Empty<int>();
            Numbers = numbers ?? Array.Empty<RouletteNumberView>();
            MinBet = minBet;
        }
    }

    /// <summary>내가 이번 판에 건 것 한 줄.</summary>
    public readonly struct RouletteBetView
    {
        public readonly int Number;

        /// <summary>그 숫자에 걸린 <b>누계</b>. 같은 숫자에 여러 번 걸면 합쳐진 값이다.</summary>
        public readonly int Amount;

        public RouletteBetView(int number, int amount)
        {
            Number = number;
            Amount = amount;
        }
    }

    /// <summary>
    /// 룰렛의 <b>현재 확정 상태 전체</b> (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>바뀐 것만 보내지 않고 전부 싣는다.</b> 이벤트 하나를 놓쳐도 화면이 실제와
    /// 어긋나지 않게 하기 위해서다 (<c>client/AGENTS.md</c> 7절).
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 매 틱 발행하지 않는다.</b> 상태가 바뀔 때·베팅했을 때·정산했을 때만 나간다.
    /// <see cref="RemainingTicks"/> 는 <b>그 순간의 값</b>이고, 남은 시간 카운트다운은
    /// 화면이 직접 센다 — 매 틱 발행하면 방 인원수만큼 이벤트가 매 틱 늘어난다.
    /// </para>
    /// </summary>
    public readonly struct RouletteStateChanged : IGameEvent
    {
        /// <summary><c>Scripts/Network/</c> 의 <c>PlayerSlot.PlayerId</c> 와 같은 값.</summary>
        public readonly int PlayerId;

        /// <summary>지금 화면을 보는 사람의 것인가. 정본 설명은 <see cref="GoldChanged.IsMine"/> 에 있다.</summary>
        public readonly bool IsMine;

        /// <summary>
        /// 바퀴의 상태.
        ///
        /// <b>도메인 열거형을 그대로 싣는다</b> — <c>ShopOfferView.Kind</c> 가
        /// <c>EquipmentItemKind</c> 를 그대로 싣는 것과 같은 판단이다. 화면용 사본을 따로 두면
        /// 값이 하나 늘 때마다 두 곳을 고쳐야 하고, 한쪽만 고치면 조용히 어긋난다.
        /// </summary>
        public readonly RouletteState State;

        /// <summary>판 번호. 베팅이 열릴 때마다 1 오른다. 화면이 지난 판과 구분하는 데 쓴다.</summary>
        public readonly int Cycle;

        /// <summary>이 상태가 끝나기까지 남은 틱. <c>Idle</c>·<c>Closed</c> 에서는 0.</summary>
        public readonly int RemainingTicks;

        /// <summary>
        /// 적중 칸 번호. <b>회전이 시작될 때 정해지고 그 전에는 -1</b> 이다.
        /// 화면은 이 값으로 3D 바퀴를 멈출 각도를 구한다.
        /// </summary>
        public readonly int WinningSlot;

        /// <summary>적중 숫자. 정해지기 전에는 0.</summary>
        public readonly int WinningNumber;

        /// <summary>
        /// 이 사람이 이번 판에 건 것 전부. <b><c>null</c> 이 아니다</b> — 안 걸었으면 길이 0.
        /// </summary>
        public readonly RouletteBetView[] MyBets;

        /// <summary>그 합계. 화면이 다시 더하지 않게 함께 싣는다.</summary>
        public readonly int MyStakeTotal;

        /// <summary>이번 판에 건 사람 수. 화면이 "판돈이 모였는지" 를 보이는 데 쓴다.</summary>
        public readonly int BettorCount;

        /// <summary>
        /// 지금 이 사람이 걸 수 있는가.
        ///
        /// <b>잔액만 보는 값이 아니다</b> — 베팅 시간이 아니거나 구역 밖이면 <c>false</c> 다.
        /// 화면은 이 값으로 버튼을 흐리게 하고, 그래도 눌렸을 때의 사유는
        /// <see cref="RouletteBetRejected"/> 로 온다. <b>둘 다 필요하다</b> —
        /// 이 값만 믿고 요청을 안 보내면 도메인이 판정할 기회가 없다.
        /// </summary>
        public readonly bool CanBet;

        public RouletteStateChanged(
            int playerId,
            bool isMine,
            RouletteState state,
            int cycle,
            int remainingTicks,
            int winningSlot,
            int winningNumber,
            RouletteBetView[] myBets,
            int myStakeTotal,
            int bettorCount,
            bool canBet)
        {
            PlayerId = playerId;
            IsMine = isMine;
            State = state;
            Cycle = cycle;
            RemainingTicks = remainingTicks;
            WinningSlot = winningSlot;
            WinningNumber = winningNumber;
            MyBets = myBets ?? Array.Empty<RouletteBetView>();
            MyStakeTotal = myStakeTotal;
            BettorCount = bettorCount;
            CanBet = canBet;
        }
    }

    /// <summary>
    /// 베팅이 실제로 걸렸다 (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>이게 있어야 화면이 확정 금액을 안다.</b> 잔액보다 크게 걸면 깎이므로
    /// <see cref="RouletteBetCommand.Amount"/> 와 <see cref="Amount"/> 가 다를 수 있다.
    /// 상태 이벤트만으로도 총액은 알 수 있지만, 그러려면 화면이 이전 상태와 비교해야 한다.
    /// </para>
    ///
    /// <para><b>재전송에는 오지 않는다</b> — 이미 한 번 걸린 요청이다.</para>
    /// </summary>
    public readonly struct RouletteBetPlaced : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>지금 화면을 보는 사람의 것인가. 정본 설명은 <see cref="GoldChanged.IsMine"/> 에 있다.</summary>
        public readonly bool IsMine;

        public readonly int Number;

        /// <summary><b>실제로 걸린 금액.</b> 요청한 값이 아니라 깎인 뒤의 값이다.</summary>
        public readonly int Amount;

        /// <summary>이번 판에 이 사람이 건 것의 총액(이번 것 포함).</summary>
        public readonly int TotalStaked;

        public RouletteBetPlaced(int playerId, bool isMine, int number, int amount, int totalStaked)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Number = number;
            Amount = amount;
            TotalStaked = totalStaked;
        }
    }

    /// <summary>
    /// 베팅이 거절된 이유 (S-GD-7VKZA5).
    ///
    /// <b>⚠️ 값은 뒤에만 붙인다.</b> 이 열거형은 계약에 실려 회선을 넘을 수 있다.
    /// 중간에 끼우면 이미 배포된 값의 뜻이 밀린다.
    /// </summary>
    public enum RouletteBetRejectReason
    {
        /// <summary>
        /// 지금은 베팅 시간이 아니다 — 회전 중이거나 결과 표시 중이거나 마감됐다.
        /// <b>잠시 뒤 다시 열린다</b>는 뜻이라 다른 사유와 문구가 달라야 한다
        /// (마감된 경우만 예외인데, 그건 <see cref="RouletteStateChanged.State"/> 로 구분한다).
        /// </summary>
        NotBetting = 0,

        /// <summary>
        /// 준비 단계가 아니다. 전투 중에는 걸 수 없다.
        ///
        /// <b>재전송과 구분된다.</b> 같은 요청을 두 번 보낸 것은 거절이 아니라 조용한 현재 상태이고,
        /// 이 값은 <b>처음 보내는 요청인데 단계가 닫혀 있을 때만</b> 나간다.
        /// </summary>
        NotPreparation = 1,

        /// <summary>
        /// 바퀴에 없는 숫자다. <b>정상 플레이에서는 나오지 않는다</b> —
        /// 화면은 배당표에 있는 숫자만 보여주기 때문이다. 보내는 쪽이 지어낸 값이다.
        /// </summary>
        NoSuchNumber = 2,

        /// <summary>요청한 금액이 최소 베팅에 못 미친다.</summary>
        BelowMinimum = 3,

        /// <summary>
        /// 가진 골드가 최소 베팅에도 못 미친다.
        ///
        /// <b>"요청액보다 잔액이 적다" 가 아니다</b> — 그 경우는 거절이 아니라 잔액만큼으로 깎인다.
        /// 깎아도 최소 베팅이 안 될 때만 이 값이 나간다.
        /// </summary>
        NotEnoughGold = 4,

        /// <summary>
        /// 룰렛 구역 밖이다 (S-GD-66GTWA, 반경 5m). 구역을 벗어나면 화면도 닫히므로
        /// <b>정상 플레이에서는 거의 나오지 않는다</b> — 창이 닫히는 순간과 클릭이 겹쳤거나,
        /// 보내는 쪽이 위치를 지어낸 경우다.
        /// </summary>
        NotInRange = 5,

        /// <summary>먼저 이번 라운드의 축복 선택을 확정해야 한다.</summary>
        SelectionRequired = 6,
    }

    /// <summary>
    /// 베팅이 거절됐다 (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>재전송에는 이 이벤트가 오지 않는다.</b> 같은 요청을 두 번 보낸 것은 거절이 아니다 —
    /// 사용자의 클릭은 이미 한 번 처리됐으므로 거절을 띄우면 거짓말이 된다.
    /// 그 구분은 <c>EconomyRequestLedger</c> 가 만든다.
    /// </para>
    /// </summary>
    public readonly struct RouletteBetRejected : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>지금 화면을 보는 사람의 것인가. 정본 설명은 <see cref="GoldChanged.IsMine"/> 에 있다.</summary>
        public readonly bool IsMine;

        /// <summary>거절된 요청이 지목한 숫자. <b>물리기 거절에서는 0</b> 이다.</summary>
        public readonly int Number;

        public readonly RouletteBetRejectReason Reason;

        public RouletteBetRejected(int playerId, bool isMine, int number, RouletteBetRejectReason reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Number = number;
            Reason = reason;
        }
    }

    /// <summary>
    /// 물리기가 거절된 이유 (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>베팅 거절과 값을 나눠 둔 이유</b>: 물리기에는 숫자도 금액도 구역 제한도 없다.
    /// 한 열거형에 합치면 화면이 <b>이 요청에서는 절대 안 오는 값</b>까지 문구를 준비해야 하고,
    /// 그중 어느 것이 실제로 오는지는 코드를 읽어야만 알 수 있다.
    /// </para>
    ///
    /// <b>⚠️ 값은 뒤에만 붙인다.</b> 이 열거형은 회선을 넘는다.
    /// </summary>
    public enum RouletteWithdrawRejectReason
    {
        /// <summary>
        /// 베팅 시간이 아니다. <b>회전이 시작되면 물릴 수 없다</b> — 결과가 이미 정해졌기 때문이다.
        /// </summary>
        NotBetting = 0,

        /// <summary>준비 단계가 아니다. 재전송과 달리 <b>처음 보내는 요청일 때만</b> 나간다.</summary>
        NotPreparation = 1,

        /// <summary>물릴 것이 없다. 아무것도 안 걸었는데 물리기를 눌렀다.</summary>
        NothingToWithdraw = 2,
    }

    /// <summary>
    /// 물리기가 거절됐다 (S-GD-7VKZA5).
    ///
    /// <para><b>재전송에는 오지 않는다</b> — <see cref="RouletteBetRejected"/> 와 같은 이유다.</para>
    /// </summary>
    public readonly struct RouletteWithdrawRejected : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>지금 화면을 보는 사람의 것인가. 정본 설명은 <see cref="GoldChanged.IsMine"/> 에 있다.</summary>
        public readonly bool IsMine;

        public readonly RouletteWithdrawRejectReason Reason;

        public RouletteWithdrawRejected(int playerId, bool isMine, RouletteWithdrawRejectReason reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Reason = reason;
        }
    }

    /// <summary>
    /// 한 판이 끝나고 정산됐다 (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>건 돈은 이미 빠져 있다.</b> 그래서 지갑에 일어난 일은 <see cref="Payout"/> 이
    /// 들어온 것뿐이고, 화면이 보여줄 손익은 <see cref="Net"/> 이다.
    /// </para>
    ///
    /// <para>
    /// <b>환불도 이 이벤트로 온다</b> — 돌지 않은 판의 돈을 돌려준 경우다.
    /// 그때는 <see cref="WasRefund"/> 가 참이고 <see cref="Net"/> 이 0 이라
    /// <b>화면이 "꽝" 으로 표시하면 안 된다.</b>
    /// </para>
    /// </summary>
    public readonly struct RouletteSettled : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>지금 화면을 보는 사람의 것인가. 정본 설명은 <see cref="GoldChanged.IsMine"/> 에 있다.</summary>
        public readonly bool IsMine;

        /// <summary>정산된 판 번호.</summary>
        public readonly int Cycle;

        /// <summary>적중 칸. <b>환불이면 -1</b> — 바퀴가 돌지 않았다.</summary>
        public readonly int WinningSlot;

        /// <summary>적중 숫자. <b>환불이면 0</b>.</summary>
        public readonly int WinningNumber;

        /// <summary>이 판에 건 돈 전부.</summary>
        public readonly int Staked;

        /// <summary>그중 적중 숫자에 건 금액. 0 이면 전부 틀렸다.</summary>
        public readonly int StakedOnWinner;

        /// <summary>돌려받은 총액(축복 환급 포함). <b>건 돈이 포함된 금액</b>이라 순이익이 아니다.</summary>
        public readonly int Payout;

        /// <summary>순손익. 양수면 땄다.</summary>
        public readonly int Net;

        /// <summary>
        /// 축복 트리거 <c>ON_ROULETTE_LOSS</c> 가 쓸 손실액 (S-GD-EYJEH7).
        /// 딴 판과 환불은 0 이다.
        /// </summary>
        public readonly int Loss;

        /// <summary>
        /// <b>돌지 않은 판의 환불인가.</b> 참이면 바퀴가 돌지 않았고 낸 돈을 그대로 돌려받았다 —
        /// 플레이어가 통제할 수 없는 일이라 손실로 세지 않는다.
        /// </summary>
        public readonly bool WasRefund;

        public RouletteSettled(
            int playerId,
            bool isMine,
            int cycle,
            int winningSlot,
            int winningNumber,
            int staked,
            int stakedOnWinner,
            int payout,
            int net,
            int loss,
            bool wasRefund)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Cycle = cycle;
            WinningSlot = winningSlot;
            WinningNumber = winningNumber;
            Staked = staked;
            StakedOnWinner = stakedOnWinner;
            Payout = payout;
            Net = net;
            Loss = loss;
            WasRefund = wasRefund;
        }
    }
}
