using BettingRoyale.Core;

namespace BettingRoyale.Data.Contracts
{
    // 이 파일의 명령은 전부 화면 → 도메인 방향이다. 규약은 docs/client/ui-domain-contract.md §4·§5.
    //
    // ⚠️ 명령에는 "누가 보냈는가" 를 담지 않는다 (§5).
    //    목업에서 보내는 쪽이 자기 id 를 적어 넣는 구조 때문에 남의 id 를 적으면 남의 골드를 쓸 수 있었다.
    //    칸이 있는 한 실수든 고의든 가능하므로, 칸 자체를 없앤다.
    //    누가 보냈는지는 어댑터가 연결 정보에서 판단한다 (Host Mode 에서는 RPC 의 송신자).
    //
    // ⚠️ 전부 IGameEvent 를 구현한다 (§4).
    //    EventBus.Publish<T> 에 where T : IGameEvent 제약이 있어서, 이게 없으면 화면이
    //    명령을 보낼 방법이 아예 없다. 붙여두면 컴파일러가 규약을 대신 강제한다.

    /// <summary>
    /// 축복 후보의 잠금을 켜고 끈다 (S-GD-S46S28, S-GD-VN5VD7). 무료지만 최대 2개다.
    /// </summary>
    public readonly struct BlessingLockToggleCommand : IGameEvent
    {
        /// <summary>0부터 시작하는 후보 번호. 후보는 항상 3개다 (S-GD-S46S28).</summary>
        public readonly int CandidateIndex;

        public BlessingLockToggleCommand(int candidateIndex) => CandidateIndex = candidateIndex;
    }

    /// <summary>
    /// 잠그지 않은 후보를 전부 다시 뽑는다 (S-GD-S46S28, S-GD-SHJGNB). 유료다.
    /// 다시 뽑히는 개수는 1~3개다 — 잠금이 최대 2개이므로.
    /// </summary>
    /// <remarks>
    /// 필드가 없다. 무엇을 재추첨할지는 도메인이 안다 — 화면은 "지금 내 후보를 다시 뽑아달라" 만 말한다.
    /// </remarks>
    public readonly struct BlessingRerollCommand : IGameEvent
    {
    }

    /// <summary>축복 하나를 선택해 준비 단계를 끝낸다 (S-GD-S46S28).</summary>
    public readonly struct BlessingSelectCommand : IGameEvent
    {
        /// <summary>0부터 시작하는 후보 번호.</summary>
        public readonly int CandidateIndex;

        public BlessingSelectCommand(int candidateIndex) => CandidateIndex = candidateIndex;
    }

    // ─────────────────────────────────────────────────────────────
    // 상자 명령은 여기 없다 — 다른 심으로 갔다 (S15P21A606-93, 2026-09-07)
    // ─────────────────────────────────────────────────────────────
    //
    // 한때 이 파일에 ChestOpenBeginCommand · ChestOpenCancelCommand · ChestClaimRewardCommand
    // 세 개가 있었다. 규약 문서(§6)도 그 셋을 명령 표에 올렸다. 그런데 **아무도 쓰지 않았다** —
    // 발행자 0, 구독자 0, 참조는 같은 폴더 안 XML 주석뿐이었다.
    //
    // 상자가 구현되지 않아서가 아니다. **다른 경로로 구현됐다.**
    //
    //   화면 → EventBus 명령        (이 파일)          ← 축복 3종은 이쪽
    //   전투 상호작용 → CombatantId (Data/Chest/)      ← 상자는 이쪽
    //
    // 상자는 "화면에서 버튼을 누르는 것" 이 아니라 "월드에서 상자에 다가가 상호작용하는 것" 이라
    // 입력이 화면이 아니라 전투에서 온다. 그래서 CombatantId 를 쓰는 별도 심이 생겼다:
    //   Data/Chest/IChestAuthorityRuntime   — Host 권위 tick·cancel
    //   Data/Chest/IChestPlayerIdMapper     — CombatantId → PlayerId (둘이 같다고 가정하지 않는다)
    //   Data/Chest/IChestRewardAdapter      — 확정된 수령을 관찰
    // 복제는 NetworkCombatantMotion 의 [Networked] 상자 상태이고, Host 판정은
    // Gameplay/Combat/CombatChestAuthorityRuntime 이 돈다.
    //
    // 명령 struct 를 남겨두면 "이걸 쓰면 되겠네" 하고 두 번째 경로를 다시 만들게 된다.
    // 그래서 지운다. 상자에서 화면으로 오는 방향(ChestsChanged · ChestRewardClaimed)은
    // 계약 그대로 살아 있고 ChestEvents.cs 에 있다.
    //
    // ⚠️ IEconomyCommandHandler 도 함께 지웠다. 구현체가 하나도 없었다.
    //    주석은 "Fusion 어댑터가 구현한다" 고 지목했지만 실제 OnlineEconomyCoordinator 는
    //    IEconomyStateSink + 자체 EconomyCommandKind 전송으로 갔다(D-069 신뢰 데이터 스냅샷).
    //    인터페이스와 구현이 서로를 모르는 상태로 남아 있었다. 명령을 모으는 자리는
    //    어댑터의 EventBus 구독부(OnlineEconomyCoordinator)이고, 그게 실제로 동작하는 쪽이다.
    //    이 판단으로 O-022("명령 핸들러를 어느 단위로 쪼개는가")가 닫힌다.
}
