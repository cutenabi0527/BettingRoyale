using System;

namespace BettingRoyale.Data.Dto
{
    /// <summary>방 배정 요청 본문 (<c>docs/server/api.md</c> 10-1, S15P21A606-321).</summary>
    /// <remarks>비밀번호는 전용 서버가 입장 심사에 쓰도록 배정 API 를 거쳐 서버에만 전달된다. 로그에 남기지 않는다.</remarks>
    [Serializable]
    public sealed class RoomTicketRequestDto
    {
        public string title;
        public bool isPublic;
        public int maxPlayers;
        public string password;
    }

    /// <summary>
    /// 방 배정 티켓 상태 (api.md 10-1·10-2). <c>status</c> 는 QUEUED·STARTING·READY·FAILED·CANCELED 중 하나.
    /// </summary>
    [Serializable]
    public sealed class RoomTicketResponseDto
    {
        public string ticketId;
        public string status;

        /// <summary>QUEUED 일 때의 대기 순번(1부터). 그 밖에는 0.</summary>
        public int position;

        /// <summary>READY 일 때 참가할 방 코드. 그 전에는 빈 값.</summary>
        public string roomCode;
    }

    /// <summary>게임 서버 → 배정 API 하트비트 (api.md 10-4). <c>state</c> 는 IDLE·BUSY.</summary>
    [Serializable]
    public sealed class GameServerHeartbeatDto
    {
        public string serverId;
        public string state;
        public string ticketId;
        public string roomCode;
        public int playerCount;

        /// <summary>배정받은 방을 열지 못했을 때 그 티켓. 배정 API 가 FAILED 로 닫는다.</summary>
        public string failedTicketId;
    }

    /// <summary>하트비트 응답. 배정이 없으면 <c>assignment.ticketId</c> 가 빈 값이다.</summary>
    [Serializable]
    public sealed class GameServerHeartbeatResponseDto
    {
        public GameServerAssignmentDto assignment;
    }

    /// <summary>게임 서버가 열 방 (api.md 10-4).</summary>
    [Serializable]
    public sealed class GameServerAssignmentDto
    {
        public string ticketId;
        public string roomCode;
        public string title;
        public bool isPublic;
        public int maxPlayers;
        public string password;
        public string ownerNickname;
    }
}
