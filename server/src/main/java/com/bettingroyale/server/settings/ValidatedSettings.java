package com.bettingroyale.server.settings;

import java.time.Instant;

/**
 * 검증을 통과한 PUT 본문. {@code document} 는 파싱된 문서를 다시 직렬화한 정본 JSON 이며
 * 알 수 없는 category/option 필드도 그대로 보존한다 (api.md 7절).
 *
 * @param document        정본 JSON 문서 (저장·응답에 같은 문자열을 쓴다)
 * @param envelopeVersion 봉투 버전 (현재 1)
 * @param settingsVersion 클라이언트 스키마 버전 (음수 불가)
 * @param updatedAt       밀리초로 절삭된 UTC 시각 — LWW 비교 1순위
 * @param deviceId        불투명 설치 식별자 — LWW 비교 2순위
 */
public record ValidatedSettings(
        String document,
        int envelopeVersion,
        long settingsVersion,
        Instant updatedAt,
        String deviceId) {
}
