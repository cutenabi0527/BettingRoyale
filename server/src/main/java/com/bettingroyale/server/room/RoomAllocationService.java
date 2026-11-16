package com.bettingroyale.server.room;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

/**
 * 전용 게임 서버 방 배정과 대기열 (docs/server/api.md 10절, S15P21A606-321, S-ONL-E33BEK).
 *
 * <p>상태는 <b>이 프로세스 메모리에만</b> 둔다. 티켓은 수십 초짜리이고, 백엔드가 재시작되면 기다리던 사람은
 * 다시 요청하면 된다 — DB 에 둘 이유가 없다. 환경마다 백엔드는 하나다(Preview·Production 스택 각 1개).
 *
 * <p>흐름: 클라이언트가 티켓을 낸다(QUEUED) → 한가한 게임 서버가 하트비트로 가장 오래 기다린 티켓을 받는다
 * (STARTING, 방 코드 발급) → 서버가 방을 열고 BUSY 하트비트로 그 방 코드를 알린다(READY) → 클라이언트가 방
 * 코드로 참가한다. 자리가 없으면 QUEUED 로 기다리며 순번을 받는다 — 대기 시간 상한은 없다(S-UX-8MT759).
 *
 * <p>동시에 돌 수 있는 방은 {@code min(살아 있는 서버 수, game-server.max-rooms)} 이다.
 *
 * <p>⚠️ 비밀번호는 방을 열 서버에만 넘긴다. 로그에 남기지 않는다.
 */
@Service
public class RoomAllocationService {

    private static final Logger LOGGER = LoggerFactory.getLogger(RoomAllocationService.class);

    public static final int MAX_TITLE_LENGTH = 40;
    public static final int MAX_PASSWORD_LENGTH = 32;
    public static final int MIN_PLAYERS = 1;
    public static final int MAX_PLAYERS = 8;

    enum Status { QUEUED, STARTING, READY, FAILED, CANCELED }

    private static final class Ticket {
        final String id;
        final String accountId;
        final String ownerNickname;
        final String title;
        final boolean isPublic;
        final int maxPlayers;
        final String password;
        Status status = Status.QUEUED;
        String roomCode = "";
        String serverId;
        Instant lastPolledAt;
        Instant finishedAt;

        Ticket(String id, String accountId, String ownerNickname, String title, boolean isPublic, int maxPlayers,
                String password, Instant now) {
            this.id = id;
            this.accountId = accountId;
            this.ownerNickname = ownerNickname;
            this.title = title;
            this.isPublic = isPublic;
            this.maxPlayers = maxPlayers;
            this.password = password;
            this.lastPolledAt = now;
        }

        boolean active() {
            return status == Status.QUEUED || status == Status.STARTING;
        }
    }

    private static final class Server {
        final String id;
        boolean busy;
        String ticketId;
        Instant lastSeen;

        Server(String id) {
            this.id = id;
        }
    }

    /** 클라이언트에 보여줄 티켓 상태. */
    public record TicketView(String ticketId, String status, int position, String roomCode) {
    }

    /** 게임 서버가 열 방. */
    public record Assignment(String ticketId, String roomCode, String title, boolean isPublic, int maxPlayers,
            String password, String ownerNickname) {
    }

    /** 방 배정 요청 입력. */
    public record TicketRequest(String title, Boolean isPublic, Integer maxPlayers, String password) {
    }

    /** 게임 서버 하트비트 입력. */
    public record HeartbeatRequest(String serverId, String state, String ticketId, String roomCode, Integer playerCount,
            String failedTicketId) {
    }

    private final GameServerProperties properties;
    private final Clock clock;

    /** 생성 순서가 곧 대기 순서다. */
    private final Map<String, Ticket> tickets = new LinkedHashMap<>();
    private final Map<String, Server> servers = new LinkedHashMap<>();

    @Autowired
    public RoomAllocationService(GameServerProperties properties) {
        this(properties, Clock.systemUTC());
    }

    RoomAllocationService(GameServerProperties properties, Clock clock) {
        this.properties = properties;
        this.clock = clock;
    }

    public GameServerProperties properties() {
        return properties;
    }

    /** api.md 10-1. 같은 계정의 기다리던 티켓은 새 티켓으로 대체된다 — 한 사람이 자리를 둘 잡지 않는다. */
    public synchronized TicketView createTicket(String accountId, String ownerNickname, TicketRequest request) {
        Instant now = clock.instant();
        expire(now);
        if (request == null) throw new RoomExceptions.Invalid("본문이 필요합니다");
        String title = request.title() == null ? "" : request.title().trim();
        if (title.length() > MAX_TITLE_LENGTH) throw new RoomExceptions.Invalid("title 이 너무 깁니다");
        int maxPlayers = request.maxPlayers() == null ? 0 : request.maxPlayers();
        if (maxPlayers < MIN_PLAYERS || maxPlayers > MAX_PLAYERS) throw new RoomExceptions.Invalid("maxPlayers 범위 밖");
        String password = request.password() == null ? "" : request.password();
        if (password.length() > MAX_PASSWORD_LENGTH) throw new RoomExceptions.Invalid("password 가 너무 깁니다");

        for (Ticket existing : tickets.values()) {
            if (existing.accountId.equals(accountId) && existing.active()) finish(existing, Status.CANCELED, now);
        }

        Ticket ticket = new Ticket(UUID.randomUUID().toString(), accountId, ownerNickname == null ? "" : ownerNickname,
                title, request.isPublic() == null || request.isPublic(), maxPlayers, password, now);
        tickets.put(ticket.id, ticket);
        LOGGER.info("room ticket {} QUEUED (account={}, maxPlayers={}, locked={})",
                ticket.id, accountId, maxPlayers, !password.isEmpty());
        return view(ticket);
    }

    /** api.md 10-2. 조회가 곧 "아직 기다린다" 는 신호다. */
    public synchronized TicketView getTicket(String accountId, String ticketId) {
        Instant now = clock.instant();
        expire(now);
        Ticket ticket = find(accountId, ticketId);
        ticket.lastPolledAt = now;
        return view(ticket);
    }

    /** api.md 10-3. 이미 끝난 티켓은 그대로 돌려준다. */
    public synchronized TicketView cancel(String accountId, String ticketId) {
        Instant now = clock.instant();
        expire(now);
        Ticket ticket = find(accountId, ticketId);
        if (ticket.active()) {
            finish(ticket, Status.CANCELED, now);
            LOGGER.info("room ticket {} CANCELED by owner", ticket.id);
        }
        return view(ticket);
    }

    /** api.md 10-4. 배정이 있으면 돌려준다. */
    public synchronized Assignment heartbeat(HeartbeatRequest request) {
        Instant now = clock.instant();
        expire(now);
        if (request == null || isBlank(request.serverId())) throw new RoomExceptions.Invalid("serverId 가 필요합니다");
        String state = request.state() == null ? "" : request.state().trim().toUpperCase(Locale.ROOT);
        if (!"IDLE".equals(state) && !"BUSY".equals(state)) throw new RoomExceptions.Invalid("state 는 IDLE 또는 BUSY");

        Server server = servers.computeIfAbsent(request.serverId().trim(), Server::new);
        boolean firstSeen = server.lastSeen == null;
        server.lastSeen = now;
        if (firstSeen) LOGGER.info("game server {} registered", server.id);

        if (!isBlank(request.failedTicketId())) {
            Ticket failed = tickets.get(request.failedTicketId());
            if (failed != null && failed.status == Status.STARTING && server.id.equals(failed.serverId)) {
                finish(failed, Status.FAILED, now);
                LOGGER.warn("room ticket {} FAILED — server {} could not open the room", failed.id, server.id);
            }
        }

        if ("BUSY".equals(state)) {
            server.busy = true;
            server.ticketId = request.ticketId();
            Ticket ticket = isBlank(request.ticketId()) ? null : tickets.get(request.ticketId());
            if (ticket != null && ticket.status == Status.STARTING && server.id.equals(ticket.serverId)
                    && ticket.roomCode.equals(request.roomCode())) {
                ticket.status = Status.READY;
                ticket.finishedAt = now;
                LOGGER.info("room ticket {} READY — room {} on {}", ticket.id, ticket.roomCode, server.id);
            }
            return null;
        }

        // IDLE: 이 서버가 들고 있던 STARTING 티켓이 아직 그대로면 방을 열지 못한 채 재시작한 것이다 — 다시 줄 세운다.
        if (!isBlank(server.ticketId)) {
            Ticket held = tickets.get(server.ticketId);
            if (held != null && held.status == Status.STARTING && server.id.equals(held.serverId)) requeue(held);
        }
        server.busy = false;
        server.ticketId = null;

        if (roomsInUse() >= properties.maxRooms()) return null;
        Ticket next = firstQueued();
        if (next == null) return null;
        next.status = Status.STARTING;
        next.serverId = server.id;
        next.roomCode = UUID.randomUUID().toString().replace("-", "").substring(0, 8).toUpperCase(Locale.ROOT);
        server.ticketId = next.id;
        LOGGER.info("room ticket {} STARTING — room {} assigned to {}", next.id, next.roomCode, server.id);
        return new Assignment(next.id, next.roomCode, next.title, next.isPublic, next.maxPlayers, next.password,
                next.ownerNickname);
    }

    private int roomsInUse() {
        int count = 0;
        for (Server server : servers.values()) {
            if (server.busy || !isBlank(server.ticketId)) count++;
        }
        return count;
    }

    private Ticket firstQueued() {
        for (Ticket ticket : tickets.values()) {
            if (ticket.status == Status.QUEUED) return ticket;
        }
        return null;
    }

    private Ticket find(String accountId, String ticketId) {
        Ticket ticket = ticketId == null ? null : tickets.get(ticketId);
        if (ticket == null || !ticket.accountId.equals(accountId)) {
            throw new RoomExceptions.TicketNotFound("ticketId=" + ticketId);
        }
        return ticket;
    }

    private TicketView view(Ticket ticket) {
        int position = 0;
        if (ticket.status == Status.QUEUED) {
            for (Ticket other : tickets.values()) {
                if (other.status == Status.QUEUED) position++;
                if (other == ticket) break;
            }
        }
        return new TicketView(ticket.id, ticket.status.name(), position,
                ticket.status == Status.READY ? ticket.roomCode : "");
    }

    private void requeue(Ticket ticket) {
        LOGGER.warn("room ticket {} re-QUEUED — server {} went away before opening room {}",
                ticket.id, ticket.serverId, ticket.roomCode);
        ticket.status = Status.QUEUED;
        ticket.serverId = null;
        ticket.roomCode = "";
    }

    private static void finish(Ticket ticket, Status status, Instant now) {
        ticket.status = status;
        ticket.finishedAt = now;
    }

    private void expire(Instant now) {
        Duration serverTimeout = Duration.ofSeconds(properties.serverTimeoutSeconds());
        Iterator<Server> serverIt = servers.values().iterator();
        while (serverIt.hasNext()) {
            Server server = serverIt.next();
            if (server.lastSeen != null && server.lastSeen.plus(serverTimeout).isBefore(now)) {
                LOGGER.warn("game server {} timed out", server.id);
                Ticket held = isBlank(server.ticketId) ? null : tickets.get(server.ticketId);
                if (held != null && held.status == Status.STARTING) requeue(held);
                serverIt.remove();
            }
        }

        Duration idle = Duration.ofSeconds(properties.ticketIdleTimeoutSeconds());
        Duration retention = Duration.ofSeconds(properties.finishedRetentionSeconds());
        Iterator<Ticket> ticketIt = tickets.values().iterator();
        while (ticketIt.hasNext()) {
            Ticket ticket = ticketIt.next();
            if (ticket.active() && ticket.lastPolledAt.plus(idle).isBefore(now)) {
                LOGGER.info("room ticket {} abandoned ({})", ticket.id, ticket.status);
                finish(ticket, Status.CANCELED, now);
            }
            if (!ticket.active() && ticket.finishedAt != null && ticket.finishedAt.plus(retention).isBefore(now)) {
                ticketIt.remove();
            }
        }
    }

    private static boolean isBlank(String value) {
        return value == null || value.isBlank();
    }
}
