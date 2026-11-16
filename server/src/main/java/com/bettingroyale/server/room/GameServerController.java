package com.bettingroyale.server.room;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;

import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 전용 게임 서버 하트비트 (docs/server/api.md 10-4, S15P21A606-321).
 *
 * <p>🔒 {@code X-Game-Server-Key} 필수 — env {@code GAME_SERVER_KEY} 와 같아야 한다. 키가 설정되지 않은 환경은
 * 503 {@code GAME_SERVER_DISABLED} 다(Production 은 아직 Host 모드).
 *
 * <p>게임 서버는 docker 네트워크 안에서 백엔드를 직접 부른다(nginx 를 거치지 않는다) — 이 경로를 공개 프록시에
 * 노출할 이유가 없지만, 노출되더라도 키 없이는 아무것도 할 수 없다.
 */
@RestController
@RequestMapping("/api/v1/game-servers")
public class GameServerController {

    public static final String SERVER_KEY_HEADER = "X-Game-Server-Key";

    private final RoomAllocationService rooms;

    public GameServerController(RoomAllocationService rooms) {
        this.rooms = rooms;
    }

    /** 배정이 없으면 {@code assignment} 가 {@code null} 이다. */
    public record HeartbeatResponse(RoomAllocationService.Assignment assignment) {
    }

    @PostMapping("/heartbeat")
    public HeartbeatResponse heartbeat(
            @RequestHeader(value = SERVER_KEY_HEADER, required = false) String key,
            @RequestBody(required = false) RoomAllocationService.HeartbeatRequest body) {
        GameServerProperties properties = rooms.properties();
        if (!properties.enabled()) throw new RoomExceptions.Disabled();
        if (key == null || !MessageDigest.isEqual(
                key.trim().getBytes(StandardCharsets.UTF_8), properties.key().getBytes(StandardCharsets.UTF_8))) {
            throw new RoomExceptions.ServerKeyInvalid();
        }
        return new HeartbeatResponse(rooms.heartbeat(body));
    }
}
