using System.Collections.Generic;
using System.Globalization;
using BettingRoyale.Data.Profile;

namespace BettingRoyale.Data.Online.Lobby
{
    /// <summary>대기방 봇 한 칸 (S-ONL-9GXTYQ). 세션 한정이며 계정과 이어지지 않는다.</summary>
    public readonly struct LobbyBotSlot
    {
        private readonly string _name;

        /// <summary>
        /// 슬롯을 직접 만든다. <b>로스터가 배정한 번호를 그대로 복원할 때만 쓴다</b>(복제 수신·테스트).
        /// 새 봇은 <see cref="LobbyBotRoster.TryAdd"/> 로만 만든다 — 번호 배정이 거기에 있다.
        /// </summary>
        public LobbyBotSlot(int number, int addedByPlayerId, int sequence)
        {
            Number = number;
            _name = "봇 " + number.ToString(CultureInfo.InvariantCulture);
            AddedByPlayerId = addedByPlayerId;
            Sequence = sequence;
        }

        /// <summary>1-based 슬롯 번호. 표시 이름의 근거이며 비어 있는 가장 작은 번호가 배정된다.</summary>
        public int Number { get; }

        /// <summary>
        /// 참가자 목록에서 쓰는 식별자. <c>PlayerRef.PlayerId</c> 와 겹치지 않도록
        /// <see cref="LobbyBotRoster.BotIdBase"/> 를 더한다.
        /// </summary>
        public int Id => LobbyBotRoster.BotIdBase + Number;

        public string Name => _name ?? string.Empty;

        /// <summary>봇 번호로 고정되는 임의 초상화. 모든 피어가 같은 봇을 같은 얼굴로 본다.</summary>
        public string PortraitId => ResolvePortraitId(Number);

        public static string ResolvePortraitId(int number)
        {
            int count = ProfilePortraitCatalog.All.Count;
            if (count <= 0) return ProfilePortraitCatalog.Default;
            // 연속 봇이 같은 얼굴로 몰리지 않되 세션 복제 값은 늘 같도록 번호만 사용한다.
            int index = (unchecked(number * 3 + 1) & int.MaxValue) % count;
            return ProfilePortraitCatalog.All[index];
        }

        /// <summary>이 봇을 추가한 참가자. 진단용이며 제거 권한 판정에는 쓰지 않는다(S-ONL-9GXTYQ).</summary>
        public int AddedByPlayerId { get; }

        /// <summary>추가된 순서. 정원을 넘겼을 때 <b>큰 값부터</b> 제거한다(LIFO).</summary>
        public int Sequence { get; }

        public override string ToString()
        {
            return string.Concat(
                "LobbyBotSlot(Number=", Number.ToString(CultureInfo.InvariantCulture),
                ", Id=", Id.ToString(CultureInfo.InvariantCulture),
                ", AddedBy=", AddedByPlayerId.ToString(CultureInfo.InvariantCulture),
                ", Sequence=", Sequence.ToString(CultureInfo.InvariantCulture),
                ")");
        }
    }

    /// <summary>봇 조작 요청의 처리 결과. 사용자 문구는 UI 계층이 안전한 한국어로 매핑한다.</summary>
    public enum LobbyBotActionResult
    {
        /// <summary>요청이 반영됐다.</summary>
        Ok = 0,

        /// <summary>요청자에게 봇 관리 권한이 없다. <b>조용히 버리지 않고 이 값으로 구분한다.</b></summary>
        NotPermitted = 1,

        /// <summary>사람 + 봇이 이미 정원이다.</summary>
        RoomFull = 2,

        /// <summary>봇 상한(정원 − 1)에 도달했다.</summary>
        LimitReached = 3,

        /// <summary>제거할 봇이 없다.</summary>
        NoBots = 4,

        /// <summary>지정한 봇을 찾지 못했다.</summary>
        NotFound = 5,
    }

    /// <summary>
    /// 대기방 봇 슬롯과 봇 관리 권한의 <b>순수 규칙</b> (S-ONL-9GXTYQ).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 정본은 <b>Host 메모리</b>이고, 클라이언트는 복제된 스냅샷을 같은 타입에 적용해 읽는다
    /// (D-ONL-AM6C7M). 그래서 이 타입은 Host 전용이 아니며 권한 판정도 양쪽에서 같은 코드로 돈다 —
    /// 화면이 "버튼을 보일지" 계산하는 규칙과 Host 가 "요청을 받을지" 판정하는 규칙이 갈라지지 않게 한다.
    /// </para>
    /// <para>
    /// ⚠️ <b>이 타입은 UnityEngine · Fusion 을 참조하지 않는다</b>(D-060 순수 도메인 테스트 대상).
    /// <c>PlayerRef</c> 를 받지 않고 <c>int playerId</c> 만 받는 이유가 그것이다.
    /// </para>
    /// </remarks>
    public sealed class LobbyBotRoster
    {
        /// <summary>
        /// 봇 식별자의 시작점. <c>PlayerRef.PlayerId</c> 는 0 부터 올라가고
        /// 훈련 NPC 는 <c>20_000</c> 대를 쓰므로(<c>NetworkCombatantMotion.TrainingActorId</c>) 그 사이를 쓴다.
        /// </summary>
        public const int BotIdBase = 10_000;

        private readonly List<LobbyBotSlot> slots = new List<LobbyBotSlot>();
        private readonly List<int> grantedPlayerIds = new List<int>();
        private int nextSequence = 1;

        /// <summary>현재 봇 수.</summary>
        public int Count => slots.Count;

        /// <summary>현재 봇 슬롯. 추가 순서대로 들어 있다.</summary>
        public IReadOnlyList<LobbyBotSlot> Slots => slots;

        /// <summary>봇 관리 권한을 받은 참가자 목록(방장 제외).</summary>
        public IReadOnlyList<int> GrantedPlayerIds => grantedPlayerIds;

        /// <summary>이 식별자가 봇인가.</summary>
        public static bool IsBotId(int id) => id >= BotIdBase;

        /// <summary>
        /// 이 방에 둘 수 있는 봇의 상한 = <b>정원 − 1</b>. 방에는 항상 사람이 최소 1명 남는다.
        /// </summary>
        public static int MaxBots(int maxPlayers) => maxPlayers > 1 ? maxPlayers - 1 : 0;

        /// <summary>
        /// 이 참가자가 봇을 다룰 수 있는가. <b>방장은 항상 가능하다.</b>
        /// </summary>
        /// <remarks>
        /// 권한은 세션 한정이라 저장되지 않는다 — <see cref="ForgetPlayer"/> 를 본다.
        /// </remarks>
        public bool HasControl(int playerId, int hostPlayerId)
        {
            if (playerId == hostPlayerId)
            {
                return true;
            }

            for (var i = 0; i < grantedPlayerIds.Count; i++)
            {
                if (grantedPlayerIds[i] == playerId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>권한을 부여한다. 이미 가지고 있으면 <c>false</c>(변경 없음).</summary>
        public bool Grant(int playerId, int hostPlayerId)
        {
            // 방장은 권한이 내장돼 있다. 목록에 넣으면 방장이 바뀌었을 때 남은 값이 권한으로 남는다.
            if (playerId == hostPlayerId || IsBotId(playerId) || HasControl(playerId, hostPlayerId))
            {
                return false;
            }

            grantedPlayerIds.Add(playerId);
            return true;
        }

        /// <summary>권한을 회수한다. 가지고 있지 않았으면 <c>false</c>(변경 없음).</summary>
        public bool Revoke(int playerId)
        {
            return grantedPlayerIds.Remove(playerId);
        }

        /// <summary>
        /// 방을 나간 참가자의 흔적을 지운다. <b>반드시 퇴장 시 부른다.</b>
        /// </summary>
        /// <remarks>
        /// ⚠️ Fusion 은 빈 슬롯의 <c>PlayerRef</c> 번호를 재사용한다. 남겨두면 <b>다음에 들어온
        /// 다른 사람</b>이 앞사람의 봇 관리 권한을 그대로 물려받는다
        /// (<c>NetworkManager.OnPlayerLeft</c> 가 <c>accountId</c> 를 지우는 것과 같은 이유).
        /// 봇 자체는 지우지 않는다 — 추가한 사람이 나가도 봇은 방에 남는다.
        /// </remarks>
        public bool ForgetPlayer(int playerId)
        {
            return Revoke(playerId);
        }

        /// <summary>봇을 한 칸 추가한다.</summary>
        /// <param name="requesterPlayerId">요청자. <b>연결에서 판정한 값</b>이어야 한다(D-ONL-AM6C7M).</param>
        /// <param name="hostPlayerId">현재 방장.</param>
        /// <param name="humanCount">지금 방에 있는 사람 수(봇 제외).</param>
        /// <param name="maxPlayers">이 방의 실제 정원.</param>
        public LobbyBotActionResult TryAdd(int requesterPlayerId, int hostPlayerId, int humanCount, int maxPlayers)
        {
            if (!HasControl(requesterPlayerId, hostPlayerId))
            {
                return LobbyBotActionResult.NotPermitted;
            }

            if (slots.Count >= MaxBots(maxPlayers))
            {
                return LobbyBotActionResult.LimitReached;
            }

            if (humanCount + slots.Count >= maxPlayers)
            {
                return LobbyBotActionResult.RoomFull;
            }

            slots.Add(new LobbyBotSlot(NextFreeNumber(), requesterPlayerId, nextSequence++));
            return LobbyBotActionResult.Ok;
        }

        /// <summary>
        /// 봇을 제거한다. <paramref name="botId"/> 가 0 이하면 <b>가장 나중에 추가된</b> 봇을 지운다.
        /// </summary>
        public LobbyBotActionResult TryRemove(int requesterPlayerId, int hostPlayerId, int botId)
        {
            if (!HasControl(requesterPlayerId, hostPlayerId))
            {
                return LobbyBotActionResult.NotPermitted;
            }

            if (slots.Count == 0)
            {
                return LobbyBotActionResult.NoBots;
            }

            if (botId <= 0)
            {
                slots.RemoveAt(NewestIndex());
                return LobbyBotActionResult.Ok;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].Id == botId)
                {
                    slots.RemoveAt(i);
                    return LobbyBotActionResult.Ok;
                }
            }

            return LobbyBotActionResult.NotFound;
        }

        /// <summary>
        /// 사람이 들어와 정원을 넘겼으면 <b>가장 나중에 추가된 봇부터</b> 잘라낸다 (S-ONL-9GXTYQ 사람 우선).
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>Photon 의 <c>MaxPlayers</c> 는 봇을 세지 않는다.</b> 실제 피어만 세므로
        /// 정원이 봇으로 찬 방에도 사람은 그대로 들어온다. 여기서 자르지 않으면
        /// 8명 방에 9명분 슬롯이 생기고, 그 상태는 매치가 시작될 때까지 드러나지 않는다.
        /// </remarks>
        /// <returns>제거한 봇 수.</returns>
        public int TrimToCapacity(int humanCount, int maxPlayers)
        {
            var removed = 0;
            int limit = MaxBots(maxPlayers);

            while (slots.Count > 0 && (slots.Count > limit || humanCount + slots.Count > maxPlayers))
            {
                slots.RemoveAt(NewestIndex());
                removed++;
            }

            return removed;
        }

        /// <summary>방을 떠나거나 새 방을 열 때 전부 비운다.</summary>
        public void Clear()
        {
            slots.Clear();
            grantedPlayerIds.Clear();
            nextSequence = 1;
        }

        /// <summary>
        /// 복제된 스냅샷을 그대로 적용한다. <b>클라이언트 전용 경로다</b> — Host 는 자기 메모리가 정본이다.
        /// </summary>
        public void ApplySnapshot(IReadOnlyList<LobbyBotSlot> replicatedSlots, IReadOnlyList<int> replicatedGrants)
        {
            slots.Clear();
            grantedPlayerIds.Clear();

            if (replicatedSlots != null)
            {
                for (var i = 0; i < replicatedSlots.Count; i++)
                {
                    slots.Add(replicatedSlots[i]);
                }
            }

            if (replicatedGrants != null)
            {
                for (var i = 0; i < replicatedGrants.Count; i++)
                {
                    grantedPlayerIds.Add(replicatedGrants[i]);
                }
            }

            nextSequence = 1;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].Sequence >= nextSequence)
                {
                    nextSequence = slots[i].Sequence + 1;
                }
            }
        }

        /// <summary>
        /// 최소 인원을 채웠는가. <b>봇이 인원을 채울 수 있다</b>(S-ONL-9GXTYQ).
        /// </summary>
        /// <remarks>
        /// ⚠️ 이것은 시작 조건의 <b>일부</b>다. 사람의 준비·신원 게이트는 여기서 판정하지 않는다 —
        /// 그 판정은 <c>NetworkManager.CanStartMatch</c> 에만 있다(D-030).
        /// </remarks>
        public static bool MeetsMinimumRoster(int humanCount, int botCount)
        {
            return humanCount >= 1 && humanCount + botCount >= LobbyRoomRules.MinPlayersToStart;
        }

        /// <summary>비어 있는 가장 작은 슬롯 번호. 제거로 빈 번호는 다시 쓴다.</summary>
        private int NextFreeNumber()
        {
            for (var number = 1; ; number++)
            {
                var taken = false;
                for (var i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Number == number)
                    {
                        taken = true;
                        break;
                    }
                }

                if (!taken)
                {
                    return number;
                }
            }
        }

        /// <summary>
        /// 가장 나중에 추가된 봇의 위치. <b>목록 순서가 아니라 <see cref="LobbyBotSlot.Sequence"/> 로 찾는다</b> —
        /// 복제된 스냅샷은 순서가 보존된다는 보장이 없다.
        /// </summary>
        private int NewestIndex()
        {
            var index = 0;
            for (var i = 1; i < slots.Count; i++)
            {
                if (slots[i].Sequence > slots[index].Sequence)
                {
                    index = i;
                }
            }

            return index;
        }
    }
}
