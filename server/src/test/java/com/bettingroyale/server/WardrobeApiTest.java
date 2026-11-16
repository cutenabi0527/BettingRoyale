package com.bettingroyale.server;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.util.UUID;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import org.hamcrest.Matchers;
import org.junit.jupiter.api.Assertions;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

/**
 * docs/server/api.md 8절(계정 외형 "옷장") 계약 검증.
 *
 * <p>근거 스펙 S-GD-5NRMCY(무기 유형별 프리셋)·S-UX-F3CJEP(소유한 의상만),
 * 근거 결정 D-BE-Z2HMR6(문자열 아이템 ID)·D-BE-5DNW6H(서버 발급 revision, 조건부 쓰기 없음).
 *
 * <p>기본 지급은 <b>카탈로그 전량</b>이다(S-GD-ZVME43 — S-GD-5R953J 의 "5종만" 을 되돌렸다).
 * 그래서 <b>카탈로그 안에는 잠긴 옷이 하나도 없다.</b> 소유·구매 관문 시험은 그대로 두되
 * <b>시험용 잠금 아이템을 직접 넣어서</b> 한다 — 관문은 살아 있어야 하고 지급 범위만 바뀌었기 때문이다.
 * 카탈로그의 아무 옷이나 골라 "잠겼을 것" 이라고 가정하면 지급 범위를 되돌릴 때마다 시험이 깨진다.
 *
 * <p>얼굴은 비울 수 없으므로(S-GD-5R953J 의 이 규칙은 유효하다) 모든 저장 본문에 {@code face} 가 들어간다.
 */
@SpringBootTest
@AutoConfigureMockMvc
class WardrobeApiTest {

    /** 저장 시험에 쓰는 대표 의상들. 전량 지급이라 특별한 지위는 없고 이름이 고정돼 있을 뿐이다. */
    private static final String DEFAULT_BODY = "Body_11";
    private static final String DEFAULT_FACE = "Male_Emotion_Usual_01";
    private static final String DEFAULT_OUTWEAR = "Outwear_19";
    private static final String DEFAULT_PANTS = "Pants_02";
    private static final String DEFAULT_SHOE = "Shoe_Sneakers_05";

    /** 얼굴은 비울 수 없다. 저장 본문에 항상 들어가는 최소 조각이다. */
    private static final String FACE_FIELD = "\"face\":\"" + DEFAULT_FACE + "\"";

    private static final Pattern ACCESS_TOKEN = Pattern.compile("\"accessToken\":\"([^\"]+)\"");
    private static final Pattern ACCOUNT_ID = Pattern.compile("\"accountId\":\"([^\"]+)\"");

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private JdbcTemplate jdbc;

    // ── 8-3 조회 ────────────────────────────────────────────────────────────

    @Test
    void 조회는_저장_전에도_모든_무기_유형을_revision_0_으로_돌려준다() throws Exception {
        String token = newAccountToken();

        mockMvc.perform(get("/api/v1/accounts/me/wardrobe").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.weaponTypes").value(Matchers.contains("Sword", "Bow")))
                .andExpect(jsonPath("$.slots").value(Matchers.contains(
                        "body", "face", "glasses", "gloves", "hairstyle", "hat", "mask",
                        "outwear", "pants", "shorts", "shoe")))
                .andExpect(jsonPath("$.presets.length()").value(2))
                .andExpect(jsonPath("$.presets[0].weaponType").value("Sword"))
                .andExpect(jsonPath("$.presets[0].revision").value(0))
                .andExpect(jsonPath("$.presets[0].updatedAt").doesNotExist())
                .andExpect(jsonPath("$.presets[0].items").isEmpty())
                .andExpect(jsonPath("$.presets[1].weaponType").value("Bow"))
                .andExpect(jsonPath("$.presets[1].revision").value(0));
    }

    @Test
    void 신규_계정은_카탈로그_전량을_소유한다() throws Exception {
        // S-GD-ZVME43: 잠긴 의상이 없다. 개수를 상수로 박지 않고 카탈로그와 맞춰 본다 —
        // 카탈로그에 옷을 추가했는데 지급을 빠뜨리면 그때 여기서 걸려야 한다.
        String token = newAccountToken();

        mockMvc.perform(get("/api/v1/accounts/me/wardrobe").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.ownedItemIds.length()").value((int) catalogSize()))
                .andExpect(jsonPath("$.ownedItemIds").value(Matchers.hasItems(
                        DEFAULT_BODY, DEFAULT_FACE, DEFAULT_OUTWEAR, DEFAULT_PANTS, DEFAULT_SHOE)))
                // ID 오름차순 — 표시 순서는 클라이언트 카탈로그가 정한다 (api.md 8-3).
                .andExpect(jsonPath("$.ownedItemIds[0]").value(firstCatalogItemId()));
    }

    @Test
    void 토큰_없는_조회는_401_TOKEN_INVALID_다() throws Exception {
        mockMvc.perform(get("/api/v1/accounts/me/wardrobe"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("TOKEN_INVALID"));
    }

    // ── 8-4 저장 ────────────────────────────────────────────────────────────

    @Test
    void 저장은_revision_을_올리고_슬롯을_그대로_돌려준다() throws Exception {
        String token = newAccountToken();

        mockMvc.perform(putPreset(token, "Sword",
                        "{\"items\":{\"body\":\"" + DEFAULT_BODY + "\"," + FACE_FIELD + "}}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.weaponType").value("Sword"))
                .andExpect(jsonPath("$.revision").value(1))
                .andExpect(jsonPath("$.updatedAt").isString())
                .andExpect(jsonPath("$.items.body").value(DEFAULT_BODY))
                .andExpect(jsonPath("$.items.face").value(DEFAULT_FACE));

        mockMvc.perform(get("/api/v1/accounts/me/wardrobe").header("Authorization", "Bearer " + token))
                .andExpect(jsonPath("$.presets[0].revision").value(1))
                .andExpect(jsonPath("$.presets[0].items.face").value(DEFAULT_FACE));
    }

    @Test
    void 같은_내용_재저장은_revision_을_올리지_않는다() throws Exception {
        String token = newAccountToken();
        String body = "{\"items\":{" + FACE_FIELD + ",\"outwear\":\"" + DEFAULT_OUTWEAR + "\"}}";

        mockMvc.perform(putPreset(token, "Sword", body))
                .andExpect(jsonPath("$.revision").value(1));
        mockMvc.perform(putPreset(token, "Sword", body))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.revision").value(1));

        // 내용이 바뀐 저장만 +1 한다.
        mockMvc.perform(putPreset(token, "Sword", "{\"items\":{" + FACE_FIELD + ",\"pants\":\"" + DEFAULT_PANTS + "\"}}"))
                .andExpect(jsonPath("$.revision").value(2));
    }

    @Test
    void 저장은_전체_교체라_빠진_슬롯은_기본값이_된다() throws Exception {
        String token = newAccountToken();

        mockMvc.perform(putPreset(token, "Sword",
                        "{\"items\":{" + FACE_FIELD + ",\"pants\":\"" + DEFAULT_PANTS + "\"}}"))
                .andExpect(jsonPath("$.items.pants").value(DEFAULT_PANTS));

        mockMvc.perform(putPreset(token, "Sword", "{\"items\":{" + FACE_FIELD + ",\"outwear\":\"" + DEFAULT_OUTWEAR + "\"}}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.items.outwear").value(DEFAULT_OUTWEAR))
                .andExpect(jsonPath("$.items.pants").doesNotExist());

        // items 를 통째로 비우면 전부 기본값이다. "한 번도 저장 안 함"(revision 0)과는 다른 상태다.
        mockMvc.perform(putPreset(token, "Sword", "{\"items\":{" + FACE_FIELD + "}}"))
                .andExpect(status().isOk())
                // 얼굴만 남는다 — 얼굴은 비울 수 없다 (S-GD-5R953J).
                .andExpect(jsonPath("$.items.face").value(DEFAULT_FACE))
                .andExpect(jsonPath("$.items.outwear").doesNotExist())
                .andExpect(jsonPath("$.revision").value(3));
    }

    @Test
    void 한_무기의_저장은_다른_무기의_프리셋을_덮어쓰지_않는다() throws Exception {
        String token = newAccountToken();

        mockMvc.perform(putPreset(token, "Sword", "{\"items\":{" + FACE_FIELD + ",\"outwear\":\"" + DEFAULT_OUTWEAR + "\"}}"))
                .andExpect(status().isOk());
        mockMvc.perform(putPreset(token, "Bow", "{\"items\":{" + FACE_FIELD + ",\"pants\":\"" + DEFAULT_PANTS + "\"}}"))
                .andExpect(status().isOk());

        mockMvc.perform(get("/api/v1/accounts/me/wardrobe").header("Authorization", "Bearer " + token))
                .andExpect(jsonPath("$.presets[0].weaponType").value("Sword"))
                .andExpect(jsonPath("$.presets[0].items.outwear").value(DEFAULT_OUTWEAR))
                .andExpect(jsonPath("$.presets[0].items.pants").doesNotExist())
                .andExpect(jsonPath("$.presets[1].weaponType").value("Bow"))
                .andExpect(jsonPath("$.presets[1].items.pants").value(DEFAULT_PANTS))
                .andExpect(jsonPath("$.presets[1].items.outwear").doesNotExist());
    }

    @Test
    void Costume_은_점유하는_슬롯마다_같은_ID_로_저장된다() throws Exception {
        // 점유 규칙의 정본은 클라이언트 공용 카탈로그다 — 서버는 받은 그대로 보관한다 (D-BE-Z2HMR6).
        String token = newAccountToken();
        // 지금은 Costume 도 기본 지급이다(S-GD-ZVME43). 그래도 소유를 명시적으로 확보해 둔다 —
        // 지급 범위가 다시 줄어도 이 시험의 관심사("점유 슬롯마다 같은 ID")는 그대로여야 한다.
        grantOwnership(token, "Costume_1_01");

        mockMvc.perform(putPreset(token, "Sword", "{\"items\":{" + FACE_FIELD + ",\"outwear\":\"Costume_1_01\","
                        + "\"pants\":\"Costume_1_01\",\"shorts\":\"Costume_1_01\"}}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.items.outwear").value("Costume_1_01"))
                .andExpect(jsonPath("$.items.pants").value("Costume_1_01"))
                .andExpect(jsonPath("$.items.shorts").value("Costume_1_01"));
    }

    // ── 8-4 거절 ────────────────────────────────────────────────────────────

    @Test
    void 모르는_무기_유형은_400_이다() throws Exception {
        // Mace 는 구현 없는 예약 확장 지점이다 (S-GD-8DM55S) — 설정에 없으므로 저장할 수 없다.
        mockMvc.perform(putPreset(newAccountToken(), "Mace", "{\"items\":{" + FACE_FIELD + "}}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 모르는_슬롯은_400_이다() throws Exception {
        mockMvc.perform(putPreset(newAccountToken(), "Sword", "{\"items\":{\"cape\":\"" + DEFAULT_OUTWEAR + "\"}}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 카탈로그에_없는_아이템은_400_이다() throws Exception {
        // 인덱스를 보내는 구현으로 되돌아가면 여기서 걸린다 (D-BE-Z2HMR6: 순번 저장 금지).
        mockMvc.perform(putPreset(newAccountToken(), "Sword", "{\"items\":{" + FACE_FIELD + ",\"hat\":\"3\"}}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 소유하지_않은_아이템은_403_WARDROBE_ITEM_NOT_OWNED_다() throws Exception {
        // 전량 기본 지급이라 카탈로그에는 잠긴 옷이 없다(S-GD-ZVME43). 관문은 그대로 살아 있어야
        // 하므로 시험용으로 하나 넣어 확인한다.
        String lockedItem = insertLockedItem(200);
        try {
            // 얼굴을 함께 보낸다 — 얼굴 누락은 400 이라 소유 검사까지 가지 못한다(S-GD-5R953J).
            mockMvc.perform(putPreset(newAccountToken(), "Sword",
                            "{\"items\":{" + FACE_FIELD + ",\"hat\":\"" + lockedItem + "\"}}"))
                    .andExpect(status().isForbidden())
                    .andExpect(jsonPath("$.code").value("WARDROBE_ITEM_NOT_OWNED"));
        } finally {
            deleteItem(lockedItem);
        }
    }

    // ── 구매 (S-GD-5R953J · 지급 범위는 S-GD-ZVME43) ─────────────────────────
    //
    // ⚠️ 지금 카탈로그에는 살 것이 없다 — 전량 기본 지급이라 모두 이미 소유다(S-GD-ZVME43).
    //    그래도 구매 경로는 지워지지 않았고 지급 범위를 다시 줄이면 바로 쓰인다. 그래서 이 절은
    //    카탈로그의 특정 옷이 잠겨 있다고 가정하는 대신 시험용 잠금 아이템을 직접 넣어서 검증한다.

    @Test
    void 메달이_모자라면_409_INSUFFICIENT_MEDALS_고_소유가_늘지_않는다() throws Exception {
        // 신규 계정 잔액은 0 이다. 가장 싼 50 짜리도 살 수 없다.
        String token = newAccountToken();
        String lockedItem = insertLockedItem(50);
        try {
            mockMvc.perform(purchase(token, lockedItem))
                    .andExpect(status().isConflict())
                    .andExpect(jsonPath("$.code").value("INSUFFICIENT_MEDALS"));

            mockMvc.perform(get("/api/v1/accounts/me/wardrobe").header("Authorization", "Bearer " + token))
                    .andExpect(jsonPath("$.ownedItemIds").value(Matchers.not(Matchers.hasItem(lockedItem))));
        } finally {
            deleteItem(lockedItem);
        }
    }

    @Test
    void 구매는_메달을_빼고_소유를_늘린다() throws Exception {
        String token = newAccountToken();
        addMedals(token, 500);
        String lockedItem = insertLockedItem(50);
        try {
            mockMvc.perform(purchase(token, lockedItem))
                    .andExpect(status().isOk())
                    .andExpect(jsonPath("$.itemId").value(lockedItem))
                    .andExpect(jsonPath("$.medalBalance").value(450))
                    .andExpect(jsonPath("$.ownedItemIds").value(Matchers.hasItem(lockedItem)));
        } finally {
            deleteItem(lockedItem);
        }
    }

    @Test
    void 같은_옷을_두_번_사도_메달은_한_번만_빠진다() throws Exception {
        String token = newAccountToken();
        addMedals(token, 500);
        String lockedItem = insertLockedItem(50);
        try {
            mockMvc.perform(purchase(token, lockedItem)).andExpect(jsonPath("$.medalBalance").value(450));
            mockMvc.perform(purchase(token, lockedItem))
                    .andExpect(status().isOk())
                    .andExpect(jsonPath("$.medalBalance").value(450));
        } finally {
            deleteItem(lockedItem);
        }
    }

    @Test
    void 이미_가진_옷을_사도_메달은_빠지지_않는다() throws Exception {
        // 전량 지급 상태에서 화면이 실수로 구매를 보내도 잔액이 줄면 안 된다 (S-GD-ZVME43).
        // 소유를 명시적으로 확보해 둔다 — 지급 범위가 다시 줄어도 이 시험의 뜻은 그대로여야 한다.
        String token = newAccountToken();
        addMedals(token, 500);
        grantOwnership(token, "Glasses_05");

        mockMvc.perform(purchase(token, "Glasses_05"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.medalBalance").value(500));
    }

    @Test
    void 값이_0_인_의상은_살_수_없다() throws Exception {
        // 가격표는 남아 있고(S-GD-ZVME43) 0 은 여전히 "팔지 않는다"는 뜻이다.
        String token = newAccountToken();
        addMedals(token, 500);

        mockMvc.perform(purchase(token, DEFAULT_BODY))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 없는_아이템_구매는_400_이다() throws Exception {
        mockMvc.perform(purchase(newAccountToken(), "없는아이템"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 카탈로그_검사가_소유_검사보다_먼저다() throws Exception {
        // 화면이 "없는 아이템"과 "잠긴 아이템"을 다른 문구로 안내할 수 있어야 한다 (api.md 8-4).
        String lockedItem = insertLockedItem(200);
        try {
            mockMvc.perform(putPreset(newAccountToken(), "Sword",
                            "{\"items\":{\"hat\":\"" + lockedItem + "\",\"pants\":\"없는아이템\"}}"))
                    .andExpect(status().isBadRequest())
                    .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
        } finally {
            deleteItem(lockedItem);
        }
    }

    @Test
    void 토큰_없는_저장은_401_이고_아무것도_저장되지_않는다() throws Exception {
        mockMvc.perform(put("/api/v1/accounts/me/wardrobe/presets/{weaponType}", "Sword")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"items\":{" + FACE_FIELD + ",\"outwear\":\"" + DEFAULT_OUTWEAR + "\"}}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("TOKEN_INVALID"));
    }

    // ── 8-2 appearancePresetSeed 와의 관계 ──────────────────────────────────

    @Test
    void 프리셋_저장은_appearancePresetSeed_를_바꾸지_않는다() throws Exception {
        // seed 는 의미도 값도 그대로 두고 새 계약이 옆에 선다 (api.md 3-2 · 8-2, S-GD-X9T8WC).
        String email = uniqueEmail();
        String password = "password-1";
        mockMvc.perform(signup(email, password, uniqueNickname())).andExpect(status().isCreated());
        String token = loginToken(email, password);

        String before = me(token);
        mockMvc.perform(putPreset(token, "Sword", "{\"items\":{" + FACE_FIELD + ",\"outwear\":\"" + DEFAULT_OUTWEAR + "\"}}"))
                .andExpect(status().isOk());
        String after = me(token);

        Assertions.assertEquals(seed(before), seed(after),
                "프리셋 저장이 appearancePresetSeed 를 바꿨다");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private MockHttpServletRequestBuilder putPreset(String token, String weaponType, String body) {
        return put("/api/v1/accounts/me/wardrobe/presets/{weaponType}", weaponType)
                .header("Authorization", "Bearer " + token)
                .contentType(MediaType.APPLICATION_JSON)
                .content(body);
    }

    private MockHttpServletRequestBuilder signup(String loginId, String password, String nickname) {
        return post("/api/v1/auth/signup")
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"loginId\":\"" + loginId + "\",\"password\":\"" + password
                        + "\",\"nickname\":\"" + nickname + "\"}");
    }

    /**
     * 토큰이 가리키는 계정에 의상 소유를 직접 넣는다.
     *
     * <p>구매 흐름을 타지 않는 이유는 그쪽에 메달이 필요해서다 — 이 시험의 관심사가 아니다.
     *
     * <p>⚠️ <b>이미 소유 중이면 아무것도 하지 않는다.</b> 전량 기본 지급(S-GD-ZVME43)이라 대부분의
     * 의상은 가입 시점에 이미 들어와 있고, 그대로 INSERT 하면 PK 충돌로 죽는다.
     */
    private void grantOwnership(String token, String itemId) throws Exception {
        jdbc.update("INSERT INTO account_wardrobe_items (account_id, item_id, granted_at) "
                        + "SELECT ?, ?, NOW(6) FROM accounts a WHERE a.account_id = ? "
                        + "AND NOT EXISTS (SELECT 1 FROM account_wardrobe_items o "
                        + "WHERE o.account_id = ? AND o.item_id = ?)",
                accountIdOf(token), itemId, accountIdOf(token), accountIdOf(token), itemId);
    }

    /**
     * 시험용 <b>잠긴</b> 의상 한 벌을 카탈로그에 넣고 ID 를 돌려준다.
     *
     * <p>지급 범위가 전량이라(S-GD-ZVME43) 카탈로그의 어떤 옷도 "잠겼다" 고 가정할 수 없다.
     * 소유·구매 관문은 그대로 살아 있어야 하므로, 그 시험만 이 임시 행에 기댄다.
     * 이미 만들어진 계정에는 소급되지 않으니 <b>계정을 이 호출 뒤에 만들 필요는 없다</b> —
     * 가입 시점의 지급은 {@code granted_by_default = 1} 만 읽고 이 행은 0 이다.
     *
     * <p>⚠️ 반드시 {@link #deleteItem(String)} 로 지운다. 남기면 다른 시험의 카탈로그 개수가 어긋난다.
     */
    private String insertLockedItem(int priceMedals) {
        String itemId = "Locked_Test_" + UUID.randomUUID().toString().substring(0, 8);
        jdbc.update("INSERT INTO wardrobe_items (item_id, category, granted_by_default, price_medals) "
                + "VALUES (?, ?, 0, ?)", itemId, "OUTFIT", priceMedals);
        return itemId;
    }

    private void deleteItem(String itemId) {
        jdbc.update("DELETE FROM account_wardrobe_items WHERE item_id = ?", itemId);
        jdbc.update("DELETE FROM wardrobe_items WHERE item_id = ?", itemId);
    }

    /** 전량 지급이므로 소유 개수의 기대값은 카탈로그 개수다. 상수로 박으면 카탈로그가 늘 때 거짓 실패가 난다. */
    private long catalogSize() {
        Long count = jdbc.queryForObject("SELECT COUNT(*) FROM wardrobe_items", Long.class);
        return count == null ? 0L : count;
    }

    /** ID 오름차순의 첫 항목. 응답 정렬을 확인하는 데만 쓴다 (api.md 8-3). */
    private String firstCatalogItemId() {
        return jdbc.queryForObject("SELECT item_id FROM wardrobe_items ORDER BY item_id ASC LIMIT 1", String.class);
    }

    private MockHttpServletRequestBuilder purchase(String token, String itemId) {
        return post("/api/v1/accounts/me/wardrobe/purchases")
                .header("Authorization", "Bearer " + token)
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"itemId\":\"" + itemId + "\"}");
    }

    /** 구매 시험용 잔액. 매치 결과를 태우지 않고 바로 넣는다 — 이 시험의 관심사가 아니다. */
    private void addMedals(String token, long amount) throws Exception {
        jdbc.update("UPDATE accounts SET medal_balance = medal_balance + ? WHERE account_id = ?",
                amount, accountIdOf(token));
    }

    private String accountIdOf(String token) throws Exception {
        String body = mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCOUNT_ID.matcher(body);
        if (!matcher.find()) {
            throw new IllegalStateException("계정 응답에서 accountId 를 찾지 못했습니다: " + body);
        }
        return matcher.group(1);
    }

    private String newAccountToken() throws Exception {
        String email = uniqueEmail();
        String password = "password-1";
        mockMvc.perform(signup(email, password, uniqueNickname())).andExpect(status().isCreated());
        return loginToken(email, password);
    }

    private String loginToken(String loginId, String password) throws Exception {
        String body = mockMvc.perform(post("/api/v1/auth/login")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"loginId\":\"" + loginId + "\",\"password\":\"" + password + "\"}"))
                .andExpect(status().isOk())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCESS_TOKEN.matcher(body);
        Assertions.assertTrue(matcher.find(), "login 응답에 accessToken 이 없다: " + body);
        return matcher.group(1);
    }

    private String me(String token) throws Exception {
        return mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andReturn().getResponse().getContentAsString();
    }

    private static String seed(String body) {
        Matcher matcher = Pattern.compile("\"appearancePresetSeed\":(\\d+)").matcher(body);
        Assertions.assertTrue(matcher.find(), "응답에 appearancePresetSeed 가 없다: " + body);
        return matcher.group(1);
    }

    private static String uniqueEmail() {
        return "wardrobe-" + UUID.randomUUID().toString().substring(0, 8) + "@test.local";
    }

    private static String uniqueNickname() {
        return "wd-" + UUID.randomUUID().toString().substring(0, 8);
    }
}
