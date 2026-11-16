package com.bettingroyale.server.room;

import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.auth.AuthService;

/**
 * 전용 게임 서버 방 배정 — 클라이언트 쪽 (docs/server/api.md 10-1~10-3, S15P21A606-321).
 *
 * <p>🔒 Bearer JWT 필수. 티켓 주인은 토큰에서만 정한다. 방장 이름은 계정 닉네임이다.
 */
@RestController
@RequestMapping("/api/v1/rooms/tickets")
public class RoomTicketController {

    private final AuthService auth;
    private final RoomAllocationService rooms;

    public RoomTicketController(AuthService auth, RoomAllocationService rooms) {
        this.auth = auth;
        this.rooms = rooms;
    }

    @PostMapping
    public RoomAllocationService.TicketView create(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @RequestBody(required = false) RoomAllocationService.TicketRequest body) {
        AccountEntity me = auth.authenticate(authorization);
        return rooms.createTicket(me.accountId(), me.nickname(), body);
    }

    @GetMapping("/{ticketId}")
    public RoomAllocationService.TicketView get(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @PathVariable String ticketId) {
        return rooms.getTicket(auth.authenticate(authorization).accountId(), ticketId);
    }

    @PostMapping("/{ticketId}/cancel")
    public RoomAllocationService.TicketView cancel(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @PathVariable String ticketId) {
        return rooms.cancel(auth.authenticate(authorization).accountId(), ticketId);
    }
}
