package com.bettingroyale.server.settings;

import java.time.Instant;

/**
 * 저장된 정본 설정의 읽기 전용 스냅샷. 컨트롤러는 {@code document} 를 그대로 본문으로 돌려준다 —
 * 저장된 바이트와 응답 바이트가 같아야 클라이언트의 "같은 버전 응답으로만 Dirty 해제" 규칙이 성립한다(S-CLI-JZKPDF).
 */
public record StoredSettings(
        String document,
        int envelopeVersion,
        long settingsVersion,
        Instant updatedAt,
        String deviceId) {

    static StoredSettings of(UserSettingsEntity entity) {
        return new StoredSettings(entity.document(), entity.envelopeVersion(), entity.settingsVersion(),
                entity.updatedAt(), entity.deviceId());
    }
}
