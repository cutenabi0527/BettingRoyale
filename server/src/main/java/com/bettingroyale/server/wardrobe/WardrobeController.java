package com.bettingroyale.server.wardrobe;

import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import jakarta.validation.Valid;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.auth.AuthService;

/**
 * 계정 외형 "옷장" (docs/server/api.md 8절).
 *
 * <p>🔒 두 엔드포인트 모두 Bearer JWT 필수다. <b>계정은 토큰에서만</b> 정한다 — 경로에도 본문에도 계정 ID 를
 * 받지 않으므로 남의 외형을 읽거나 쓸 경로 자체가 없다(7절 설정과 같은 규칙). 토큰 실패는 3-8 과 같은 401 규약
 * ({@code TOKEN_INVALID}/{@code TOKEN_EXPIRED}/{@code SESSION_REPLACED})을 그대로 쓴다.
 *
 * <p>Photon 식별자는 어디에도 등장하지 않는다(server/AGENTS.md 규칙 3).
 */
@RestController
@RequestMapping("/api/v1/accounts/me/wardrobe")
public class WardrobeController {

    /**
     * 계약의 시각 표기는 항상 소수 세 자리 UTC 다(7절과 같다). Jackson 기본 Instant 직렬화에 맡기면
     * 표기가 설정에 따라 흔들리므로 문자열로 고정한다.
     */
    private static final DateTimeFormatter TIMESTAMP =
            DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'").withZone(ZoneOffset.UTC);

    /** {@code revision} 이 0 이면 한 번도 저장하지 않은 것이고 {@code updatedAt} 은 null 이다 (api.md 8-2). */
    public record PresetResponse(String weaponType, long revision, String updatedAt,
            Map<String, String> items) {
    }

    /** 잠긴 아이템의 가격. 화면이 "얼마가 드는지" 를 보여 주려면 필요하다 (S-GD-5R953J). */
    public record PriceResponse(String itemId, int medals) {
    }

    /**
     * {@code medalBalance} 를 함께 싣는다 — 화면이 "살 수 있는가" 를 판정하려면 잔액이 필요한데,
     * 계정을 따로 조회하게 만들 이유가 없다.
     */
    public record WardrobeResponse(List<String> weaponTypes, List<String> slots,
            List<String> ownedItemIds, List<PresetResponse> presets,
            List<PriceResponse> prices, long medalBalance) {
    }

    /** {@code items} 가 없거나 비면 <b>모든 슬롯을 기본값으로</b> 저장한다 — 전체 교체(PUT)다 (api.md 8-4). */
    public record PresetRequest(Map<String, String> items) {
    }

    /** 외형 의상 구매 요청 (S-GD-5R953J). */
    public record PurchaseRequest(String itemId) {
    }

    /** 구매 결과 — 산 아이템과 남은 잔액. 화면이 잔액을 다시 조회하지 않게 한다. */
    public record PurchaseResponse(String itemId, long medalBalance, List<String> ownedItemIds) {
    }

    private final AuthService auth;
    private final WardrobeService wardrobe;
    private final WardrobePurchaseService purchases;

    public WardrobeController(AuthService auth, WardrobeService wardrobe,
            WardrobePurchaseService purchases) {
        this.auth = auth;
        this.wardrobe = wardrobe;
        this.purchases = purchases;
    }

    @GetMapping
    public WardrobeResponse get(
            @RequestHeader(value = "Authorization", required = false) String authorization) {
        AccountEntity account = auth.authenticate(authorization);
        WardrobeService.WardrobeView view = wardrobe.read(account.accountId());
        return new WardrobeResponse(
                view.weaponTypes(),
                view.slots(),
                view.ownedItemIds(),
                view.presets().stream().map(WardrobeController::toResponse).toList(),
                wardrobe.prices().entrySet().stream()
                        .map(entry -> new PriceResponse(entry.getKey(), entry.getValue()))
                        .toList(),
                account.medalBalance());
    }

    /**
     * 한 무기 유형의 프리셋을 통째로 교체한다. 다른 무기 유형의 프리셋은 덮어쓰지 않는다
     * (S-GD-5NRMCY 3항). 같은 내용 재저장은 저장을 건너뛰고 같은 {@code revision} 을 돌려준다.
     */
    @PutMapping("/presets/{weaponType}")
    public PresetResponse putPreset(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @PathVariable String weaponType,
            @RequestBody(required = false) PresetRequest request) {
        // 본문 해석 전에 인증부터 끝낸다 — 인증되지 않은 요청에 검증 비용을 쓰지 않는다.
        AccountEntity account = auth.authenticate(authorization);
        Map<String, String> items = request == null ? Map.of() : request.items();
        return toResponse(wardrobe.savePreset(account.accountId(), weaponType, items));
    }

    /**
     * 외형 의상을 메달로 산다 (S-GD-5R953J).
     *
     * <p>지금은 상점이 없어 잠긴 칸에서 바로 산다. 상점이 생기면 잠금 자체가 사라진다.
     */
    @PostMapping("/purchases")
    public PurchaseResponse purchase(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @Valid @RequestBody PurchaseRequest request) {
        AccountEntity account = auth.authenticate(authorization);
        long balance = purchases.purchase(account, request.itemId());
        return new PurchaseResponse(
                request.itemId(), balance, wardrobe.read(account.accountId()).ownedItemIds());
    }

    private static PresetResponse toResponse(StoredPreset preset) {
        Map<String, String> items = new LinkedHashMap<>();
        preset.items().forEach((slot, itemId) -> items.put(slot.contractName(), itemId));
        return new PresetResponse(
                preset.weaponType(),
                preset.revision(),
                preset.updatedAt() == null ? null : TIMESTAMP.format(preset.updatedAt()),
                items);
    }
}
