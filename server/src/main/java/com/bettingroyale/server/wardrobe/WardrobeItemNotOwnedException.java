package com.bettingroyale.server.wardrobe;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/**
 * 소유하지 않은 외형 아이템을 프리셋에 넣으려 했다 (api.md 8-4, S-UX-F3CJEP).
 *
 * <p>카탈로그에 없는 아이템은 이것이 아니라 400 이다 — 화면이 "없는 아이템"과 "잠긴 아이템"을
 * 다른 문구로 안내할 수 있어야 한다.
 */
public class WardrobeItemNotOwnedException extends ApiException {

    public WardrobeItemNotOwnedException(String accountId, String slot, String itemId) {
        super(HttpStatus.FORBIDDEN, "WARDROBE_ITEM_NOT_OWNED",
                "소유하지 않은 외형 아이템입니다",
                "accountId=" + accountId + ", slot=" + slot + ", itemId=" + itemId);
    }
}
