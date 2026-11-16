using System;

namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// 보물상자 하나의 상태 기계 (S-GD-5Y4PTJ).
    ///
    /// 개봉은 <b>그 자리에 멈춰 있어야</b> 완료된다 — 이동하거나 피격당하면 취소되고 대기로 돌아간다.
    /// 한 번 보상을 받은 상자는 <b>재생성되기 전까지 다시 보상을 주지 않는다</b>
    /// (완료 조건: "Fake 상자 상태 전이와 중복 보상 방지 규칙 실행").
    ///
    /// <b>보상 내용은 여기에 없다.</b> 무엇이 나오는지와 그 확률은 S-GD-5Y4PTJ 의 미정 항목이라
    /// 임의로 만들어 넣지 않는다. 이 클래스는 "지금 보상을 받을 수 있는가" 까지만 답한다.
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class TreasureChest
    {
        /// <summary>아무도 개봉하고 있지 않음을 나타내는 <see cref="OpenerId"/> 값.</summary>
        public const int NoOpener = -1;

        private readonly ChestConfig config;
        private int elapsedTicks;

        public TreasureChest(ChestConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            State = ChestState.Idle;
            OpenerId = NoOpener;
        }

        public ChestState State { get; private set; }
        public int OpeningSequence { get; private set; }
        public int CompletionSequence { get; private set; }

        /// <summary>
        /// 이 상자를 마지막으로 연 / 열고 있는 플레이어의 id.
        /// <c>Scripts/Network/</c> 의 <c>PlayerSlot.PlayerId</c> 와 같은 값을 쓴다 — 경계에서 타입이 갈리지 않게 한다.
        ///
        /// <b>보상 상태에서도 지워지지 않는다</b> (2026-08-28 실제 동작에 맞춰 문서 정정).
        /// <see cref="NoOpener"/> 가 되는 것은 <c>Idle</c> 일 때뿐이다 —
        /// <see cref="TryCancel"/> 과 <see cref="Respawn"/> 에서만 초기화한다.
        /// 개봉이 완료된 뒤에도 <b>누구에게 보상을 줄지</b> 알아야 하므로 남겨둔다.
        /// </summary>
        public int OpenerId { get; private set; }

        /// <summary>현재 상태에서 지난 틱 수.</summary>
        public int ElapsedTicks => elapsedTicks;

        /// <summary>현재 구성에서 개봉에 필요한 틱 수. 늦게 접속한 peer의 진행률 복제에 쓴다.</summary>
        public int OpenDurationTicks => config.OpenDurationTicks;

        /// <summary>이번 생애에서 보상이 이미 지급됐는가. 재생성하면 false 로 돌아간다.</summary>
        public bool IsRewardClaimed { get; private set; }

        /// <summary>지금 보상을 받을 수 있는가.</summary>
        public bool IsRewardClaimable => State == ChestState.Rewarded && !IsRewardClaimed;

        /// <summary>개봉 진행률 0~1. 개봉 중이 아니면 0.</summary>
        public float OpenProgress => State == ChestState.Opening
            ? (float)elapsedTicks / config.OpenDurationTicks
            : 0f;

        /// <summary>
        /// 개봉을 시작한다. 대기 상태일 때만 된다 —
        /// 이미 다른 사람이 열고 있으면 false 다 (경쟁 처리 규칙은 Jira S15P21A606-33 에서 다룬다).
        /// </summary>
        public bool TryBeginOpen(int openerId)
        {
            if (openerId < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(openerId), openerId, "개봉자 id 는 0 이상이어야 합니다.");
            }

            if (State != ChestState.Idle)
            {
                return false;
            }

            State = ChestState.Opening;
            OpeningSequence++;
            OpenerId = openerId;
            elapsedTicks = 0;
            return true;
        }

        /// <summary>
        /// 한 틱 진행한다. 개봉 중이면 진행도를, 재생성 중이면 대기 시간을 센다.
        /// </summary>
        /// <returns>이 틱에서 상태가 바뀌었으면 true.</returns>
        public bool Tick()
        {
            switch (State)
            {
                case ChestState.Opening:
                    elapsedTicks++;
                    if (elapsedTicks < config.OpenDurationTicks)
                    {
                        return false;
                    }

                    State = ChestState.Rewarded;
                    CompletionSequence++;
                    elapsedTicks = 0;
                    return true;

                case ChestState.Respawning:
                    elapsedTicks++;
                    if (elapsedTicks < config.RespawnDelayTicks)
                    {
                        return false;
                    }

                    Respawn();
                    return true;

                default:
                    // 대기·보상 상태는 시간이 흘러도 저절로 바뀌지 않는다.
                    return false;
            }
        }

        /// <summary>
        /// 개봉을 취소한다 (S-GD-5Y4PTJ). 개봉 중일 때만 의미가 있고, 진행도는 <b>버려진다</b> —
        /// 다시 열면 처음부터다.
        /// </summary>
        /// <returns>실제로 취소됐으면 true.</returns>
        public bool TryCancel(ChestCancelReason reason)
        {
            if (State != ChestState.Opening)
            {
                return false;
            }

            _ = reason; // 사유는 연출·로그용이다. 상태 전이는 사유와 무관하게 같다.
            State = ChestState.Idle;
            OpenerId = NoOpener;
            elapsedTicks = 0;
            return true;
        }

        /// <summary>
        /// 보상을 수령한다. <b>중복 보상 방지</b> — 이미 받았거나 보상 상태가 아니면 false 다.
        /// 성공하면 재생성 대기로 넘어간다.
        /// </summary>
        /// <remarks>
        /// 무엇을 주는지는 이 클래스가 정하지 않는다 (S-GD-5Y4PTJ 의 미정 항목).
        /// 호출한 쪽이 보상 테이블을 적용한다.
        /// </remarks>
        public bool TryClaimReward()
        {
            if (!IsRewardClaimable)
            {
                return false;
            }

            IsRewardClaimed = true;
            State = ChestState.Respawning;
            elapsedTicks = 0;
            return true;
        }

        /// <summary>재생성 대기를 건너뛰고 즉시 대기 상태로 되돌린다. 라운드 초기화 등에 쓴다.</summary>
        public void Respawn()
        {
            State = ChestState.Idle;
            OpenerId = NoOpener;
            IsRewardClaimed = false;
            elapsedTicks = 0;
        }

        public override string ToString() =>
            $"{State}({elapsedTicks}틱){(IsRewardClaimed ? " 보상완료" : string.Empty)}";
    }
}
