using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Chest
{
    /// <summary>한 상자의 현재 사실. 화면 계약(<c>ChestSnapshot</c>)으로 옮기기 직전의 도메인 값이다.</summary>
    public readonly struct ChestPlacement
    {
        /// <summary>이 라운드 안에서 유일한 상자 번호. 0부터 시작한다.</summary>
        public readonly int ChestId;

        /// <summary>몇 번째 앵커에 놓였는가. 재현성 확인에 쓴다.</summary>
        public readonly int AnchorIndex;

        public readonly WorldPosition Position;
        public readonly ChestState State;
        public readonly int OpenerId;
        public readonly int ElapsedTicks;
        public readonly float Progress;
        public readonly bool IsRewardClaimable;

        internal ChestPlacement(
            int chestId,
            int anchorIndex,
            WorldPosition position,
            ChestState state,
            int openerId,
            int elapsedTicks,
            float progress,
            bool isRewardClaimable)
        {
            ChestId = chestId;
            AnchorIndex = anchorIndex;
            Position = position;
            State = state;
            OpenerId = openerId;
            ElapsedTicks = elapsedTicks;
            Progress = progress;
            IsRewardClaimable = isRewardClaimable;
        }
    }

    /// <summary>개봉이 완료돼 보상이 확정된 건. 필드가 <see cref="ChestField.Tick"/> 에서 모아 돌려준다.</summary>
    public readonly struct ChestRewardEarned
    {
        public readonly int ChestId;
        public readonly int ClaimerId;

        /// <summary>
        /// 실제로 나온 것. 지금은 골드만 담기지만 <see cref="ChestReward"/> 를 통째로 나르므로
        /// 소모품·장신구가 생겨도 이 구조체의 시그니처는 바뀌지 않는다.
        /// </summary>
        public readonly ChestReward Reward;

        /// <summary>보상 중 골드분. <c>Reward.Gold</c> 의 지름길이다.</summary>
        public int Gold => Reward.Gold;

        internal ChestRewardEarned(int chestId, int claimerId, ChestReward reward)
        {
            ChestId = chestId;
            ClaimerId = claimerId;
            Reward = reward;
        }
    }

    /// <summary>취소된 개봉 건. 화면이 "왜 끊겼는지" 를 말할 수 있어야 한다 (규약 §6).</summary>
    public readonly struct ChestOpenCancellation
    {
        public readonly int ChestId;
        public readonly int OpenerId;
        public readonly ChestCancelReason Reason;

        internal ChestOpenCancellation(int chestId, int openerId, ChestCancelReason reason)
        {
            ChestId = chestId;
            OpenerId = openerId;
            Reason = reason;
        }
    }

    /// <summary>
    /// <b>한 전투 라운드의 상자들을 소유한다</b> (Jira S15P21A606-32, S-GD-MQP6AS, S-GD-5Y4PTJ).
    ///
    /// <para>
    /// <see cref="TreasureChest"/> 는 <b>상자 하나</b>의 상태 기계다. 이 타입은 그 위에서
    /// <b>몇 개를 · 어디에 · 언제까지</b> 를 책임진다 — 앵커 선택, <c>ChestId</c> 발급,
    /// 라운드 시작·종료 수명, 보상 수집.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="TreasureChest"/> 를 고치지 않는다.</b> 그쪽은 이미 테스트로 잠긴 상태 기계이고,
    /// 여기서 필요한 것은 그 위의 관리다.
    /// </para>
    ///
    /// <para>
    /// <b>모든 무작위는 <see cref="DeterministicRandom"/> 으로만 한다.</b> Host 가 뽑아 복제하는
    /// 값이므로 같은 시드면 같은 두 자리가 나와야 한다 — 완료 조건 1("Host/Client 위치 일치")의
    /// 도메인 쪽 근거가 이것이다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class ChestField
    {
        private sealed class Slot
        {
            // 자동 프로퍼티로 둔다. lint 가 public 필드를 Inspector 노출로 보고 경고하는데,
            // 이 클래스는 private 중첩이라 오탐이다 — 다만 MatchRoundTally.Row 가 같은 이유로
            // 이미 프로퍼티를 쓰고 있어 관례를 맞춘다.
            public TreasureChest Chest { get; set; }

            public int AnchorIndex { get; set; }

            public WorldPosition Position { get; set; }

            /// <summary>
            /// 이 상자를 열다 취소당한 사람의 <b>남은 재시도 지연 틱</b> (S-GD-5AZ6HN).
            /// 없는 사람은 항목 자체가 없다.
            ///
            /// <para>
            /// <b>왜 슬롯에 두는가</b> — 지연이 "그 상자에만" 걸리기 때문이다. 그리고
            /// <see cref="BeginRound"/>·<see cref="EndRound"/> 의 <c>slots.Clear()</c> 가
            /// 자동으로 비워주므로 <b>라운드 경계에서 쿨다운이 새지 않는다.</b>
            /// </para>
            ///
            /// <para>
            /// ⚠️ <c>TreasureChest.ElapsedTicks</c> 를 재사용하지 않는 이유 — 그 값은
            /// <c>ChestSnapshot</c> 으로 나가 화면 진행 바의 시작점이 된다. 쿨다운에 쓰면
            /// <b>아무도 안 연 상자 위에서 진행 바가 움직인다.</b>
            /// </para>
            /// </summary>
            public Dictionary<int, int> RetryCooldownTicks { get; } = new();
        }

        private readonly ChestFieldConfig config;
        private readonly List<Slot> slots = new();

        public ChestField(ChestFieldConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>지금 라운드가 열려 있는가. 닫혀 있으면 상자가 하나도 없다.</summary>
        public bool IsRoundOpen { get; private set; }

        /// <summary>
        /// <b>새 개봉을 아직 받는가</b> (S-GD-DFG3VR).
        ///
        /// <para>
        /// 라운드 막바지에는 새로 열 수 없다 — <b>자기장이 맵 전역을 덮는 순간</b>부터다.
        /// <b>이미 진행 중인 개봉은 계속 진행되고 완료되면 보상을 받는다.</b>
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b>그 시점을 도메인이 스스로 알 수 없다.</b> 자기장 일정은 밖에 있으므로
        /// 어댑터가 <see cref="StopAcceptingNewOpens"/> 로 알려줘야 한다.
        /// </para>
        /// </summary>
        public bool IsAcceptingNewOpens { get; private set; }

        /// <summary>
        /// 새 개봉을 그만 받는다 (S-GD-DFG3VR). <b>진행 중인 개봉은 건드리지 않는다.</b>
        ///
        /// <para>
        /// 자기장이 맵 전역을 덮는 순간 어댑터가 부른다. <b>여러 번 불러도 안전하다.</b>
        /// 다음 <see cref="BeginRound"/> 가 다시 받는 상태로 되돌린다.
        /// </para>
        /// </summary>
        public void StopAcceptingNewOpens()
        {
            RequireRound();
            IsAcceptingNewOpens = false;
        }

        /// <summary>현재 상자 수. 라운드 밖에서는 0이다.</summary>
        public int Count => slots.Count;

        /// <summary>
        /// 전투 라운드를 열고 상자를 놓는다 (S-GD-MQP6AS — 라운드 시작 시 무작위 두 곳).
        ///
        /// <para>
        /// <b><c>ChestId</c> 는 슬롯 번호(0부터)이고 라운드 내내 유지된다.</b> 재생성해도 같은 id·같은 자리다
        /// (S-GD-MQP6AS 가 "동일 위치 재생성" 을 확정했다). 라운드가 바뀌면 새로 발급한다.
        /// ⚠️ 이 규칙은 <c>ui-domain-contract.md</c> §13 의 미결이었고 여기서 <c>assumed</c> 로 정한다.
        /// </para>
        ///
        /// <para>
        /// <b>같은 앵커를 두 번 고르지 않는다.</b> 겹치면 화면에 상자가 하나로 보이는데 도메인은 둘로 세어,
        /// 완료 조건 4("재생성 전후 중복 상자 없음")를 판정할 수 없게 된다.
        /// </para>
        /// </summary>
        /// <param name="random">Host 가 소유한 결정론 난수. 같은 시드면 같은 자리가 나온다.</param>
        public void BeginRound(DeterministicRandom random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (IsRoundOpen)
            {
                throw new InvalidOperationException(
                    "이미 열린 라운드입니다. EndRound 를 먼저 부르세요 — 안 부르면 지난 라운드 상자가 남습니다.");
            }

            slots.Clear();

            var picked = PickAnchors(random);

            for (int i = 0; i < picked.Count; i++)
            {
                slots.Add(new Slot
                {
                    Chest = new TreasureChest(config.Chest),
                    AnchorIndex = picked[i],
                    Position = config.AnchorCandidates[picked[i]],
                });
            }

            IsRoundOpen = true;
            IsAcceptingNewOpens = true;
        }

        /// <summary>
        /// 라운드를 닫고 상자를 <b>전부 제거</b>한다 (S-GD-MQP6AS — 라운드 종료 시 제거).
        ///
        /// <para>
        /// <b>취소가 아니라 파기다.</b> <see cref="TreasureChest.TryCancel"/> 은 <c>Opening</c> 에서만
        /// 동작하므로 그것만 부르면 <c>Rewarded</c>·<c>Respawning</c> 상자가 조용히 남는다.
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b>개봉은 끝났지만 아직 수령되지 않은 상자는 보상을 주지 않는다.</b>
        /// 주면 라운드가 끝난 뒤에 골드 이벤트가 발행돼 정산 순서와 얽힌다.
        /// 스펙이 없어 <c>assumed</c> 로 정한 값이다.
        /// </para>
        /// </summary>
        /// <returns>진행 중이던 개봉을 끊은 건. 화면이 안내를 지울 수 있게 알려준다.</returns>
        public IReadOnlyList<ChestOpenCancellation> EndRound()
        {
            var cancelled = new List<ChestOpenCancellation>();

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (slot.Chest.State == ChestState.Opening)
                {
                    int openerId = slot.Chest.OpenerId;
                    slot.Chest.TryCancel(ChestCancelReason.RoundEnded);
                    cancelled.Add(new ChestOpenCancellation(i, openerId, ChestCancelReason.RoundEnded));
                }
            }

            slots.Clear();
            IsRoundOpen = false;
            IsAcceptingNewOpens = false;
            return cancelled;
        }

        /// <summary>한 틱 진행한다. 개봉이 완료된 상자의 보상을 모아 돌려준다.</summary>
        /// <remarks>
        /// 보상은 <b>개봉 완료 즉시 개봉자에게</b> 준다. 별도 상호작용을 요구하면
        /// 개봉을 끝내고 자리를 뜬 사이 남이 주워가는 경로가 생긴다.
        /// ⚠️ 스펙이 없어 <c>assumed</c> 로 정한 값이다 — <c>ChestClaimRewardCommand</c> 계약은
        /// 남겨두되 여기서는 쓰지 않는다.
        /// </remarks>
        public IReadOnlyList<ChestRewardEarned> Tick()
        {
            RequireRound();

            var earned = new List<ChestRewardEarned>();

            for (int i = 0; i < slots.Count; i++)
            {
                TickRetryCooldown(slots[i]);

                var chest = slots[i].Chest;
                chest.Tick();

                if (chest.IsRewardClaimable)
                {
                    int claimerId = chest.OpenerId;
                    if (chest.TryClaimReward())
                    {
                        earned.Add(new ChestRewardEarned(i, claimerId, config.Reward));
                    }
                }
            }

            return earned;
        }

        /// <summary>
        /// 개봉을 시작한다.
        ///
        /// <para>
        /// <b>한 사람이 두 상자를 동시에 열 수 없다</b> (S-GD-PTVC2X).
        /// <see cref="TreasureChest"/> 는 상자 하나만 알아서 <i>"이 사람이 다른 상자를 열고 있는가"</i>
        /// 를 구조적으로 알 수 없다 — 슬롯을 전부 보는 이 타입이 유일하게 답할 수 있는 자리다.
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b>요청한 그 상자는 검사에서 뺀다.</b> 같은 상자를 다시 요청하는 것은 두 번째 상자가
        /// 아니라 <b>재전송</b>이고, 그 경로는 <see cref="ChestRejectReason.NotIdle"/> 로 답하기로
        /// 이미 고정돼 있다. 빼지 않으면 재전송의 사유가 조용히 바뀐다.
        /// </para>
        /// </summary>
        /// <returns>
        /// 시작했으면 <c>true</c>. 이미 누가 열고 있거나 대기 상태가 아니거나,
        /// <b>이 사람이 다른 상자를 열고 있으면</b> <c>false</c>.
        /// </returns>
        public bool TryBeginOpen(int chestId, int playerId)
        {
            RequireRound();

            if (!IsAcceptingNewOpens
                || !TryGetSlot(chestId, out var slot)
                || IsRetryDelayed(slot, playerId)
                || IsOpeningAnotherChest(playerId, chestId))
            {
                return false;
            }

            return slot.Chest.TryBeginOpen(playerId);
        }

        /// <summary>
        /// 이 사람이 <paramref name="exceptChestId"/> 말고 <b>다른</b> 상자를 열고 있는가.
        ///
        /// <para>
        /// <b>사전을 따로 두지 않고 슬롯을 훑는다.</b> 상자는 라운드당 2개뿐이고(S-GD-MQP6AS),
        /// <see cref="CancelOpenBy"/> 가 이미 같은 모양으로 훑는다. 무엇보다
        /// <c>Opening → Rewarded</c> 전이는 <see cref="TreasureChest.Tick"/> 안에서 일어나
        /// 이 타입에 통보되지 않는다 — <b>사전을 두면 그 순간 조용히 낡아</b> 개봉을 끝낸 사람을
        /// 영원히 잠근다. 스캔은 진실을 읽고 사전은 진실을 흉내낸다.
        /// </para>
        /// </summary>
        /// <summary>
        /// 이 사람이 이 상자의 재시도 지연에 걸려 있는가 (S-GD-5AZ6HN).
        ///
        /// <para>
        /// <b>게이트가 거부 사유를 고를 때 쓴다.</b> 상자가 비어 있는데 개봉이 거부됐다면
        /// 이유가 둘 중 하나인데, 그것을 <b>추론하지 않고 물어보게</b> 하려고 연다.
        /// 없는 상자 번호나 지연이 없는 사람에게는 <c>false</c> 다.
        /// </para>
        /// </summary>
        public bool IsRetryDelayedFor(int chestId, int playerId) =>
            TryGetSlot(chestId, out var slot) && IsRetryDelayed(slot, playerId);

        /// <summary>
        /// 이 사람이 <b>이 상자</b>의 재시도 지연에 걸려 있는가 (S-GD-5AZ6HN).
        /// 지연은 <b>취소당한 본인에게, 그 상자에만</b> 걸린다 — 남은 즉시 열 수 있고
        /// 본인도 다른 상자는 바로 열 수 있다.
        /// </summary>
        private static bool IsRetryDelayed(Slot slot, int playerId) =>
            slot.RetryCooldownTicks.TryGetValue(playerId, out var remaining) && remaining > 0;

        /// <summary>
        /// 이 상자의 재시도 지연을 한 틱 깎는다. 0 이 된 항목은 지운다 — 남겨두면 사람이 나갈 때마다
        /// 사전이 자란다.
        ///
        /// <para>
        /// <b><see cref="Tick"/> 의 맨 앞에서 부른다.</b> 같은 프레임에 <c>Tick → TryBeginOpen</c>
        /// 순서로 들어와도 감소가 이미 반영돼 있어야 하기 때문이다.
        /// </para>
        /// </summary>
        private static void TickRetryCooldown(Slot slot)
        {
            if (slot.RetryCooldownTicks.Count == 0)
            {
                return;
            }

            // 순회 중 수정할 수 없어 키를 먼저 뜬다. 사람 수만큼이라 크지 않다.
            var players = new List<int>(slot.RetryCooldownTicks.Keys);
            for (int i = 0; i < players.Count; i++)
            {
                int remaining = slot.RetryCooldownTicks[players[i]] - 1;
                if (remaining > 0)
                {
                    slot.RetryCooldownTicks[players[i]] = remaining;
                }
                else
                {
                    slot.RetryCooldownTicks.Remove(players[i]);
                }
            }
        }

        private bool IsOpeningAnotherChest(int playerId, int exceptChestId)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (i == exceptChestId)
                {
                    continue;
                }

                var chest = slots[i].Chest;
                if (chest.State == ChestState.Opening && chest.OpenerId == playerId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 이 플레이어가 열고 있던 상자의 개봉을 끊는다 (이동·피격).
        ///
        /// <para>
        /// <b>입력 경계를 <c>playerId</c> 와 사유로만 좁힌다.</b> 이동·피격의 원본은
        /// <c>UnityEngine</c> 타입을 쓰는 전투 이벤트인데, 그것을 도메인에 들이면 D-060 이 깨진다.
        /// </para>
        /// </summary>
        /// <returns>
        /// 실제로 끊긴 건. 아무것도 안 열고 있었으면 빈 목록이고, <b>최대 1건이다</b> —
        /// <see cref="TryBeginOpen"/> 이 "한 사람에 한 상자" 를 강제한다 (S-GD-PTVC2X).
        /// 목록으로 남겨두는 것은 <see cref="EndRound"/> 와 모양을 맞추기 위해서다 —
        /// 그쪽은 <b>사람이 여럿이라</b> 여전히 여러 건을 돌려준다.
        /// </returns>
        public IReadOnlyList<ChestOpenCancellation> CancelOpenBy(int playerId, ChestCancelReason reason)
        {
            RequireRound();

            var cancelled = new List<ChestOpenCancellation>();

            for (int i = 0; i < slots.Count; i++)
            {
                var chest = slots[i].Chest;

                if (chest.State == ChestState.Opening && chest.OpenerId == playerId && chest.TryCancel(reason))
                {
                    cancelled.Add(new ChestOpenCancellation(i, playerId, reason));

                    // 취소당한 본인은 이 상자를 잠시 다시 못 연다 (S-GD-5AZ6HN).
                    //
                    // ⚠️ 라운드 종료는 예외다. 그것은 개봉자의 잘못이 아니라서 사유를 따로
                    //    만들었는데(S-GD-GPXVNR), 지연까지 걸면 다음 라운드 시작 직후 그 사람만
                    //    상자를 못 여는 벌을 받는다. RoundEnded 로는 쿨다운을 만들지 않는다.
                    if (reason != ChestCancelReason.RoundEnded && config.Chest.RetryDelayTicks > 0)
                    {
                        slots[i].RetryCooldownTicks[playerId] = config.Chest.RetryDelayTicks;
                    }
                }
            }

            return cancelled;
        }

        /// <summary>화면이 그대로 그릴 수 있는 현재 상자들. 라운드 밖에서는 빈 목록이다.</summary>
        public IReadOnlyList<ChestPlacement> BuildPlacements()
        {
            var placements = new ChestPlacement[slots.Count];

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                placements[i] = new ChestPlacement(
                    chestId: i,
                    anchorIndex: slot.AnchorIndex,
                    position: slot.Position,
                    state: slot.Chest.State,
                    openerId: slot.Chest.OpenerId,
                    elapsedTicks: slot.Chest.ElapsedTicks,
                    progress: slot.Chest.OpenProgress,
                    isRewardClaimable: slot.Chest.IsRewardClaimable);
            }

            return placements;
        }

        /// <summary>같은 앵커를 두 번 고르지 않고 <paramref name="config"/> 개수만큼 뽑는다.</summary>
        private List<int> PickAnchors(DeterministicRandom random)
        {
            var pool = new List<int>(config.AnchorCandidates.Count);
            for (int i = 0; i < config.AnchorCandidates.Count; i++)
            {
                pool.Add(i);
            }

            random.Shuffle(pool);

            var picked = new List<int>(config.ChestCount);
            for (int i = 0; i < config.ChestCount; i++)
            {
                picked.Add(pool[i]);
            }

            // 인덱스 순으로 두면 같은 시드에서 슬롯 번호와 위치의 짝이 항상 같다.
            picked.Sort();
            return picked;
        }

        private bool TryGetSlot(int chestId, out Slot slot)
        {
            if (chestId < 0 || chestId >= slots.Count)
            {
                slot = null;
                return false;
            }

            slot = slots[chestId];
            return true;
        }

        private void RequireRound()
        {
            if (!IsRoundOpen)
            {
                throw new InvalidOperationException(
                    "전투 라운드가 열려 있지 않습니다. BeginRound 를 먼저 부르세요.");
            }
        }

        public override string ToString() =>
            IsRoundOpen ? $"상자 {slots.Count}개 (라운드 열림)" : "라운드 닫힘";
    }
}
