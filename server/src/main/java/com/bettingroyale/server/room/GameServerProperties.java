package com.bettingroyale.server.room;

import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * 전용 게임 서버 방 배정 설정 (docs/server/api.md 10절, S15P21A606-321).
 *
 * @param key                      게임 서버 키. 비어 있으면 배정 기능이 꺼진다(하트비트 503). 서버 측 시크릿이라
 *                                 env(GAME_SERVER_KEY)로만 받는다 — D-036.
 * @param maxRooms                 동시에 돌릴 방 수 상한. 정민호 2026-09-25 확정 시작값 1(S-ONL-XBB2VB).
 * @param serverTimeoutSeconds     하트비트가 이만큼 끊기면 서버가 사라진 것으로 본다.
 * @param ticketIdleTimeoutSeconds 대기 중 티켓의 조회가 이만큼 끊기면 버린다 — 떠난 사람이 자리를 잡고 있지 않게.
 * @param finishedRetentionSeconds 끝난 티켓(READY·FAILED·CANCELED)을 조회용으로 남겨 두는 시간.
 */
@ConfigurationProperties(prefix = "game-server")
public record GameServerProperties(
        String key,
        int maxRooms,
        int serverTimeoutSeconds,
        int ticketIdleTimeoutSeconds,
        int finishedRetentionSeconds) {

    public GameServerProperties {
        key = key == null ? "" : key.trim();
        if (maxRooms <= 0) maxRooms = 1;
        if (serverTimeoutSeconds <= 0) serverTimeoutSeconds = 15;
        if (ticketIdleTimeoutSeconds <= 0) ticketIdleTimeoutSeconds = 20;
        if (finishedRetentionSeconds <= 0) finishedRetentionSeconds = 120;
    }

    public boolean enabled() {
        return !key.isEmpty();
    }
}
