package com.bettingroyale.server.wardrobe;

import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.common.AccountNotFoundException;

import jakarta.persistence.EntityManager;
import jakarta.persistence.LockModeType;
import jakarta.persistence.PersistenceContext;

/**
 * 옷장 정본의 트랜잭션 경계 (docs/server/api.md 8절).
 *
 * <p>쓰기는 <b>계정 행 비관적 잠금</b> 아래에서만 한다. 프리셋 저장은 "슬롯 행 전체 삭제 후 재삽입"이라,
 * 같은 계정·같은 무기 유형의 요청 둘이 겹치면 A 의 삭제와 B 의 삽입이 섞여 <b>어느 쪽 저장도 아닌 한 벌</b>이
 * 남을 수 있다. 계정 행을 먼저 잠그면 두 번째 요청은 첫 번째의 커밋을 본 뒤 온전한 교체를 한다.
 * 잠금 대상이 프리셋 행이 아니라 계정 행인 이유는 <b>아직 존재하지 않는 행은 잠글 수 없기</b> 때문이다
 * ({@code UserSettingsStore} 와 같은 이유, D-BE-5DNW6H).
 *
 * <p>계정은 호출자가 이미 JWT 로 확인한 것만 넘긴다 — 이 서비스는 계정 ID 를 요청 본문이나 경로에서 받지 않는다.
 */
@Service
public class WardrobeStore {

    private final WardrobePresetRepository presets;
    private final WardrobePresetItemRepository presetItems;
    private final AccountWardrobeItemRepository ownership;

    /** 트랜잭션 바운드 EntityManager 프록시 주입은 이 애너테이션이 정본 경로다. */
    @PersistenceContext
    private EntityManager entityManager;

    public WardrobeStore(WardrobePresetRepository presets, WardrobePresetItemRepository presetItems,
            AccountWardrobeItemRepository ownership) {
        this.presets = presets;
        this.presetItems = presetItems;
        this.ownership = ownership;
    }

    @Transactional(readOnly = true)
    public List<String> ownedItemIds(String accountId) {
        return ownership.findOwnedItemIds(accountId);
    }

    /** 저장된 무기 유형만 담는다 — 저장 전 무기 유형을 채우는 것은 {@link WardrobeService} 의 몫이다. */
    @Transactional(readOnly = true)
    public Map<String, StoredPreset> readPresets(String accountId) {
        Map<String, Map<WardrobeSlot, String>> itemsByWeapon = new HashMap<>();
        for (WardrobePresetItemEntity item : presetItems.findByAccountId(accountId)) {
            WardrobeSlot slot = WardrobeSlot.find(item.slot()).orElse(null);
            if (slot == null) {
                // 이 빌드가 모르는 슬롯 이름이 저장돼 있다 = 코드가 스키마보다 뒤처졌다는 뜻이다(롤백 직후 등).
                // 조회 전체를 실패시키지 않고 그 칸만 건너뛴다 — 나머지 슬롯은 정상적으로 복원돼야 한다.
                continue;
            }
            itemsByWeapon.computeIfAbsent(item.weaponType(), key -> new EnumMap<>(WardrobeSlot.class))
                    .put(slot, item.itemId());
        }

        Map<String, StoredPreset> byWeapon = new HashMap<>();
        for (WardrobePresetEntity preset : presets.findByAccountId(accountId)) {
            byWeapon.put(preset.weaponType(), snapshot(preset,
                    itemsByWeapon.getOrDefault(preset.weaponType(), Map.of())));
        }
        return byWeapon;
    }

    /**
     * 한 무기 유형의 프리셋을 통째로 교체하고 저장 후의 정본을 돌려준다.
     * 다른 무기 유형은 건드리지 않는다(S-GD-5NRMCY 3항).
     *
     * <p>내용이 같으면 쓰지 않고 같은 {@code revision} 을 돌려준다 — 저장 버튼을 여러 번 눌러도 행이 흔들리지 않는다.
     */
    @Transactional
    public StoredPreset writePreset(String accountId, String weaponType, Map<WardrobeSlot, String> items) {
        AccountEntity account = entityManager.find(AccountEntity.class, accountId, LockModeType.PESSIMISTIC_WRITE);
        if (account == null) {
            throw new AccountNotFoundException(accountId);
        }

        WardrobePresetEntity header = entityManager.find(WardrobePresetEntity.class,
                new WardrobePresetEntity.Key(accountId, weaponType));
        List<WardrobePresetItemEntity> existingRows =
                presetItems.findByAccountIdAndWeaponType(accountId, weaponType);
        Map<WardrobeSlot, String> existing = toSlotMap(existingRows);
        if (header != null && existing.equals(items)) {
            return snapshot(header, existing);
        }

        Instant now = Instant.now().truncatedTo(ChronoUnit.MILLIS);
        if (header == null) {
            // 배정 PK 신규 행 — merge(SELECT 후 UPDATE 가능)가 아니라 명시적 INSERT 다 (D-BE-SG4AD6).
            header = new WardrobePresetEntity(accountId, weaponType, now);
            entityManager.persist(header);
            // 슬롯 행이 머리 행을 FK 로 참조하므로 먼저 밀어 넣는다.
            entityManager.flush();
        } else {
            header.markChanged(now);
            // 한 건씩 remove 한다 — 대량 delete 쿼리는 영속성 컨텍스트를 건너뛰어, 방금 읽은 행이
            // 남아 있는 채로 같은 PK 를 persist 하게 된다. 한 프리셋은 최대 11행이라 비용도 문제가 아니다.
            existingRows.forEach(entityManager::remove);
            entityManager.flush();
        }

        for (Map.Entry<WardrobeSlot, String> entry : items.entrySet()) {
            entityManager.persist(
                    new WardrobePresetItemEntity(accountId, weaponType, entry.getKey(), entry.getValue()));
        }
        entityManager.flush();
        return snapshot(header, items);
    }

    /**
     * 기본 지급분을 넣는다. 가입 트랜잭션 안에서 부른다 — 지급 목록의 정본은
     * {@code wardrobe_items.granted_by_default} 한 곳이다.
     *
     * <p>현재 범위는 <b>카탈로그 전량</b>이다(S-GD-ZVME43). S-GD-9BFMZ6(임의 전량) →
     * S-GD-5R953J(5종만) → S-GD-ZVME43(다시 전량) 순으로 바뀌었으니 옛 스펙을 근거로 읽지 않는다.
     * 범위가 또 바뀌어도 <b>이 코드는 그대로다</b> — 마이그레이션이 그 열만 고친다.
     */
    @Transactional
    public void grantDefaultItems(String accountId) {
        ownership.insertDefaultGrants(accountId, Instant.now());
    }

    private static Map<WardrobeSlot, String> toSlotMap(List<WardrobePresetItemEntity> rows) {
        Map<WardrobeSlot, String> map = new EnumMap<>(WardrobeSlot.class);
        for (WardrobePresetItemEntity row : rows) {
            WardrobeSlot.find(row.slot()).ifPresent(slot -> map.put(slot, row.itemId()));
        }
        return map;
    }

    private static StoredPreset snapshot(WardrobePresetEntity header, Map<WardrobeSlot, String> items) {
        Map<WardrobeSlot, String> ordered = new EnumMap<>(WardrobeSlot.class);
        ordered.putAll(items);
        return new StoredPreset(header.weaponType(), header.revision(), header.updatedAt(), ordered);
    }
}
