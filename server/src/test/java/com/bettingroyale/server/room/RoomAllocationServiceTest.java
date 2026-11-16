package com.bettingroyale.server.room;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;

import org.junit.jupiter.api.Test;

/** docs/server/api.md 10절 — 방 배정·대기열 규칙 (S15P21A606-321, S-ONL-E33BEK, S-UX-8MT759). */
class RoomAllocationServiceTest {

    private static final class MutableClock extends Clock {
        Instant now = Instant.parse("2026-09-25T00:00:00Z");

        @Override
        public ZoneId getZone() {
            return ZoneId.of("UTC");
        }

        @Override
        public Clock withZone(ZoneId zone) {
            return this;
        }

        @Override
        public Instant instant() {
            return now;
        }

        void advance(long seconds) {
            now = now.plus(Duration.ofSeconds(seconds));
        }
    }

    private final MutableClock clock = new MutableClock();
    private final RoomAllocationService service =
            new RoomAllocationService(new GameServerProperties("k", 1, 15, 20, 120), clock);

    private static RoomAllocationService.TicketRequest request() {
        return new RoomAllocationService.TicketRequest("방", true, 6, "");
    }

    private static RoomAllocationService.HeartbeatRequest idle(String server) {
        return new RoomAllocationService.HeartbeatRequest(server, "IDLE", "", "", 0, "");
    }

    private static RoomAllocationService.HeartbeatRequest busy(String server, String ticket, String room) {
        return new RoomAllocationService.HeartbeatRequest(server, "BUSY", ticket, room, 1, "");
    }

    @Test
    void 한가한_서버가_가장_오래_기다린_티켓을_받고_BUSY_보고로_READY_가_된다() {
        var first = service.createTicket("a", "에이", request());
        var second = service.createTicket("b", "비", request());
        assertEquals("QUEUED", first.status());
        assertEquals(1, first.position());
        assertEquals(2, second.position());

        var assignment = service.heartbeat(idle("s1"));
        assertNotNull(assignment);
        assertEquals(first.ticketId(), assignment.ticketId());
        assertEquals("에이", assignment.ownerNickname());
        assertEquals("STARTING", service.getTicket("a", first.ticketId()).status());
        assertEquals(1, service.getTicket("b", second.ticketId()).position(), "앞사람이 빠지면 순번이 당겨진다");

        service.heartbeat(busy("s1", assignment.ticketId(), assignment.roomCode()));
        var ready = service.getTicket("a", first.ticketId());
        assertEquals("READY", ready.status());
        assertEquals(assignment.roomCode(), ready.roomCode());
    }

    @Test
    void 동시_방_상한을_넘으면_배정하지_않고_기다리게_한다() {
        var first = service.createTicket("a", "에이", request());
        var assignment = service.heartbeat(idle("s1"));
        service.heartbeat(busy("s1", assignment.ticketId(), assignment.roomCode()));
        var second = service.createTicket("b", "비", request());

        assertNull(service.heartbeat(idle("s2")), "max-rooms=1 이면 두 번째 서버가 한가해도 배정하지 않는다");
        assertEquals("QUEUED", service.getTicket("b", second.ticketId()).status());
        assertEquals("READY", service.getTicket("a", first.ticketId()).status());
    }

    @Test
    void 방을_열지_못한_서버가_다시_IDLE_로_오면_티켓을_다시_줄_세운다() {
        var ticket = service.createTicket("a", "에이", request());
        var assignment = service.heartbeat(idle("s1"));
        assertNotNull(assignment);
        var again = service.heartbeat(idle("s1"));
        assertNotNull(again, "재시작한 서버에 같은 티켓을 다시 준다");
        assertEquals(ticket.ticketId(), again.ticketId());
    }

    @Test
    void 실패_보고는_티켓을_FAILED_로_닫는다() {
        var ticket = service.createTicket("a", "에이", request());
        var assignment = service.heartbeat(idle("s1"));
        service.heartbeat(new RoomAllocationService.HeartbeatRequest("s1", "IDLE", "", "", 0, assignment.ticketId()));
        assertEquals("FAILED", service.getTicket("a", ticket.ticketId()).status());
    }

    @Test
    void 서버가_끊기면_시작하던_티켓을_다시_줄_세운다() {
        var ticket = service.createTicket("a", "에이", request());
        service.heartbeat(idle("s1"));
        clock.advance(16);
        service.getTicket("a", ticket.ticketId());
        assertEquals("QUEUED", service.getTicket("a", ticket.ticketId()).status());
    }

    @Test
    void 조회가_끊긴_티켓은_버린다() {
        var ticket = service.createTicket("a", "에이", request());
        clock.advance(21);
        assertNull(service.heartbeat(idle("s1")), "떠난 사람의 티켓은 배정하지 않는다");
        assertEquals("CANCELED", service.getTicket("a", ticket.ticketId()).status());
    }

    @Test
    void 취소하면_CANCELED_이고_같은_계정의_새_요청은_앞_요청을_대체한다() {
        var first = service.createTicket("a", "에이", request());
        var second = service.createTicket("a", "에이", request());
        assertEquals("CANCELED", service.getTicket("a", first.ticketId()).status());
        assertEquals("CANCELED", service.cancel("a", second.ticketId()).status());
    }

    @Test
    void 남의_티켓은_404_다() {
        var ticket = service.createTicket("a", "에이", request());
        assertThrows(RoomExceptions.TicketNotFound.class, () -> service.getTicket("b", ticket.ticketId()));
    }

    @Test
    void 입력_범위를_검사한다() {
        assertThrows(RoomExceptions.Invalid.class,
                () -> service.createTicket("a", "에이", new RoomAllocationService.TicketRequest("방", true, 0, "")));
        assertThrows(RoomExceptions.Invalid.class,
                () -> service.createTicket("a", "에이", new RoomAllocationService.TicketRequest("x".repeat(41), true, 6, "")));
        assertThrows(RoomExceptions.Invalid.class,
                () -> service.heartbeat(new RoomAllocationService.HeartbeatRequest("s1", "WHAT", "", "", 0, "")));
    }
}
