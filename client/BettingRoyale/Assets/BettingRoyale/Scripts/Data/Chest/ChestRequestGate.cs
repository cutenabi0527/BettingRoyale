using System;
using System.Collections.Generic;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// 상자 요청 하나를 처리한 결과. 어댑터는 <b>판단하지 않고 그대로 발행</b>한다.
    ///
    /// <para>
    /// 여러 이벤트가 한 번에 나올 수 있다 — 개봉이 끝난 틱에는 보상·골드·목록이 함께 바뀐다.
    /// <c>Has~</c> 를 보고 있는 것만 발행하면 된다.
    /// </para>
    /// </summary>
    public readonly struct ChestRequestOutcome
    {
        /// <summary><c>false</c> 면 라운드가 안 열려 있어 아무것도 발행하지 않는다.</summary>
        public readonly bool HasEvents;

        /// <summary>항상 발행한다. 상자 <b>전체 목록</b>이라 하나를 놓쳐도 다음 이벤트가 복구한다.</summary>
        public readonly ChestsChanged Chests;

        public readonly bool HasRejection;
        public readonly ChestActionRejected Rejection;

        /// <summary>개봉이 끊겼을 때. 화면이 안내를 지울 수 있게 사유를 싣는다.</summary>
        public readonly bool HasCancellation;
        public readonly ChestOpenCancelled Cancellation;

        /// <summary>보상이 확정됐을 때. 골드는 <see cref="Gold"/> 로 따로 간다.</summary>
        public readonly bool HasReward;
        public readonly ChestRewardClaimed Reward;

        /// <summary>보상으로 골드가 늘었을 때.</summary>
        public readonly bool HasGold;
        public readonly GoldChanged Gold;

        internal ChestRequestOutcome(
            bool hasEvents,
            ChestsChanged chests,
            bool hasRejection = false,
            ChestActionRejected rejection = default,
            bool hasCancellation = false,
            ChestOpenCancelled cancellation = default,
            bool hasReward = false,
            ChestRewardClaimed reward = default,
            bool hasGold = false,
            GoldChanged gold = default)
        {
            HasEvents = hasEvents;
            Chests = chests;
            HasRejection = hasRejection;
            Rejection = rejection;
            HasCancellation = hasCancellation;
            Cancellation = cancellation;
            HasReward = hasReward;
            Reward = reward;
            HasGold = hasGold;
            Gold = gold;
        }
    }

    /// <summary>
    /// <b>Host 가 받은 상자 요청을 판정하고, 화면에 보낼 이벤트를 완성해 돌려준다</b>
    /// (Jira S15P21A606-32).
    ///
    /// <para>
    /// <c>-31</c> 의 <see cref="EconomyRequestGate"/> 와 같은 자리·같은 모양이다 —
    /// 어댑터에는 판단이 한 줄도 남지 않는다:
    /// </para>
    ///
    /// <code>
    /// var r = gate.HandleOpenBegin(playerId, command.ChestId, IsMine(playerId));
    /// if (!r.HasEvents) return;
    /// if (r.HasRejection) EventBus.Publish(r.Rejection);
    /// EventBus.Publish(r.Chests);
    /// </code>
    ///
    /// <para>
    /// ⚠️ <b>이 클래스는 발행하지 않는다.</b> <c>EventBus</c> 가 <c>UnityEngine</c> 을 참조하므로
    /// <c>Scripts/Data/</c> 에서는 부를 수 없다 (D-060).
    /// </para>
    ///
    /// <para>
    /// ⚠️ <c>playerId</c> 는 <b>RPC 입력 권한에서 꺼낸 값</b>이어야 한다. 페이로드에서 온 값을 넘기면
    /// 규약 §5 가 없앤 "남의 것으로 조작하는" 경로가 되살아난다.
    /// </para>
    ///
    /// <para>
    /// <b>보상은 <c>MatchEconomyState.Earn</c> 이라는 기존 입구로 넣는다.</b> 티켓이
    /// *"-31 의 골드·정산 계약을 변경하지 않고 확장한다"* 를 못박았으므로 새 경로를 만들지 않는다.
    /// </para>
    /// </summary>
    public sealed class ChestRequestGate
    {
        private readonly ChestField field;
        private readonly MatchEconomyState economy;
        private readonly int ticksPerSecond;
        private readonly int openDurationTicks;

        /// <param name="ticksPerSecond">
        /// 어댑터가 Fusion 런타임에서 읽어 넣는다. 화면이 이벤트 사이를 보간하는 데 쓴다
        /// (<c>ui-domain-contract.md</c> §8). <b>도메인은 이 값을 알 방법이 없다.</b>
        /// </param>
        public ChestRequestGate(
            ChestField field,
            MatchEconomyState economy,
            ChestFieldConfig config,
            int ticksPerSecond)
        {
            this.field = field ?? throw new ArgumentNullException(nameof(field));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            this.ticksPerSecond = ticksPerSecond;
            openDurationTicks = config.Chest.OpenDurationTicks;
        }

        /// <summary>개봉 시작 요청. 완료 조건 3 — 이미 누가 열고 있으면 거부한다.</summary>
        public ChestRequestOutcome HandleOpenBegin(int playerId, int chestId, bool isMine)
        {
            if (!IsParticipant(playerId) || !field.IsRoundOpen)
            {
                return default;
            }

            bool started = field.TryBeginOpen(chestId, playerId);

            return new ChestRequestOutcome(
                hasEvents: true,
                chests: BuildChests(playerId),
                hasRejection: !started,
                rejection: new ChestActionRejected(playerId, isMine, chestId, RejectReasonFor(playerId, chestId)));
        }

        /// <summary>
        /// 개봉 취소 요청 (이동·피격). 완료 조건 2 — 취소되면 보상이 지급되지 않는다.
        ///
        /// <para>
        /// <b>취소에는 거부가 없다.</b> 열고 있지 않은데 취소가 와도 그건 실패가 아니라
        /// "아무 일도 안 일어남" 이다 (<c>ui-domain-contract.md</c> §6 의 판단과 같다).
        /// </para>
        /// </summary>
        public ChestRequestOutcome HandleOpenCancel(int playerId, ChestCancelReason reason, bool isMine)
        {
            if (!IsParticipant(playerId) || !field.IsRoundOpen)
            {
                return default;
            }

            var cancelled = field.CancelOpenBy(playerId, reason);

            if (cancelled.Count == 0)
            {
                return new ChestRequestOutcome(hasEvents: true, chests: BuildChests(playerId));
            }

            // 한 사람이 동시에 두 상자를 열 수는 없으므로 취소도 최대 하나다.
            var one = cancelled[0];

            return new ChestRequestOutcome(
                hasEvents: true,
                chests: BuildChests(playerId),
                hasCancellation: true,
                cancellation: new ChestOpenCancelled(one.ChestId, one.OpenerId, one.Reason));
        }

        /// <summary>
        /// 한 틱 진행한다. 개봉이 완료되면 <b>보상을 경제에 넣고</b> 이벤트를 완성해 돌려준다.
        ///
        /// <para>
        /// 보상 수령은 개봉 완료 즉시 자동이다 (S-GD-GPXVNR, ⚠️ <c>assumed</c>).
        /// 그래서 <c>ChestClaimRewardCommand</c> 는 지금 쓰이지 않는다 — 계약은 남겨둔다.
        /// </para>
        /// </summary>
        /// <param name="viewerId">
        /// 이 결과를 볼 사람. <c>IsMine</c> 과 <c>GoldChanged</c> 의 주인을 정한다.
        /// Host 가 피어마다 한 번씩 부르거나, 한 번 불러 어댑터가 다시 채운다.
        /// </param>
        public ChestRequestOutcome Tick(int viewerId, bool isMine)
        {
            if (!IsParticipant(viewerId) || !field.IsRoundOpen)
            {
                return default;
            }

            var earned = field.Tick();

            if (earned.Count == 0)
            {
                return new ChestRequestOutcome(hasEvents: true, chests: BuildChests(viewerId));
            }

            // 보상은 전원 몫을 반영한다 — 내 화면에 안 보여도 남의 골드는 실제로 늘어난다.
            ChestRewardEarned? mine = null;
            for (int i = 0; i < earned.Count; i++)
            {
                economy.Earn(earned[i].ClaimerId, earned[i].Gold);

                if (earned[i].ClaimerId == viewerId)
                {
                    mine = earned[i];
                }
            }

            var chests = BuildChests(viewerId);

            if (mine == null)
            {
                // 남이 먹었다. 목록은 바뀌었지만 내 골드는 그대로다.
                var other = earned[0];
                return new ChestRequestOutcome(
                    hasEvents: true,
                    chests: chests,
                    hasReward: true,
                    reward: new ChestRewardClaimed(other.ChestId, other.ClaimerId));
            }

            var got = mine.Value;

            return new ChestRequestOutcome(
                hasEvents: true,
                chests: chests,
                hasReward: true,
                reward: new ChestRewardClaimed(got.ChestId, got.ClaimerId),
                hasGold: true,
                gold: economy.BuildGoldChanged(viewerId, isMine, got.Gold, GoldChangeReason.Earned));
        }

        /// <summary>화면이 그대로 그릴 수 있는 현재 상자 목록.</summary>
        public ChestsChanged BuildChests(int viewerId)
        {
            var placements = field.BuildPlacements();
            var snapshots = new ChestSnapshot[placements.Count];

            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                snapshots[i] = new ChestSnapshot(
                    chestId: p.ChestId,
                    state: p.State,
                    openerId: p.OpenerId,
                    isMine: p.OpenerId == viewerId && p.OpenerId != TreasureChest.NoOpener,
                    progress: p.Progress,
                    elapsedTicks: p.ElapsedTicks,
                    openDurationTicks: openDurationTicks,
                    position: p.Position);
            }

            return new ChestsChanged(snapshots, ticksPerSecond);
        }

        /// <summary>
        /// 이 매치의 참가자인가. <see cref="EconomyRequestGate"/> 와 <b>같은 가드다</b>.
        ///
        /// <para>
        /// ⚠️ <b>없으면 두 갈래로 깨진다.</b>
        /// 음수 id 는 <see cref="TreasureChest.TryBeginOpen"/> 이 <c>false</c> 가 아니라
        /// <b>예외를 던지고</b>(<c>openerId &lt; 0</c>), 그 예외가 <c>EventBus</c> 핸들러에서 삼켜져
        /// 화면이 무반응이 된다. 모르는 양수 id 는 남의 상자를 열거나 취소할 수 있다.
        /// </para>
        ///
        /// <para>
        /// 팀은 이미 반대 원칙을 세워 뒀다 — <c>없는_상자_번호는_예외가_아니라_거부다</c>.
        /// 참가자 검사도 같은 자리에서 같은 방식으로 막는다.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// 개봉 시작이 실패한 <b>이유</b>를 관측 가능한 상태에서 고른다.
        /// 침묵하면 화면이 "거부당한 것" 과 "아직 안 온 것" 을 구분할 수 없다 (규약 §6).
        ///
        /// <para>
        /// ⚠️ <b>검사 순서가 규칙이다.</b> 요청한 상자가 <c>Idle</c> 인지를 <b>먼저</b> 본다.
        /// 뒤집으면 같은 상자 재전송이 <see cref="ChestRejectReason.AlreadyOpeningAnother"/> 로
        /// 나가는데, 재전송은 "두 번째 상자" 가 아니라서 사유가 거짓이 된다.
        /// </para>
        /// </summary>
        private ChestRejectReason RejectReasonFor(int playerId, int chestId)
        {
            var placements = field.BuildPlacements();

            if (chestId < 0 || chestId >= placements.Count)
            {
                return ChestRejectReason.NoSuchChest;
            }

            // 요청한 상자가 이미 누군가에게 잡혀 있으면 그것이 먼저다 — 재전송도 여기로 온다.
            if (placements[chestId].State != ChestState.Idle)
            {
                return ChestRejectReason.NotIdle;
            }

            // 상자는 비어 있다. 남은 가능성은 셋이고, 도메인의 검사 순서와 같게 본다.
            // 추론하지 않고 직접 확인한다 — 나중에 거부 사유가 더 늘어도 이 답은 틀리지 않는다.

            // ⓪ 라운드가 곧 끝나 새 개봉을 안 받는다 (S-GD-DFG3VR).
            //    기다린다고 풀리지 않으므로 가장 먼저 알려준다.
            if (!field.IsAcceptingNewOpens)
            {
                return ChestRejectReason.RoundEnding;
            }

            // ① 취소당한 직후라 이 상자에 쿨다운이 걸려 있다 (S-GD-5AZ6HN).
            if (field.IsRetryDelayedFor(chestId, playerId))
            {
                return ChestRejectReason.RetryDelayed;
            }

            // ② 내가 다른 상자를 열고 있다 (S-GD-PTVC2X).
            for (int i = 0; i < placements.Count; i++)
            {
                if (i == chestId)
                {
                    continue;
                }

                if (placements[i].State == ChestState.Opening && placements[i].OpenerId == playerId)
                {
                    return ChestRejectReason.AlreadyOpeningAnother;
                }
            }

            // 여기까지 왔다면 도메인이 거부한 이유를 게이트가 재현하지 못한 것이다.
            // 가장 무해한 답을 준다 — 화면은 어차피 "지금은 안 된다" 를 띄운다.
            return ChestRejectReason.NotIdle;
        }
    }
}
