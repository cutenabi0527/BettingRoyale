using System;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Preparation;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화 요청 하나를 <b>끝까지 판정하는 유일한 자리</b> (S-GD-A2YDDJ, S-GD-Z9CJ4H, S-CLI-9QYFCT).
    ///
    /// <para>
    /// <c>ShopRequestGate</c>·<c>RouletteRequestGate</c> 와 같은 모양이다 — 어댑터는 결과를 받아
    /// <b>그대로 발행만</b> 하고 규칙을 다시 구현하지 않는다 (D-069, D-079).
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 원장은 상점·룰렛·축복과 같은 것을 쓴다.</b> 순번은 명령 종류와 무관한 하나짜리
    /// 카운터라(<c>EconomyRequestTicket.Sequence</c>) 강화만 따로 두면 순번이 갈린다. 그리고
    /// <b>여기서 <c>Accept</c> 를 부르므로 어댑터는 부르면 안 된다</b> — 두 번 부르면 첫 호출이
    /// 순번을 먹어 이 게이트가 언제나 <c>Duplicate</c> 를 보고 모든 요청이 조용히 무시된다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 모르는 부위는 예외가 아니라 거절이다.</b> 회선에서 온 값이라
    /// <c>(UpgradeSlot)7</c> 이 실제로 올 수 있고, 그것이 도메인까지 가면
    /// <c>UpgradeState</c> 가 던진다 — 그 예외는 <c>EventBus</c> 구독부에서 로그 한 줄로 삼켜져
    /// <b>"눌렀는데 아무 일도 안 일어난다"</b> 가 된다. 여기서 <see cref="UpgradeRejectReason.UnknownSlot"/>
    /// 으로 돌려주고 <b>지갑도 단계도 건드리지 않는다.</b>
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 축복을 확정하기 전에는 강화를 쓸 수 없다</b> (S-GD-DP2DVC — *"선택 중 이동과 다른
    /// 상호작용이 막힌다"*). 이 판정은 한때 <b>Network 어댑터에만</b> 있었고, 그래서 같은 게이트를
    /// 쓰는 훈련 경로에는 규칙이 없었다. 지금은 <see cref="IPreparationSelectionPolicy"/> 로
    /// <b>게이트가 직접 본다</b> — 어댑터가 하나 더 붙어도 빠뜨릴 자리가 없다.
    ///
    /// <b>⚠️ 어댑터의 같은 판정은 아직 지우지 않았다.</b> <c>OnlineEconomyCoordinator</c> 가
    /// 명령 경로와 상태 방송 경로에서 <c>IsSelectionDone</c> 을 직접 한 번 더 본다 —
    /// <b>지금은 두 겹이고 답은 같다.</b> 그 파일은 이 작업의 범위 밖이라 손대지 않았다.
    /// 어댑터 쪽을 지우는 것은 그 파일을 소유한 작업의 몫이다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <paramref name="playerId"/> 는 <b>반드시 RPC 입력 권한에서 꺼낸 값</b>이어야 한다.
    /// 페이로드에서 온 값을 넘기면 규약 §5 가 없앤 "남의 골드를 쓰는" 경로가 되살아난다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class UpgradeRequestGate
    {
        private readonly MatchEconomyState economy;
        private readonly EconomyRequestLedger ledger;
        private readonly IPreparationAccessPolicy access;
        private readonly IPreparationSelectionPolicy selection;

        /// <param name="access">
        /// 모루 구역 안에 있는지 판정한다 (S-GD-66GTWA · S-CLI-9QYFCT). <b>Host 가 직접 재는 자리다</b> —
        /// 화면이 "가까이 있다" 고 보낸 값을 믿으면 맵 반대편에서 강화할 수 있다.
        /// </param>
        /// <remarks>
        /// 축복 선택 확정 판정은 <see cref="MatchEconomySelectionPolicy"/> 로 <b>경제 상태를 그대로
        /// 본다</b>. 제품 경로가 쓰는 기본값이다 — 다른 답이 필요한 경로는 아래 생성자를 쓴다.
        /// </remarks>
        public UpgradeRequestGate(
            MatchEconomyState economy,
            EconomyRequestLedger ledger,
            IPreparationAccessPolicy access)
            : this(economy, ledger, access, null)
        {
        }

        /// <param name="selection">
        /// 이번 준비 단계의 축복을 확정했는지 판정한다 (S-GD-DP2DVC — *"선택 중 이동과 다른
        /// 상호작용이 막힌다"*). <c>null</c> 이면 경제 상태를 그대로 보는 기본 판정을 쓴다.
        ///
        /// <para>
        /// <b>⚠️ 제품 축복 추첨을 돌리지 않는 경로만 다른 값을 넣는다.</b> 훈련 세션이 그렇다 —
        /// 연습 축복은 경제의 후보를 확정하지 않으므로 기본 판정을 쓰면 모루가 영영 안 열린다.
        /// </para>
        /// </param>
        public UpgradeRequestGate(
            MatchEconomyState economy,
            EconomyRequestLedger ledger,
            IPreparationAccessPolicy access,
            IPreparationSelectionPolicy selection)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.access = access ?? throw new ArgumentNullException(nameof(access));
            this.selection = selection ?? new MatchEconomySelectionPolicy(this.economy);
        }

        /// <summary>
        /// 두 부위의 현재 상태. 준비 단계가 열릴 때와 값이 바뀔 때 어댑터가 이걸 발행한다.
        ///
        /// <b>여기의 <c>CanUpgrade</c> 와 <see cref="HandleUpgrade(int, int, EconomyRequestTicket, bool)"/>
        /// 의 판정은 같은 조건을 본다.</b> 어긋나면 "밝게 켜져 있는데 눌리지 않는 버튼" 이 생긴다.
        /// </summary>
        public UpgradeStateChanged BuildState(int playerId, bool isMine)
        {
            UpgradeStateChanged state = economy.BuildUpgradeState(playerId, isMine);

            // ⚠️ 선택 확정이 구역보다 먼저다 — HandleUpgrade 와 같은 순서여야 한다.
            //    선택 중에는 이동이 막혀 있어(S-GD-DP2DVC) 어차피 구역 밖이므로, "구역 밖" 이라고
            //    답하면 **실제로 해야 할 일(축복 고르기)을 가린다.**
            if (RequiresSelection(playerId))
            {
                return state.WithBlockedIfOpen(UpgradeRejectReason.SelectionRequired);
            }

            return access.CanUse(playerId, PreparationFacility.Upgrade)
                ? state
                : state.WithBlockedIfOpen(UpgradeRejectReason.NotInRange);
        }

        /// <summary>부위를 아는 값으로 받는 경로. 모르는 값이면 <see cref="UpgradeRejectReason.UnknownSlot"/>.</summary>
        public UpgradeRequestOutcome HandleUpgrade(
            int playerId, UpgradeSlot slot, EconomyRequestTicket ticket, bool isMine) =>
            HandleUpgrade(playerId, UpgradeRequestPayload.Pack(slot), ticket, isMine);

        /// <summary>
        /// 강화 요청. <b>같은 요청을 두 번 보내도 골드는 한 번만 나가고 단계도 한 번만 오른다.</b>
        /// </summary>
        /// <param name="slotPayload">
        /// 전송 한 칸에 담긴 부위 (<c>UpgradeRequestPayload</c>). <b>푸는 자리는 여기 하나뿐이다.</b>
        /// </param>
        public UpgradeRequestOutcome HandleUpgrade(
            int playerId, int slotPayload, EconomyRequestTicket ticket, bool isMine)
        {
            if (!IsParticipant(playerId))
            {
                return default;
            }

            // ⚠️ 원장은 판정보다 먼저 소비한다. 거절된 요청도 "처리했다" 로 기록된다 —
            //    이유는 EconomyRequestVerdict.Duplicate 주석에 있다. 모르는 부위도 마찬가지다:
            //    기록하지 않으면 같은 순번을 다시 보내 판정을 계속 시켜볼 수 있다.
            EconomyRequestVerdict verdict = ledger.Accept(playerId, ticket);

            if (verdict != EconomyRequestVerdict.Fresh)
            {
                // 재전송·지난 요청 — 거절이 아니라 "현재 확정 상태" 를 돌려준다.
                return Snapshot(verdict, playerId, isMine, delta: 0);
            }

            bool known = UpgradeRequestPayload.TryUnpack(slotPayload, out UpgradeSlot slot);

            // 처음 보는 요청인데 단계가 닫혀 있다 — 재전송과 달리 **알려야 한다**.
            if (economy.Phase != MatchEconomyPhase.Preparation)
            {
                return Reject(
                    EconomyRequestVerdict.PreparationClosed,
                    playerId, isMine, slot, UpgradeRejectReason.NotPreparation);
            }

            // 이번 라운드의 축복을 확정했는가 (S-GD-DP2DVC — "선택 중 이동과 다른 상호작용이 막힌다").
            //
            // ⚠️ 이 판정이 어댑터에만 있었다. 그래서 같은 게이트를 쓰는 훈련 경로에는 규칙이 없었고,
            //    어댑터를 하나 더 붙이면 또 빠뜨릴 자리가 됐다 — 그래서 게이트가 직접 본다.
            //    ⚠️ 어댑터(OnlineEconomyCoordinator)의 사본은 아직 안 지웠다. 그 파일은 이 작업의
            //    범위 밖이라 **지금은 두 겹**이고, 답이 같아 동작은 바뀌지 않는다.
            //
            // ⚠️ 구역 판정보다 앞이다. 선택 중에는 이동이 막혀 어차피 구역 밖이므로, 순서를 뒤집으면
            //    "모루에서 멀어졌습니다" 라는 **할 수 없는 일**을 안내하게 된다.
            if (RequiresSelection(playerId))
            {
                return Reject(verdict, playerId, isMine, slot, UpgradeRejectReason.SelectionRequired);
            }

            // 모루 구역 안에 서 있는가 — Host 가 직접 잰다 (S-CLI-9QYFCT).
            // ⚠️ 부위를 풀기 전에 본다. 모르는 부위로 보내면 위치 판정을 건너뛰는 경로가 되면 안 된다.
            if (!access.CanUse(playerId, PreparationFacility.Upgrade))
            {
                return Reject(verdict, playerId, isMine, slot, UpgradeRejectReason.NotInRange);
            }

            if (!known || !UpgradeRequestPayload.IsKnown(slot))
            {
                // 정상 클라이언트는 만들 수 없는 값이다. **아무것도 바꾸지 않고** 사유만 돌려준다.
                return Reject(verdict, playerId, isMine, slot, UpgradeRejectReason.UnknownSlot);
            }

            int before = economy.GetGold(playerId);
            UpgradeAttemptResult result = economy.TryUpgradeSlot(playerId, slot);

            switch (result)
            {
                case UpgradeAttemptResult.Success:
                    // 성공 이벤트는 없다 — 바뀐 단계와 잔액이 성공이다 (UpgradeStateChanged 주석).
                    return Snapshot(verdict, playerId, isMine, economy.GetGold(playerId) - before);

                case UpgradeAttemptResult.Failed:
                    // 확률 실패 (S15P21A606-304). **거절이 아니다** — 시도는 이루어졌고 골드는 나갔다.
                    // 실패 이벤트도 따로 없다: 단계 그대로 + 잔액 감소가 곧 실패다(S-UX-HQ92B5).
                    // ⚠️ 여기서 Reject 로 돌리면 화면이 "판정 전에 막혔다" 로 읽어 결과 비교를 취소한다.
                {
                    UpgradeRequestOutcome snapshot =
                        Snapshot(verdict, playerId, isMine, economy.GetGold(playerId) - before);

                    return new UpgradeRequestOutcome(
                        snapshot.Verdict,
                        hasEvents: true,
                        gold: snapshot.Gold,
                        state: snapshot.State,
                        hasRejection: false,
                        rejection: default,
                        chanceFailed: true);
                }

                case UpgradeAttemptResult.NotEnoughGold:
                    return Reject(verdict, playerId, isMine, slot, UpgradeRejectReason.NotEnoughGold);

                case UpgradeAttemptResult.MaxLevelReached:
                    return Reject(verdict, playerId, isMine, slot, UpgradeRejectReason.MaxLevelReached);

                default:
                    // NoTrack — 이 무기의 표가 없다. 조작이 아니라 데이터가 없는 것이다.
                    return Reject(verdict, playerId, isMine, slot, UpgradeRejectReason.NoTrack);
            }
        }

        /// <summary>거절·재전송에도 현재 상태를 실어 보낸다 — 화면이 이벤트 하나로 다시 그려지게.</summary>
        private UpgradeRequestOutcome Snapshot(
            EconomyRequestVerdict verdict, int playerId, bool isMine, int delta) =>
            new UpgradeRequestOutcome(
                verdict,
                hasEvents: true,
                gold: economy.BuildGoldChanged(playerId, isMine, delta, GoldChangeReason.Spent),
                state: BuildState(playerId, isMine),
                hasRejection: false,
                rejection: default);

        private UpgradeRequestOutcome Reject(
            EconomyRequestVerdict verdict,
            int playerId,
            bool isMine,
            UpgradeSlot slot,
            UpgradeRejectReason reason)
        {
            UpgradeRequestOutcome snapshot = Snapshot(verdict, playerId, isMine, delta: 0);

            return new UpgradeRequestOutcome(
                snapshot.Verdict,
                hasEvents: true,
                gold: snapshot.Gold,
                state: snapshot.State,
                hasRejection: true,
                rejection: new UpgradeSlotRejected(playerId, isMine, slot, reason));
        }

        /// <summary>
        /// 아직 축복을 확정하지 않아 강화가 막혀 있는가.
        ///
        /// <para>
        /// <b>준비 단계일 때만 묻는다.</b> 전투 중에는 축복 추첨 자체가 없어 언제나 "안 골랐다" 가
        /// 되는데, 그때 화면에 "먼저 축복을 선택해주세요" 를 띄우면 <b>할 수 없는 일을 시키는 안내</b>
        /// 가 된다 — 그 상황의 사유는 <see cref="UpgradeRejectReason.NotPreparation"/> 이다.
        /// </para>
        /// </summary>
        private bool RequiresSelection(int playerId) =>
            economy.Phase == MatchEconomyPhase.Preparation && !selection.IsSelectionConfirmed(playerId);

        private bool IsParticipant(int playerId)
        {
            var ids = economy.PlayerIds;

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == playerId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
