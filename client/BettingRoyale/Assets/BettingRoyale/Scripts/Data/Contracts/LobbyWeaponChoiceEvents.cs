using BettingRoyale.Core;
using BettingRoyale.Data.Online.Lobby;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// 대기방 화면 → 세션. 로컬 참가자가 이번 매치에 쓸 무기 선택을 바꾼다 ([S-UX-NDANX2]).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 <c>LobbyActionRequested</c> 에 종류를 늘리지 않았는가.</b> 그 요청은
    /// <c>LobbyFlowAdapter</c> 가 <c>pending</c> 한 건씩 직렬로 처리하는 <b>세션 조작</b>
    /// 창구다(방 만들기·참가·준비·시작). 무기 선택은 세션을 건드리지 않고 복제 값 하나만 바꾸므로,
    /// 거기 끼우면 방 참가 중이라는 이유로 무기를 못 고르거나 반대로 무기 선택이 참가를 막는다.
    /// 별도 명령으로 두면 두 흐름이 서로를 기다리지 않는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>이 명령이 곧 확정이 아니다.</b> 화면은 자기 값을 먼저 뒤집지 않고
    /// <see cref="LobbyWeaponChoiceChanged"/> 를 기다린다 — 준비(Ready)와 같은 원칙이다
    /// ([S-ONL-42ATRV]). 쓰는 쪽은 언제나 Host 하나다.
    /// </para>
    /// </remarks>
    public readonly struct LobbyWeaponChoiceCommand : IGameEvent
    {
        public readonly LobbyWeaponChoice Choice;

        public LobbyWeaponChoiceCommand(LobbyWeaponChoice choice)
        {
            Choice = choice;
        }
    }

    /// <summary>
    /// 세션 → 대기방 화면. 참가자 <b>한 명</b>의 복제 확정된 무기 선택 ([S-UX-NDANX2]).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 참가자 목록(<c>LobbyFlowSnapshot.Players</c>)에 싣지 않는가.</b> 무기 선택은 명단과
    /// 다른 네트워크 프레임에 도착한다 — 장비 포커스를 인벤토리와 분리한 것과 같은 이유다
    /// (<c>docs/client/ui-domain-contract.md</c> 장비 포커스 계약). 한 이벤트로 묶으면 둘 중 늦게
    /// 오는 쪽이 먼저 온 쪽을 덮어쓰거나, 명단이 바뀔 때마다 무기를 지웠다 다시 채우게 된다.
    /// </para>
    /// <para>
    /// 화면은 마지막 명단 스냅샷과 이 이벤트들의 마지막 값을 <b>조합해서</b> 그린다.
    /// 이 이벤트만으로 참가자가 있다고 판단하거나, 명단만으로 무기를 초기화하지 않는다.
    /// </para>
    /// <para>
    /// <see cref="PlayerId"/> 는 <c>LobbyPlayerView.Id</c> 와 같은 세션 한정 식별자다.
    /// <see cref="IsMine"/> 는 받는 쪽이 판정한 결과이며, 드롭다운을 되맞추는 데만 쓴다.
    /// </para>
    /// </remarks>
    public readonly struct LobbyWeaponChoiceChanged : IGameEvent
    {
        public readonly int PlayerId;
        public readonly LobbyWeaponChoice Choice;
        public readonly bool IsMine;

        public LobbyWeaponChoiceChanged(int playerId, LobbyWeaponChoice choice, bool isMine)
        {
            PlayerId = playerId;
            Choice = choice;
            IsMine = isMine;
        }
    }
    /// <summary>Host가 거절한 선택과 현재 확정 값. 소유자 화면만 복구한다 (S-CLI-XTRYQD).</summary>
    public readonly struct LobbyWeaponChoiceRejected : IGameEvent
    {
        public readonly int PlayerId;
        public readonly LobbyWeaponChoice RequestedChoice;
        public readonly LobbyWeaponChoice CurrentChoice;
        public readonly LobbyWeaponChoiceRejectReason Reason;
        public readonly bool IsMine;

        public LobbyWeaponChoiceRejected(int playerId, LobbyWeaponChoice requestedChoice,
            LobbyWeaponChoice currentChoice, LobbyWeaponChoiceRejectReason reason, bool isMine)
        {
            PlayerId = playerId;
            RequestedChoice = requestedChoice;
            CurrentChoice = currentChoice;
            Reason = reason;
            IsMine = isMine;
        }
    }
}
