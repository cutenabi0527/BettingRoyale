package com.bettingroyale.server.wardrobe;

import java.util.ArrayList;
import java.util.EnumMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

import org.springframework.stereotype.Service;

import com.bettingroyale.server.common.BadRequestException;

/**
 * 옷장 조회·저장 유스케이스 (docs/server/api.md 8절).
 *
 * <p>검증은 <b>형식 → 카탈로그 → 소유</b> 순서로 전부 끝낸 뒤에만 저장으로 넘어간다. 순서가 계약인 이유는
 * 화면이 "없는 아이템"(400)과 "잠긴 아이템"(403)을 다른 문구로 안내해야 하기 때문이다(api.md 8-4).
 *
 * <p>검증을 클라이언트에 맡기지 않는다 — 화면이 목록을 어떻게 그리든 서버가 카탈로그와 소유를 다시 본다
 * (S-UX-F3CJEP, 프로필 초상화 3-9 와 같은 원칙).
 */
@Service
public class WardrobeService {

    /** 조회 응답 한 벌. 화면이 한 번의 호출로 그릴 수 있어야 한다. */
    public record WardrobeView(List<String> weaponTypes, List<String> slots, List<String> ownedItemIds,
            List<StoredPreset> presets) {
    }

    private final WardrobeProperties properties;
    private final WardrobeItemRepository catalog;
    private final WardrobeStore store;

    public WardrobeService(WardrobeProperties properties, WardrobeItemRepository catalog, WardrobeStore store) {
        this.properties = properties;
        this.catalog = catalog;
        this.store = store;
    }

    public WardrobeView read(String accountId) {
        Map<String, StoredPreset> stored = store.readPresets(accountId);
        List<StoredPreset> presets = new ArrayList<>();
        for (String weaponType : properties.getWeaponTypes()) {
            // 저장 전 무기 유형도 빠뜨리지 않는다 — 화면이 404 와 "빈 프리셋"을 구분할 일을 만들지 않는다.
            presets.add(stored.getOrDefault(weaponType, StoredPreset.empty(weaponType)));
        }
        return new WardrobeView(
                List.copyOf(properties.getWeaponTypes()),
                WardrobeSlot.contractNames(),
                store.ownedItemIds(accountId),
                presets);
    }

    /** 한 무기 유형의 프리셋을 통째로 교체한다. 다른 무기 유형은 건드리지 않는다(S-GD-5NRMCY 3항). */
    public StoredPreset savePreset(String accountId, String weaponType, Map<String, String> requested) {
        String canonicalWeapon = properties.canonicalWeaponType(weaponType)
                .orElseThrow(() -> new BadRequestException(
                        "지원하지 않는 무기 유형입니다",
                        "weaponType=" + weaponType + ", supported=" + properties.getWeaponTypes()));

        Map<WardrobeSlot, String> items = normalize(requested);
        requireFace(items);
        requireKnownItems(items);
        requireOwnedItems(accountId, items);
        return store.writePreset(accountId, canonicalWeapon, items);
    }

    /**
     * 얼굴은 비울 수 없다 (S-GD-5R953J).
     *
     * <p>다른 슬롯은 값이 없으면 "기본값"이라 행을 만들지 않지만, 얼굴만은 그 상태를 허용하지
     * 않는다 — 이목구비가 사라진 모델이 되기 때문이다. <b>화면만 막으면 계약이 아니라</b>
     * 다른 클라이언트나 직접 호출이 얼굴 없는 프리셋을 저장할 수 있으므로 여기서도 막는다.
     */
    private static void requireFace(Map<WardrobeSlot, String> items) {
        if (!items.containsKey(WardrobeSlot.FACE)) {
            throw new BadRequestException(
                    "얼굴은 비울 수 없습니다",
                    "slot=" + WardrobeSlot.FACE.contractName());
        }
    }

    /**
     * 살 수 있는 아이템의 메달 가격 (S-GD-5R953J).
     *
     * <p>가격이 0 인 기본 지급품은 넣지 않는다 — 살 대상이 아니므로 화면이 값을 보여 줄 일도 없다.
     */
    public Map<String, Integer> prices() {
        var result = new LinkedHashMap<String, Integer>();
        for (WardrobeItemEntity item : catalog.findAll()) {
            if (item.priceMedals() > 0) result.put(item.itemId(), item.priceMedals());
        }

        return result;
    }

    /** 신규 계정에 기본 지급분을 넣는다. 가입 트랜잭션 안에서 부른다(S-GD-5R953J). */
    public void grantSignupItems(String accountId) {
        store.grantDefaultItems(accountId);
    }

    /** 형식 검사 — 아는 슬롯인가, 길이가 맞는가. 값이 비면 "그 슬롯은 기본값"이라 행을 만들지 않는다. */
    private Map<WardrobeSlot, String> normalize(Map<String, String> requested) {
        Map<WardrobeSlot, String> items = new EnumMap<>(WardrobeSlot.class);
        if (requested == null) {
            return items;
        }
        for (Map.Entry<String, String> entry : requested.entrySet()) {
            WardrobeSlot slot = WardrobeSlot.find(entry.getKey())
                    .orElseThrow(() -> new BadRequestException(
                            "알 수 없는 외형 슬롯입니다",
                            "slot=" + entry.getKey() + ", known=" + WardrobeSlot.contractNames()));
            String itemId = entry.getValue() == null ? "" : entry.getValue().trim();
            if (itemId.isEmpty()) {
                continue;
            }
            if (itemId.length() > properties.getMaxItemIdLength()) {
                throw new BadRequestException(
                        "외형 아이템 ID 가 너무 깁니다",
                        "slot=" + slot.contractName() + ", length=" + itemId.length());
            }
            if (items.put(slot, itemId) != null) {
                // "hat" 과 "Hat" 처럼 대소문자만 다른 키가 같은 슬롯으로 접히면 어느 쪽이 이겼는지 알 수 없다.
                throw new BadRequestException(
                        "같은 슬롯이 두 번 왔습니다",
                        "slot=" + slot.contractName());
            }
        }
        return items;
    }

    /** 카탈로그 검사 — 서버가 모르는 아이템은 소유 여부를 따지기 전에 막는다. */
    private void requireKnownItems(Map<WardrobeSlot, String> items) {
        if (items.isEmpty()) {
            return;
        }
        Set<String> known = Set.copyOf(catalog.findAllItemIds());
        for (Map.Entry<WardrobeSlot, String> entry : items.entrySet()) {
            if (!known.contains(entry.getValue())) {
                throw new BadRequestException(
                        "카탈로그에 없는 외형 아이템입니다",
                        "slot=" + entry.getKey().contractName() + ", itemId=" + entry.getValue());
            }
        }
    }

    /**
     * 소유 검사 — 카탈로그에는 있으나 이 계정이 갖지 않은 아이템은 403 이다(S-UX-F3CJEP).
     *
     * <p>검사는 저장 트랜잭션 밖에서 한다. 지금은 <b>회수 경로가 없어</b> 소유가 늘기만 하므로 안전하다.
     * 회수(환불·정지 등)가 생기면 이 검사를 {@link WardrobeStore#writePreset} 의 잠금 안으로 옮긴다.
     */
    private void requireOwnedItems(String accountId, Map<WardrobeSlot, String> items) {
        if (items.isEmpty()) {
            return;
        }
        Set<String> owned = Set.copyOf(store.ownedItemIds(accountId));
        for (Map.Entry<WardrobeSlot, String> entry : items.entrySet()) {
            if (!owned.contains(entry.getValue())) {
                throw new WardrobeItemNotOwnedException(
                        accountId, entry.getKey().contractName(), entry.getValue());
            }
        }
    }
}
