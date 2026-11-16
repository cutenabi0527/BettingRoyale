package com.bettingroyale.server.wardrobe;

import java.time.Instant;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * 한 무기 유형의 저장 상태 스냅샷 (api.md 8-3·8-4).
 *
 * @param weaponType 설정의 정본 표기 (현재 Sword / Bow)
 * @param revision   0 이면 <b>한 번도 저장하지 않았다</b> — 그 동안 클라이언트는 appearancePresetSeed 를 쓴다
 * @param updatedAt  revision 0 이면 {@code null}
 * @param items      슬롯 → 아이템 ID. 없는 슬롯은 기본값이다
 */
public record StoredPreset(String weaponType, long revision, Instant updatedAt,
        Map<WardrobeSlot, String> items) {

    public StoredPreset {
        items = items == null ? Map.of() : Collections.unmodifiableMap(new LinkedHashMap<>(items));
    }

    /** 아직 저장된 적이 없는 무기 유형. 조회 응답에서 모든 무기 유형이 빠짐없이 나오도록 이것으로 채운다. */
    static StoredPreset empty(String weaponType) {
        return new StoredPreset(weaponType, 0L, null, Map.of());
    }
}
