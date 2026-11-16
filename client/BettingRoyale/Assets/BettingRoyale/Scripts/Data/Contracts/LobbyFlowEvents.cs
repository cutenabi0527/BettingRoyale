using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using BettingRoyale.Core;
using BettingRoyale.Data.Online.Lobby;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// 로비 UI 가 요청할 수 있는 행동의 종류 (S-UX-BXXTHX).
    /// UI 는 이 요청만 발행하고, 실제 세션 작업은 Network 계층 어댑터가 수행한다.
    /// </summary>
    public enum LobbyActionKind
    {
        /// <summary>로비 접속(세션 목록 구독 시작).</summary>
        Connect = 0,

        /// <summary>방 목록 갱신 요청.</summary>
        Refresh = 1,

        /// <summary>방 만들기. Text=방 제목, MaxPlayers=정원, Flag=공개 여부, Password=선택적 비밀번호.</summary>
        Create = 2,

        /// <summary>코드/목록 선택 참가. Text=방 코드, Password=필요 시 비밀번호.</summary>
        Join = 3,

        /// <summary>랜덤 참가. 후보가 없으면 UI 가 발행하지 않으며, 이 요청으로 방을 새로 만들지 않는다.</summary>
        RandomJoin = 4,

        /// <summary>방 나가기(나가면 방 찾기 화면으로 돌아간다).</summary>
        Leave = 5,

        /// <summary>준비/준비 취소. Flag=준비 여부.</summary>
        SetReady = 6,

        /// <summary>방장 전용 게임 시작.</summary>
        StartMatch = 7,

        /// <summary>현재 상태 스냅샷 재발행 요청(화면 진입 시 초기 동기화).</summary>
        Snapshot = 8,

        /// <summary>대기방에 봇 하나를 추가한다 (S-ONL-9GXTYQ). 권한 판정은 Host 가 한다.</summary>
        AddBot = 9,

        /// <summary>
        /// 대기방에서 봇 하나를 제거한다. <b>대상은 가장 나중에 추가된 봇</b>이므로 id 를 싣지 않는다
        /// (S-UX-9SMAFJ).
        /// </summary>
        RemoveBot = 10,

        /// <summary>
        /// 전용 게임 서버 방 배정 대기를 취소한다 (S15P21A606-321, S-UX-8MT759). 대기 중일 때만 의미가 있다.
        /// </summary>
        CancelQueue = 11,
    }

    /// <summary>
    /// 로비 UI → 어댑터 방향의 단일 요청 이벤트.
    /// 필드를 종류별로 나누지 않고 <see cref="Text"/> / <see cref="Flag"/> 로 공유해 이벤트 수를 늘리지 않는다.
    /// <see cref="Password"/> 원문은 <see cref="ToString"/> 를 포함한 어떤 진단 출력에도 넣지 않는다.
    /// </summary>
    public readonly struct LobbyActionRequested : IGameEvent
    {
        // default(LobbyActionRequested) 도 null 문자열을 노출하지 않도록 getter 에서 막는다.
        private readonly string _text;
        private readonly string _password;

        public LobbyActionRequested(
            LobbyActionKind kind,
            string text = null,
            string password = null,
            int maxPlayers = 0,
            bool flag = false)
        {
            Kind = kind;
            _text = text ?? string.Empty;
            _password = password ?? string.Empty;
            MaxPlayers = maxPlayers;
            Flag = flag;
        }

        public LobbyActionKind Kind { get; }

        /// <summary><see cref="LobbyActionKind.Create"/> 는 방 제목, <see cref="LobbyActionKind.Join"/> 은 방 코드.</summary>
        public string Text => _text ?? string.Empty;

        /// <summary>선택적 비밀번호 원문. 없으면 빈 문자열. 로그·세션 속성·파일에 저장하지 않는다.</summary>
        public string Password => _password ?? string.Empty;

        /// <summary><see cref="LobbyActionKind.Create"/> 의 정원(2·4·6·8). 그 외에는 0.</summary>
        public int MaxPlayers { get; }

        /// <summary><see cref="LobbyActionKind.Create"/> 는 공개 여부, <see cref="LobbyActionKind.SetReady"/> 는 준비 여부.</summary>
        public bool Flag { get; }

        public bool HasPassword => Password.Length > 0;

        public static LobbyActionRequested Connect()
        {
            return new LobbyActionRequested(LobbyActionKind.Connect);
        }

        public static LobbyActionRequested Refresh()
        {
            return new LobbyActionRequested(LobbyActionKind.Refresh);
        }

        /// <summary>검증을 통과한 방 생성 입력으로 요청을 만든다.</summary>
        public static LobbyActionRequested Create(LobbyCreateRoomDetails details)
        {
            return new LobbyActionRequested(
                LobbyActionKind.Create,
                details.Title,
                details.Password,
                details.MaxPlayers,
                details.IsPublic);
        }

        public static LobbyActionRequested Create(string roomTitle, int maxPlayers, bool isPublic, string password = null)
        {
            return new LobbyActionRequested(LobbyActionKind.Create, roomTitle, password, maxPlayers, isPublic);
        }

        public static LobbyActionRequested Join(string roomCode, string password = null)
        {
            return new LobbyActionRequested(LobbyActionKind.Join, roomCode, password);
        }

        public static LobbyActionRequested RandomJoin()
        {
            return new LobbyActionRequested(LobbyActionKind.RandomJoin);
        }

        public static LobbyActionRequested Leave()
        {
            return new LobbyActionRequested(LobbyActionKind.Leave);
        }

        public static LobbyActionRequested SetReady(bool ready)
        {
            return new LobbyActionRequested(LobbyActionKind.SetReady, flag: ready);
        }

        public static LobbyActionRequested StartMatch()
        {
            return new LobbyActionRequested(LobbyActionKind.StartMatch);
        }

        public static LobbyActionRequested AddBot()
        {
            return new LobbyActionRequested(LobbyActionKind.AddBot);
        }

        public static LobbyActionRequested RemoveBot()
        {
            return new LobbyActionRequested(LobbyActionKind.RemoveBot);
        }

        public static LobbyActionRequested CancelQueue()
        {
            return new LobbyActionRequested(LobbyActionKind.CancelQueue);
        }

        public static LobbyActionRequested Snapshot()
        {
            return new LobbyActionRequested(LobbyActionKind.Snapshot);
        }

        /// <summary>비밀번호 원문을 포함하지 않는 진단 문자열(존재 여부만 남긴다).</summary>
        public override string ToString()
        {
            return string.Concat(
                "LobbyActionRequested(Kind=", Kind.ToString(),
                ", Text='", Text,
                "', HasPassword=", HasPassword ? "true" : "false",
                ", MaxPlayers=", MaxPlayers.ToString(CultureInfo.InvariantCulture),
                ", Flag=", Flag ? "true" : "false",
                ")");
        }
    }

    /// <summary>
    /// 어댑터 → 로비 UI 방향의 단일 상태 스냅샷. UI 는 이 한 이벤트만 구독해 모든 화면을 그린다.
    /// 불변이며 생성 시 목록을 복사해 읽기 전용으로 감싸므로, 발행 뒤 원본 목록이 바뀌거나
    /// 소비자가 캐스팅을 시도해도 이미 전달된 스냅샷은 변하지 않는다.
    /// 비밀번호는 어떤 형태로도 담지 않는다.
    /// </summary>
    public sealed class LobbyFlowSnapshot : IGameEvent
    {
        /// <summary>화면 진입 직후 쓰는 빈 스냅샷(연결 전 상태).</summary>
        public static readonly LobbyFlowSnapshot Empty = new LobbyFlowSnapshot();

        private readonly ReadOnlyCollection<LobbyRoomView> _rooms;
        private readonly ReadOnlyCollection<LobbyPlayerView> _players;
        private readonly ReadOnlyCollection<LobbyBotSlot> _bots;

        /// <summary>연결 전 초기 스냅샷.</summary>
        public LobbyFlowSnapshot()
            : this(false, false, false, false, false, false, false, null, null, null, 0, true, null, null)
        {
        }

        public LobbyFlowSnapshot(
            bool connected,
            bool connecting,
            bool inRoom,
            bool isHost,
            bool isLocalReady,
            bool canStart,
            bool busy,
            string status,
            string roomCode,
            string roomTitle,
            int maxPlayers,
            bool isPublic,
            IReadOnlyList<LobbyRoomView> rooms,
            IReadOnlyList<LobbyPlayerView> players,
            bool returnToBrowser = false,
            IReadOnlyList<LobbyBotSlot> bots = null,
            bool canManageBots = false,
            bool canCancelQueue = false)
        {
            _bots = Clone(bots);
            CanManageBots = canManageBots;
            CanCancelQueue = canCancelQueue;
            Connected = connected;
            Connecting = connecting;
            InRoom = inRoom;
            IsHost = isHost;
            IsLocalReady = isLocalReady;
            CanStart = canStart;
            Busy = busy;
            Status = status ?? string.Empty;
            RoomCode = roomCode ?? string.Empty;
            RoomTitle = roomTitle ?? string.Empty;
            MaxPlayers = maxPlayers;
            IsPublic = isPublic;
            ReturnToBrowser = returnToBrowser;
            _rooms = Clone(rooms);
            _players = Clone(players);
        }

        /// <summary>로비 세션에 접속돼 목록을 신뢰할 수 있는 상태.</summary>
        public bool Connected { get; }

        /// <summary>접속 시도 중.</summary>
        public bool Connecting { get; }

        /// <summary>대기방 안에 있는 상태.</summary>
        public bool InRoom { get; }

        /// <summary>세션 종료로 로비 씬을 다시 연 경우 방 찾기로 복귀한다.</summary>
        public bool ReturnToBrowser { get; }

        /// <summary>로컬 플레이어가 방장인가.</summary>
        public bool IsHost { get; }

        /// <summary>로컬 플레이어의 준비 상태.</summary>
        public bool IsLocalReady { get; }

        /// <summary>방장 기준으로 계산된 시작 가능 여부(최소 2명 · 전원 준비).</summary>
        public bool CanStart { get; }

        /// <summary>요청 처리 중. 중복 클릭·이탈을 막기 위해 UI 가 입력을 잠근다.</summary>
        public bool Busy { get; }

        /// <summary>사용자에게 보여줄 안전한 한국어 상태 문구. 없으면 빈 문자열.</summary>
        public string Status { get; }

        /// <summary>현재 방 코드(대기방에서 복사에 사용). 방 밖이면 빈 문자열.</summary>
        public string RoomCode { get; }

        public string RoomTitle { get; }

        public int MaxPlayers { get; }

        public bool IsPublic { get; }

        /// <summary>방 찾기 목록. 복사본을 감싼 읽기 전용 뷰라 캐스팅해도 쓸 수 없다.</summary>
        public IReadOnlyList<LobbyRoomView> Rooms => _rooms;

        /// <summary>대기방 슬롯. 복사본을 감싼 읽기 전용 뷰라 캐스팅해도 쓸 수 없다.</summary>
        public IReadOnlyList<LobbyPlayerView> Players => _players;

        /// <summary>
        /// 대기방의 봇 (S-ONL-9GXTYQ). <see cref="Players"/> 와 <b>합쳐서</b> 정원을 쓴다.
        /// </summary>
        /// <remarks>
        /// 사람과 한 목록에 섞지 않는다 — 봇은 준비·무기·방장 배지가 없어서, 섞으면 소비자마다
        /// "이 줄이 봇인가" 를 다시 판정해야 한다. 화면은 사람을 먼저 그리고 봇을 뒤에 잇는다
        /// (S-UX-9SMAFJ).
        /// </remarks>
        public IReadOnlyList<LobbyBotSlot> Bots => _bots;

        /// <summary>
        /// 지금 이 클라이언트가 봇을 추가·제거할 수 있는가. <b>버튼을 보일지</b>의 기준이다.
        /// </summary>
        /// <remarks>
        /// ⚠️ 이것은 <b>자격</b>이고 조건이 아니다. 정원이 찼거나 봇이 없어 못 누르는 것은 별개이며
        /// 화면이 활성 여부로 그린다 (S-UX-9SMAFJ). 권한이 없으면 버튼 자체를 감춘다.
        /// <para>판정 정본은 Host 이고 여기 실린 값은 그 복제다 — 화면이 스스로 정하지 않는다.</para>
        /// </remarks>
        public bool CanManageBots { get; }

        /// <summary>
        /// 전용 게임 서버 방 배정을 기다리는 중이라 취소 버튼을 보여야 하는가 (S15P21A606-321, S-UX-8MT759).
        /// </summary>
        /// <remarks>대기 중에는 <see cref="Busy"/> 도 켜져 다른 입력은 잠기고 이 버튼만 살아 있다.</remarks>
        public bool CanCancelQueue { get; }

        /// <summary>
        /// 입력을 복사한 뒤 읽기 전용 래퍼로 감싼다. 배열을 그대로 노출하면 소비자가
        /// <c>(T[])snapshot.Rooms</c> 로 캐스팅해 이미 발행된 스냅샷을 바꿀 수 있다.
        /// </summary>
        private static ReadOnlyCollection<T> Clone<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.AsReadOnly(Array.Empty<T>());
            }

            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return Array.AsReadOnly(copy);
        }

        public override string ToString()
        {
            return string.Concat(
                "LobbyFlowSnapshot(Connected=", Connected ? "true" : "false",
                ", Connecting=", Connecting ? "true" : "false",
                ", InRoom=", InRoom ? "true" : "false",
                ", IsHost=", IsHost ? "true" : "false",
                ", Ready=", IsLocalReady ? "true" : "false",
                ", CanStart=", CanStart ? "true" : "false",
                ", Busy=", Busy ? "true" : "false",
                ", Rooms=", _rooms.Count.ToString(CultureInfo.InvariantCulture),
                ", Players=", _players.Count.ToString(CultureInfo.InvariantCulture),
                ", Bots=", _bots.Count.ToString(CultureInfo.InvariantCulture),
                ", CanManageBots=", CanManageBots ? "true" : "false",
                ")");
        }
    }
}
