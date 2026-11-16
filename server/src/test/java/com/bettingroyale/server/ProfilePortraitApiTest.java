package com.bettingroyale.server;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.patch;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.sql.Timestamp;
import java.time.Instant;
import java.util.List;
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

import com.bettingroyale.server.account.ProfilePortraitService;
import com.bettingroyale.server.account.ProfilePortraits;

/**
 * docs/server/api.md 3-8(`ownedPortraitIds`)·3-9(초상화 변경)·3-10(기본 지급) 계약 검증.
 *
 * <p>근거 스펙 S-UX-RQ5N9E(프로필 초상화 — 문자열 ID·소유·기본 지급·서버 검증),
 * 근거 결정 D-BE-SG4AD6(레거시 D-083 — 배정 PK 엔티티는 {@code save()} 가 아니라 명시적 INSERT).
 *
 * <p>⚠️ 현재 5장이 전원 기본 지급이라(S-UX-RQ5N9E) API 만으로는 "소유하지 않은 초상화"를 만들 수 없다.
 * 그래서 소유 관문 테스트는 소유 행 하나를 직접 지운다 — 지급 범위가 넓다는 이유로 소유 검사가
 * 없는 것처럼 통과해서는 안 되기 때문이다(옷장 테스트가 잠긴 아이템을 심는 것과 같은 이유).
 */
@SpringBootTest
@AutoConfigureMockMvc
class ProfilePortraitApiTest {

    private static final Pattern ACCESS_TOKEN = Pattern.compile("\"accessToken\":\"([^\"]+)\"");
    private static final Pattern ACCOUNT_ID = Pattern.compile("\"accountId\":\"([^\"]+)\"");

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private JdbcTemplate jdbc;

    /**
     * 가입 지급의 멱등성을 서비스 경계에서 직접 확인한다. API 로는 가입을 두 번 할 수 없으므로
     * (같은 계정이 두 번 생기지 않는다) 재호출을 만들 다른 방법이 없다.
     */
    @Autowired
    private ProfilePortraitService portraits;

    // ── 3-8 ownedPortraitIds ────────────────────────────────────────────────

    @Test
    void 가입_직후_소유_목록은_기본_지급_목록과_같다() throws Exception {
        String token = newAccountToken();

        mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                // 순서는 카탈로그 순서다 — 화면이 매번 같은 차례로 그린다.
                .andExpect(jsonPath("$.ownedPortraitIds").value(
                        Matchers.contains(ProfilePortraits.GRANTED_ON_SIGNUP.toArray())))
                // 기본값은 모든 신규 계정이 같다. 무작위로 주지 않는다 (S-UX-RQ5N9E).
                .andExpect(jsonPath("$.profilePortraitId").value(ProfilePortraits.DEFAULT));
    }

    @Test
    void 가입_지급은_멱등하다_재호출이_행을_늘리지도_덮어쓰지도_않는다() {
        // D-BE-SG4AD6(레거시 D-083) 회귀 방어 — 두 가지를 동시에 본다.
        //  (1) 행 수: NOT EXISTS 를 빼면 PK 제약 위반으로 터진다.
        //  (2) granted_at: save() 로 되돌아가면 merge 가 기존 행을 찾아 UPDATE 로 "성공"한다.
        //      행 수만 세면 그 조용한 UPDATE 를 놓친다 — 메달 이중 지급이 바로 그 침묵이었다.
        String accountId = newAccountId();
        int granted = ProfilePortraits.GRANTED_ON_SIGNUP.size();
        Assertions.assertEquals(granted, portraitRowCount(accountId), "가입 지급이 카탈로그 전량이 아니다");

        // 재호출이 행을 건드렸는지 눈에 보이게 하려고 지급 시각을 과거의 고정값으로 밀어 둔다.
        Timestamp marker = Timestamp.from(Instant.parse("2020-01-02T03:04:05Z"));
        jdbc.update("UPDATE account_portraits SET granted_at = ? WHERE account_id = ?", marker, accountId);

        portraits.grantSignupPortraits(accountId);
        portraits.grantSignupPortraits(accountId);

        Assertions.assertEquals(granted, portraitRowCount(accountId),
                "재지급이 소유 행을 늘렸다 — 가입 지급이 멱등하지 않다");
        Assertions.assertEquals(ProfilePortraits.GRANTED_ON_SIGNUP, portraits.ownedPortraitIds(accountId));
        Assertions.assertEquals(List.of(marker), jdbc.queryForList(
                        "SELECT DISTINCT granted_at FROM account_portraits WHERE account_id = ?",
                        Timestamp.class, accountId),
                "재지급이 기존 행을 UPDATE 로 덮어썼다 — save()/merge 경로로 되돌아갔다(D-BE-SG4AD6)");
    }

    @Test
    void 없는_계정에_지급해도_행을_만들지_않는다() {
        // 계정 행에서 SELECT 하므로 0행이다 — FK 위반으로 터지지 않는다.
        String ghost = "acc_ghost_" + UUID.randomUUID().toString().substring(0, 8);

        portraits.grantSignupPortraits(ghost);

        Assertions.assertEquals(0, portraitRowCount(ghost));
    }

    // ── 3-9 변경 성공 ────────────────────────────────────────────────────────

    @Test
    void 소유한_초상화로_바꾸면_200_이고_값이_바뀐다() throws Exception {
        String token = newAccountToken();

        mockMvc.perform(changePortrait(token, "hood_bear"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.profilePortraitId").value("hood_bear"));

        // 기기에만 남기지 않는다 — 다시 조회해도 같은 값이어야 한다 (S-UX-RQ5N9E).
        mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.profilePortraitId").value("hood_bear"));
    }

    @Test
    void 같은_값으로_다시_바꿔도_200_이다() throws Exception {
        // 저장 버튼을 여러 번 눌러도 안전하다 (api.md 3-9).
        String token = newAccountToken();

        mockMvc.perform(changePortrait(token, "hood_cat")).andExpect(status().isOk());
        mockMvc.perform(changePortrait(token, "hood_cat"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.profilePortraitId").value("hood_cat"));
    }

    @Test
    void 초상화_변경은_인게임_외형_seed_를_바꾸지_않는다() throws Exception {
        // 프로필과 인게임 외형은 다른 기능이다 (S-UX-RQ5N9E, api.md 3-8).
        String token = newAccountToken();
        String before = me(token);

        mockMvc.perform(changePortrait(token, "hood_rabbit")).andExpect(status().isOk());

        Assertions.assertEquals(seed(before), seed(me(token)),
                "초상화 변경이 appearancePresetSeed 를 바꿨다");
    }

    // ── 3-9 거절 ─────────────────────────────────────────────────────────────

    @Test
    void 소유하지_않은_초상화는_403_PORTRAIT_NOT_OWNED_다() throws Exception {
        String email = uniqueEmail();
        String password = "password-1";
        String accountId = signupAccountId(email, password);
        String token = loginToken(email, password);

        // 소유 관문을 실제로 시험하려면 소유하지 않은 상태를 만들어야 한다.
        jdbc.update("DELETE FROM account_portraits WHERE account_id = ? AND portrait_id = ?",
                accountId, "hood_rabbit");

        mockMvc.perform(changePortrait(token, "hood_rabbit"))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("PORTRAIT_NOT_OWNED"));

        // 거절된 요청이 값을 바꾸지 않았어야 한다.
        mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + token))
                .andExpect(jsonPath("$.profilePortraitId").value(ProfilePortraits.DEFAULT))
                .andExpect(jsonPath("$.ownedPortraitIds").value(
                        Matchers.not(Matchers.hasItem("hood_rabbit"))));
    }

    @Test
    void 카탈로그에_없는_초상화는_400_이다() throws Exception {
        mockMvc.perform(changePortrait(newAccountToken(), "hood_dragon"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 순번을_보내면_400_이다() throws Exception {
        // 배열 순번을 저장하는 구현으로 되돌아가면 여기서 걸린다 (S-UX-RQ5N9E: 순번 금지).
        mockMvc.perform(changePortrait(newAccountToken(), "3"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 빈_값과_16자_초과는_400_이다() throws Exception {
        String token = newAccountToken();

        mockMvc.perform(changePortrait(token, ""))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
        mockMvc.perform(changePortrait(token, "x".repeat(ProfilePortraits.MAX_ID_LENGTH + 1)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 토큰_없음과_망가진_토큰은_401_TOKEN_INVALID_다() throws Exception {
        mockMvc.perform(patch("/api/v1/accounts/me")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"profilePortraitId\":\"hood_bear\"}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("TOKEN_INVALID"));

        mockMvc.perform(changePortrait("not-a-jwt", "hood_bear"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("TOKEN_INVALID"));
    }

    @Test
    void 무효화된_토큰의_변경은_401_SESSION_REPLACED_이고_저장되지_않는다() throws Exception {
        String email = uniqueEmail();
        String password = "password-1";
        signupAccountId(email, password);
        String firstToken = loginToken(email, password);
        String secondToken = loginToken(email, password);

        mockMvc.perform(changePortrait(firstToken, "hood_bear"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("SESSION_REPLACED"));

        mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + secondToken))
                .andExpect(jsonPath("$.profilePortraitId").value(ProfilePortraits.DEFAULT));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private MockHttpServletRequestBuilder changePortrait(String token, String portraitId) {
        return patch("/api/v1/accounts/me")
                .header("Authorization", "Bearer " + token)
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"profilePortraitId\":\"" + portraitId + "\"}");
    }

    private int portraitRowCount(String accountId) {
        Integer count = jdbc.queryForObject(
                "SELECT COUNT(*) FROM account_portraits WHERE account_id = ?", Integer.class, accountId);
        return count == null ? 0 : count;
    }

    private MockHttpServletRequestBuilder signup(String loginId, String password, String nickname) {
        return post("/api/v1/auth/signup")
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"loginId\":\"" + loginId + "\",\"password\":\"" + password
                        + "\",\"nickname\":\"" + nickname + "\"}");
    }

    private String signupAccountId(String loginId, String password) throws Exception {
        String body = mockMvc.perform(signup(loginId, password, uniqueNickname()))
                .andExpect(status().isCreated())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCOUNT_ID.matcher(body);
        Assertions.assertTrue(matcher.find(), "signup 응답에 accountId 가 없다: " + body);
        return matcher.group(1);
    }

    private String newAccountId() {
        try {
            return signupAccountId(uniqueEmail(), "password-1");
        } catch (Exception ex) {
            throw new IllegalStateException("가입에 실패했다", ex);
        }
    }

    private String newAccountToken() throws Exception {
        String email = uniqueEmail();
        String password = "password-1";
        signupAccountId(email, password);
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
        Matcher matcher = Pattern.compile("\"appearancePresetSeed\":(-?\\d+)").matcher(body);
        Assertions.assertTrue(matcher.find(), "응답에 appearancePresetSeed 가 없다: " + body);
        return matcher.group(1);
    }

    private static String uniqueEmail() {
        return "portrait-" + UUID.randomUUID().toString().substring(0, 8) + "@test.local";
    }

    private static String uniqueNickname() {
        return "pt-" + UUID.randomUUID().toString().substring(0, 8);
    }
}
