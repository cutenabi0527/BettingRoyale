using BettingRoyale.Core;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>상자 하나의 현재 모습. <see cref="ChestsChanged"/> 가 이 배열을 싣는다.</summary>
    public readonly struct ChestSnapshot
    {
        /// <summary>상자 식별자. 한 맵에 여러 개가 있으므로 필요하다.</summary>
        public readonly int ChestId;

        public readonly ChestState State;

        /// <summary>
        /// 이 상자를 마지막으로 연 / 열고 있는 플레이어의 id.
        /// <c>Scripts/Network/</c> 의 <c>PlayerSlot.PlayerId</c> 와 같은 값이다.
        ///
        /// <b>언제 <see cref="TreasureChest.NoOpener"/> 인가</b> (2026-08-28 실제 동작에 맞춰 정정):
        /// <list type="bullet">
        ///   <item><c>Idle</c> — 아직 아무도 안 열었거나, 취소됐거나, 재생성됐다 → <c>NoOpener</c></item>
        ///   <item><c>Opening</c> — 여는 중인 사람의 id</item>
        ///   <item><c>Rewarded</c> / <c>Respawning</c> — <b>연 사람의 id 가 그대로 남는다.</b>
        ///     보상을 누구에게 줄지 알아야 하고, 화면도 "누가 먹었는지" 를 표시해야 하기 때문이다</item>
        /// </list>
        /// 전에는 "개봉 중이 아니면 NoOpener" 라고 적었으나 실제와 달랐다.
        /// </summary>
        public readonly int OpenerId;

        /// <summary>
        /// <b>이 상자를 열고 있는 것이 나인가.</b> 화면이 "(내가)" / "(다른 사람)" 을 고르고,
        /// 남이 열고 있으면 전체 알림을 띄우는 데 쓴다 (S-GD-MQP6AS).
        ///
        /// <see cref="OpenerId"/> 와 내 id 를 비교하면 나오는 값이지만 <b>어댑터가 비교해서 넣는다</b> —
        /// "내 id 가 무엇인가" 는 네트워크 레이어의 지식이고, 화면이 그걸 알아야 할 이유가 없다.
        /// </summary>
        public readonly bool IsMine;

        /// <summary>
        /// <b>이 이벤트가 발행된 순간</b>의 개봉 진행도 0~1. 개봉 중이 아니면 0 이다.
        ///
        /// <b>⚠️ 이 값은 계속 갱신되지 않는다.</b> 도메인은 상태가 실제로 바뀔 때만 발행하므로
        /// (<see cref="ChestsChanged"/> 참고), 그 사이 진행 바를 움직이는 것은 <b>화면 몫</b>이다.
        /// 화면은 이 값에서 출발해 <see cref="OpenDurationTicks"/> 와
        /// <see cref="ChestsChanged.TicksPerSecond"/> 로 그 뒤를 보간한다
        /// (<c>docs/client/ui-domain-contract.md</c> §8).
        ///
        /// <b>나눗셈을 도메인이 하는 이유</b>: 개봉 시간이 아직 확정값이 아니고(S-GD-6DNSGY),
        /// 시작 값까지 화면이 계산하게 두면 규칙이 두 곳에 생긴다. <b>시작점은 도메인, 그 뒤는 화면</b>이다.
        ///
        /// <b>2026-08-31 정정</b>: 전에는 이 주석이 "틱→비율 변환을 화면이 하면 안 된다" 고 하고
        /// <see cref="ChestsChanged"/> 주석은 "화면이 스스로 채운다" 고 해서 <b>서로 반대</b>였다.
        /// 그래서 진행 바가 멈춰 있었고 어느 쪽 잘못도 아니었다.
        /// </summary>
        public readonly float Progress;

        /// <summary>현재 상태에서 지난 틱 수. 화면이 보간의 시작점으로 쓴다.</summary>
        public readonly int ElapsedTicks;

        /// <summary>개봉 완료까지 필요한 총 틱 수.</summary>
        public readonly int OpenDurationTicks;

        /// <summary>
        /// 상자의 월드 좌표. 미니맵·화면 밖 인디케이터를 그리는 데 쓴다(UI 파트 요청, 2026-08-27).
        ///
        /// <c>Vector3</c> 가 아닌 이유는 <see cref="WorldPosition"/> 문서 참고 —
        /// <c>Scripts/Data/</c> 는 <c>UnityEngine</c> 을 참조하지 않는다.
        /// </summary>
        public readonly WorldPosition Position;

        public ChestSnapshot(
            int chestId,
            ChestState state,
            int openerId,
            bool isMine,
            float progress,
            int elapsedTicks,
            int openDurationTicks,
            WorldPosition position)
        {
            ChestId = chestId;
            State = state;
            OpenerId = openerId;
            IsMine = isMine;
            Progress = progress;
            ElapsedTicks = elapsedTicks;
            OpenDurationTicks = openDurationTicks;
            Position = position;
        }
    }

    /// <summary>
    /// 상자 상태가 바뀌었다 (S-GD-5Y4PTJ). 개봉 시작·완료·취소·재생성이 전부 이 하나로 온다.
    ///
    /// <b>바뀐 상자 하나가 아니라 전체 목록을 싣는다.</b> 이벤트 하나를 놓치면 화면과 실제가
    /// 어긋난 채 재현이 안 되기 때문이다 (<c>client/AGENTS.md</c> 7절, D-018 과 같은 근거).
    /// 이름이 복수형인 것도 그 뜻이다.
    ///
    /// <b>진행률을 매 틱 쏘지 않는다.</b> 그건 대역폭 낭비다 — 8명 방에서 상자 2개면 초당 수백 건이 된다.
    /// 이 이벤트는 <b>상태가 실제로 바뀐 순간에만</b> 발행하고, 그 사이 진행 바를 움직이는 것은
    /// 화면이 <see cref="ChestSnapshot.Progress"/> 에서 출발해 <see cref="TicksPerSecond"/> 로 보간한다
    /// (<c>docs/client/ui-domain-contract.md</c> §8).
    /// </summary>
    public readonly struct ChestsChanged : IGameEvent
    {
        /// <summary>맵에 있는 상자 전체. 일부가 아니다.</summary>
        public readonly ChestSnapshot[] Chests;

        /// <summary>
        /// 1초에 시뮬레이션 틱이 몇 번 도는가. 화면이 <c>틱 → 초</c> 로 환산해
        /// 이벤트 사이의 진행 바를 채우는 데 쓴다.
        ///
        /// <b>왜 상수로 박지 않고 실어 보내는가</b>: 이 값의 출처는 Fusion 런타임 설정인데
        /// <c>Scripts/Data/</c> 는 Fusion 을 참조하지 않는다(D-009). 저장소의
        /// <c>NetworkProjectConfig.fusion</c> 도 index 기반 기본값이라 정적으로 읽을 수 없다.
        /// <b>임의의 숫자를 계약에 박으면 아무도 승인하지 않은 값이 사실상의 스펙이 된다</b> —
        /// <c>ChestConfig</c> 에 <c>Default</c> 를 두지 않은 것과 같은 이유다.
        ///
        /// <b>누가 채우는가</b>: 어댑터가 <c>Runner</c> 에서 읽어 넣는다 (Jira S15P21A606-26).
        /// Fake 는 자기 타이머 기준값을 넣는다.
        ///
        /// ⚠️ <b>0 이하면 화면은 보간하지 않고 <see cref="ChestSnapshot.Progress"/> 를 그대로 그린다.</b>
        /// 0 으로 나누는 대신 "덜 부드럽지만 틀리지는 않은" 쪽으로 떨어지게 한다.
        /// </summary>
        public readonly int TicksPerSecond;

        public ChestsChanged(ChestSnapshot[] chests, int ticksPerSecond)
        {
            Chests = chests;
            TicksPerSecond = ticksPerSecond;
        }
    }

    /// <summary>
    /// 개봉이 취소됐다 (S-GD-5Y4PTJ). 상태 전이는 <see cref="ChestsChanged"/> 로도 오지만,
    /// <b>사유</b>는 연출이 달라야 해서 따로 알린다 — 이동 취소와 피격 취소는 다른 피드백이다.
    /// </summary>
    public readonly struct ChestOpenCancelled : IGameEvent
    {
        public readonly int ChestId;
        public readonly int OpenerId;
        public readonly ChestCancelReason Reason;

        public ChestOpenCancelled(int chestId, int openerId, ChestCancelReason reason)
        {
            ChestId = chestId;
            OpenerId = openerId;
            Reason = reason;
        }
    }

    /// <summary>
    /// 상자 보상이 지급됐다 (S-GD-5Y4PTJ). <b>한 상자당 한 번만</b> 발행된다 — 중복 보상 방지가 도메인에 있다.
    ///
    /// <b>무엇을 받았는지는 여기에 없다.</b> 보상 내용과 확률은 S-GD-5Y4PTJ 의 미정 항목이라
    /// 정해지기 전에는 필드를 만들지 않는다. 골드가 들어왔다면 <see cref="GoldChanged"/> 가 따로 온다.
    /// </summary>
    public readonly struct ChestRewardClaimed : IGameEvent
    {
        public readonly int ChestId;
        public readonly int ClaimerId;

        public ChestRewardClaimed(int chestId, int claimerId)
        {
            ChestId = chestId;
            ClaimerId = claimerId;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 거부 이벤트 (docs/client/ui-domain-contract.md §6 "침묵은 성공이 아니다")
    //
    // 발행자는 **있다** (2026-09-14, S15P21A606-127 / D-CLI-EB4ZA8).
    //    OnlineCoreLoopWorldPresenter.ApplyReplicatedChestFeedback 이 각 피어에서 올린다.
    //    (줄 번호는 적지 않는다 — 어긋난다. 그 메서드 안의 EventBus.Publish 두 줄이다.)
    //
    //    ⚠️ 판정부(Gameplay/Combat/CombatChestAuthorityRuntime)가 직접 EventBus 를 부르지 않는다.
    //       판정부는 Host 에만 있어서 거기서 발행하면 Host 화면에만 뜬다. 그래서
    //       판정부 → NetworkCombatantMotion 의 사람별 [Networked] → 각 피어에서 발행으로 돈다.
    //       (ChestRejectionFeedbackContractTests.판정부는_EventBus_로_직접_발행하지_않는다)
    //
    //    ⚠️ 그래도 다음 셋은 아직 아니다 — 구독하는 쪽이 알아야 한다.
    //       ① **사유 여섯 중 실제로 오는 것은 NotIdle 하나다.** (2026-09-14 실측)
    //          · NothingToClaim — 판정 분기는 ResolveRejectReason 에 있지만 **도달하지 않는다.**
    //            State==Rewarded ∧ 내가 개봉자 ∧ 수령 실패가 동시에 성립해야 하는데,
    //            IsRewardClaimed 를 세우는 곳이 같은 호출에서 State 를 Respawning 으로 바꿔
    //            그 조합이 만들어지지 않는다. 중복 수령도 NotIdle 로 나간다
    //            (ChestRejectionFeedbackContractTests.이미_가져간_보상을_다시_누르면_NotIdle_로_거부한다).
    //          · AlreadyOpeningAnother · RetryDelayed — 규칙은 ChestField/ChestRequestGate 에 있는데
    //            **그쪽이 라이브 경로에 배선돼 있지 않다** (O-032 → S15P21A606-85).
    //          · RoundEnding — 판정 입력(IsAcceptingNewOpens)이 라이브 경로에 없다.
    //          · NoSuchChest — 의도적으로 안 올린다(상호작용 키가 공용이라 문구가 쏟아진다).
    //       ② **한 사람이 상자 둘을 동시에 취소당하면 1건만 온다.** 복제 칸이 사람당 하나라
    //          마지막 상자번호·사유로 덮인다. 화면은 "내가 열던 것이 취소됐다" 로만 쓰고
    //          여기 오는 ChestId 로 개수를 세지 않는다.
    //       ③ Host→client 전달은 EditMode 로 검증할 수 없다. 2PC 인수가 남아 있다.
    //
    // 취소(ChestCancelReason)에는 짝이 되는 거부가 없다 —
    // 개봉 중이 아닐 때 취소는 "아무 일도 안 일어남" 이지 실패가 아니고,
    // 이동·피격으로 자동 발생하는 것이라 화면에 알릴 실패가 없다.
    // 취소가 *일어났다* 는 사실만 ChestOpenCancelled 로 알린다.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>상자 요청이 거부된 이유. <c>TreasureChest</c> 가 false 를 내는 조건에서만 뽑았다.</summary>
    public enum ChestRejectReason
    {
        /// <summary>그 번호의 상자가 맵에 없다. 화면과 도메인의 상자 목록이 어긋났다는 뜻이다.</summary>
        NoSuchChest = 0,

        /// <summary>
        /// 개봉을 시작할 수 없다 — 대기(<c>Idle</c>) 상태가 아니다.
        /// 이미 누가 열고 있거나, 보상 대기이거나, 재생성 중이다.
        /// <c>TreasureChest.TryBeginOpen</c> 이 false 를 내는 유일한 조건이다.
        /// </summary>
        NotIdle = 1,

        /// <summary>
        /// 수령할 보상이 없다 — 개봉이 끝나지 않았거나 <b>이미 받았다</b>.
        /// <c>TreasureChest.TryClaimReward</c> 가 false 를 내는 조건이다(중복 보상 방지).
        ///
        /// <para>
        /// ⚠️ <b>라이브 경로에서는 이 사유가 나오지 않는다</b> (2026-09-14 실측).
        /// <c>CombatChestAuthorityRuntime.ResolveRejectReason</c> 에 분기는 있으나
        /// <c>State==Rewarded</c> ∧ 내가 개봉자 ∧ 수령 실패가 동시에 성립하지 않는다 —
        /// <c>IsRewardClaimed</c> 를 세우는 유일한 지점이 같은 호출에서 <c>State</c> 를
        /// <c>Respawning</c> 으로 바꾼다. 중복 수령도 <c>NotIdle</c> 로 나간다.
        /// </para>
        /// </summary>
        NothingToClaim = 2,

        /// <summary>
        /// <b>내가 이미 다른 상자를 열고 있다</b> (S-GD-PTVC2X, 조영운 2026-09-04 확정).
        ///
        /// <para>
        /// <b>요청한 상자 자체는 <c>Idle</c> 이다.</b> 그래서 <see cref="NotIdle"/> 로 보내면
        /// 화면이 <b>아무도 안 연 상자 위에</b> "이미 누가 열고 있습니다" 라는 거짓 안내를 띄운다.
        /// 사유를 새로 만든 이유가 이것뿐이다 — <c>ChestCancelReason.RoundEnded</c> 를 따로 만든
        /// 것과 같은 근거다(같은 문구를 쓰면 플레이어가 자기가 뭘 잘못한 줄 안다).
        /// </para>
        ///
        /// <para>
        /// <b>같은 상자를 다시 요청한 경우는 여기로 오지 않는다.</b> 그것은 재전송이고
        /// <see cref="NotIdle"/> 로 답한다 — <c>ChestField.TryBeginOpen</c> 이 요청한 상자를
        /// 검사에서 뺀다.
        /// </para>
        ///
        /// ⚠️ <b>값은 뒤에만 붙인다.</b> 이 열거형은 <see cref="ChestActionRejected"/> 에 실려
        /// 회선을 넘는다 (<c>docs/client/ui-domain-contract.md</c> 계약 변경 절차).
        /// </summary>
        AlreadyOpeningAnother = 3,

        /// <summary>
        /// <b>취소당한 직후라 아직 이 상자를 다시 열 수 없다</b> (S-GD-5AZ6HN, 조영운 2026-09-04 확정).
        ///
        /// <para>
        /// 이동·피격으로 개봉이 끊기면 <b>그 사람은 그 상자를</b> 잠시 다시 열 수 없다.
        /// <b>다른 사람은 즉시 열 수 있고, 본인도 다른 상자는 바로 열 수 있다.</b>
        /// </para>
        ///
        /// <para>
        /// <see cref="NotIdle"/> 과 다른 이유는 <see cref="AlreadyOpeningAnother"/> 와 같다 —
        /// <b>그 상자는 비어 있다.</b> 그리고 플레이어가 취해야 할 행동이 다르다:
        /// 이쪽은 "잠깐 기다려라", 저쪽은 "열던 것을 마저 열어라" 다.
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b>라운드 종료로 끊긴 경우는 여기 해당하지 않는다.</b> 그것은 개봉자의 잘못이
        /// 아니므로 지연을 걸지 않는다.
        /// </para>
        /// </summary>
        RetryDelayed = 4,

        /// <summary>
        /// <b>라운드가 곧 끝나 새 개봉을 받지 않는다</b> (S-GD-DFG3VR, 조영운 2026-09-04 확정).
        ///
        /// <para>
        /// <b>자기장이 맵 전역을 덮는 순간</b>부터 새로 열 수 없다. 이미 진행 중인 개봉은
        /// 계속되고 완료되면 보상을 받는다 — <b>막는 것은 "시작" 뿐이다.</b>
        /// </para>
        ///
        /// <para>
        /// ⚠️ 이 사유는 <b>기다린다고 풀리지 않는다.</b> 다른 사유는 전부 "잠시 뒤 다시 해보라"
        /// 인데 이것만 "이번 라운드는 끝" 이다. 화면 문구가 그 차이를 드러내야 한다.
        /// </para>
        /// </summary>
        RoundEnding = 5,
    }

    /// <summary>
    /// 상자 요청이 거부됐다 — <b>개봉 시작</b>과 <b>보상 수령</b>의 공통 짝이다.
    ///
    /// 발행자는 <b>있다</b> (2026-09-14, S15P21A606-127). 한때 짝이던 <c>ChestOpenBeginCommand</c>·
    /// <c>ChestClaimRewardCommand</c> 는 아무도 쓰지 않아 <c>EconomyCommands.cs</c> 에서 지웠고,
    /// 상자는 전투 상호작용 심(<c>Data/Chest/IChestAuthorityRuntime</c>)으로 구현됐다.
    /// Host 판정부가 델리게이트로 알리고 → 사람별 <c>[Networked]</c> 로 되돌아가 →
    /// <c>OnlineCoreLoopWorldPresenter</c> 가 각 피어에서 이 이벤트로 올린다.
    ///
    /// ⚠️ <b>실제로 오는 사유는 <c>NotIdle</c> 하나뿐이다</b> (2026-09-14 실측).
    /// 나머지 다섯은 판정이 없거나 도달하지 않는다 — 이 파일 위쪽 블록 주석 참조.
    ///
    /// ⚠️ <b>구독자는 아직 0곳이다.</b> 발행은 되지만 화면에 띄우는 쪽이 없다.
    ///
    /// <b>둘을 한 이벤트로 묶은 이유</b>: 화면이 하는 일이 같다. 어느 쪽이든
    /// "그 상자에 지금은 안 된다" 를 그 상자 위에 띄우면 된다.
    /// 구분이 필요하면 <see cref="Reason"/> 으로 갈린다 — <c>NotIdle</c> 과
    /// <c>AlreadyOpeningAnother</c> 는 개봉, <c>NothingToClaim</c> 은 수령에서만 나온다.
    ///
    /// <b>왜 거부가 필요한가</b>: 상자는 <b>여러 사람이 동시에 노린다.</b> 남이 먼저 열기 시작하면
    /// 내 개봉 요청은 조용히 무시되는데, 화면에 아무 반응이 없으면 사용자는 버튼이 고장 난 줄 안다.
    /// (경쟁 처리 규칙 자체는 Jira S15P21A606-33 에서 다룬다.)
    /// </summary>
    public readonly struct ChestActionRejected : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>화면을 보는 사람의 것인가. 규약은 <see cref="GoldChanged.IsMine"/> 참조 (§7).</summary>
        public readonly bool IsMine;

        /// <summary>거부된 요청의 상자 번호. <see cref="ChestRejectReason.NoSuchChest"/> 여도 채운다.</summary>
        public readonly int ChestId;

        public readonly ChestRejectReason Reason;

        public ChestActionRejected(int playerId, bool isMine, int chestId, ChestRejectReason reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            ChestId = chestId;
            Reason = reason;
        }
    }
}
