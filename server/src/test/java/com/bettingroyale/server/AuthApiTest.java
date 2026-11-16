package com.bettingroyale.server;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.util.UUID;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import org.junit.jupiter.api.Assertions;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

/**
 * docs/server/api.md 3-6(signup)·3-7(login)·3-8(me) 계약 검증.
 * 임시 파라미터(형식 제약·JWT)는 S-BE-JNTJTE, 입력·중복·동시 로그인 정책은 S-BE-VGE4WD.
 */
@SpringBootTest
@AutoConfigureMockMvc
class AuthApiTest {

    private static final Pattern ACCOUNT_ID = Pattern.compile("\"accountId\":\"([^\"]+)\"");
    private static final Pattern ACCESS_TOKEN = Pattern.compile("\"accessToken\":\"([^\"]+)\"");
    private static final Pattern APPEARANCE_PRESET_SEED = Pattern.compile("\"appearancePresetSeed\":(\\d+)");

    @Autowired
    private MockMvc mockMvc;

    // ── 3-6 signup ──────────────────────────────────────────────────────────

    @Test
    void 가입은_201_로_계정을_돌려주고_토큰을_발급하지_않는다() throws Exception {
        String email = uniqueEmail();
        String body = mockMvc.perform(signup(email, "password-1", uniqueNickname()))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.medalBalance").value(0))
                .andExpect(jsonPath("$.appearancePresetSeed").isNumber())
                .andExpect(jsonPath("$.accessToken").doesNotExist())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCOUNT_ID.matcher(body);
        Assertions.assertTrue(matcher.find(), "signup 응답에 accountId 가 없다: " + body);
        Assertions.assertTrue(matcher.group(1).length() <= 32, "accountId 는 32자 이내여야 한다 (api.md 3-2)");
        Assertions.assertTrue(
                appearancePresetSeed(body) > 0,
                "신규 계정의 appearancePresetSeed 는 양수여야 한다: " + body);
    }

    @Test
    void 같은_이메일_재가입은_409_DUPLICATE_LOGIN_ID_다() throws Exception {
        String email = uniqueEmail();
        mockMvc.perform(signup(email, "password-1", uniqueNickname()))
                .andExpect(status().isCreated());
        mockMvc.perform(signup(email, "password-2", uniqueNickname()))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("DUPLICATE_LOGIN_ID"));
    }

    @Test
    void 이메일은_대소문자만_달라도_같은_아이디다() throws Exception {
        String email = uniqueEmail();
        mockMvc.perform(signup(email, "password-1", uniqueNickname()))
                .andExpect(status().isCreated());
        mockMvc.perform(signup(email.toUpperCase(), "password-2", uniqueNickname()))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("DUPLICATE_LOGIN_ID"));
    }

    @Test
    void 같은_닉네임_재가입은_409_DUPLICATE_NICKNAME_다() throws Exception {
        String nickname = uniqueNickname();
        mockMvc.perform(signup(uniqueEmail(), "password-1", nickname))
                .andExpect(status().isCreated());
        mockMvc.perform(signup(uniqueEmail(), "password-2", nickname))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("DUPLICATE_NICKNAME"));
    }

    @Test
    void 이메일_형식이_아니면_400_이다() throws Exception {
        mockMvc.perform(signup("not-an-email", "password-1", uniqueNickname()))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 비밀번호가_8자_미만이면_400_이다() throws Exception {
        mockMvc.perform(signup(uniqueEmail(), "short-7", uniqueNickname()))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 닉네임은_16자까지_되고_17자부터_400_이다() throws Exception {
        // 한글도 문자 수 기준 16자 — 클라 NetworkString<_16> 실측과 동일 (api.md 3-6).
        mockMvc.perform(signup(uniqueEmail(), "password-1", "가나다라마바사아자차카타파하호루"))
                .andExpect(status().isCreated());
        mockMvc.perform(signup(uniqueEmail(), "password-1", "가나다라마바사아자차카타파하호루가"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    // ── 3-7 login ───────────────────────────────────────────────────────────

    @Test
    void 로그인은_토큰과_계정을_돌려준다() throws Exception {
        String email = uniqueEmail();
        String nickname = uniqueNickname();
        String signupBody = mockMvc.perform(signup(email, "password-1", nickname))
                .andExpect(status().isCreated())
                .andReturn().getResponse().getContentAsString();
        int appearancePresetSeed = appearancePresetSeed(signupBody);

        mockMvc.perform(login(email, "password-1"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.tokenType").value("Bearer"))
                .andExpect(jsonPath("$.expiresInSeconds").isNumber())
                .andExpect(jsonPath("$.account.nickname").value(nickname))
                .andExpect(jsonPath("$.account.medalBalance").value(0))
                .andExpect(jsonPath("$.account.appearancePresetSeed").value(appearancePresetSeed));
    }

    @Test
    void 잘못된_비밀번호와_없는_아이디는_같은_401_INVALID_CREDENTIALS_다() throws Exception {
        String email = uniqueEmail();
        mockMvc.perform(signup(email, "password-1", uniqueNickname()))
                .andExpect(status().isCreated());

        mockMvc.perform(login(email, "wrong-password"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("INVALID_CREDENTIALS"));
        mockMvc.perform(login(uniqueEmail(), "password-1"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("INVALID_CREDENTIALS"));
    }

    // ── 3-8 me + 동시 로그인 차단 ─────────────────────────────────────────────

    @Test
    void me_는_토큰의_계정을_돌려준다() throws Exception {
        String email = uniqueEmail();
        String nickname = uniqueNickname();
        mockMvc.perform(signup(email, "password-1", nickname))
                .andExpect(status().isCreated());
        String token = loginToken(email, "password-1");

        mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.nickname").value(nickname));
    }

    @Test
    void 헤더가_없거나_망가진_토큰은_401_TOKEN_INVALID_다() throws Exception {
        mockMvc.perform(get("/api/v1/accounts/me"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("TOKEN_INVALID"));
        mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer not-a-jwt"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("TOKEN_INVALID"));
    }

    @Test
    void 다른_곳에서_다시_로그인하면_이전_토큰은_401_SESSION_REPLACED_다() throws Exception {
        String email = uniqueEmail();
        mockMvc.perform(signup(email, "password-1", uniqueNickname()))
                .andExpect(status().isCreated());

        String firstToken = loginToken(email, "password-1");
        String secondToken = loginToken(email, "password-1");

        // 나중 로그인이 이긴다 (S-BE-VGE4WD, 방식은 S-BE-JNTJTE ⚠️추정).
        mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + firstToken))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("SESSION_REPLACED"));
        mockMvc.perform(get("/api/v1/accounts/me").header("Authorization", "Bearer " + secondToken))
                .andExpect(status().isOk());
    }

    // ── 가입 계정과 기존 계약(3-3·3-4)의 연결 ────────────────────────────────

    @Test
    void 가입_계정도_결과_제출과_잔액_조회에_그대로_쓰인다() throws Exception {
        String email = uniqueEmail();
        String nickname = uniqueNickname();
        String body = mockMvc.perform(signup(email, "password-1", nickname))
                .andExpect(status().isCreated())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCOUNT_ID.matcher(body);
        Assertions.assertTrue(matcher.find());
        String accountId = matcher.group(1);
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"submitterAccountId\":\"" + accountId + "\",\"results\":[{\"accountId\":\""
                                + accountId + "\",\"rank\":1}]}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.receipts[0].nickname").value(nickname));

        mockMvc.perform(get("/api/v1/accounts/{id}/balance", accountId))
                .andExpect(status().isOk());
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder signup(
            String loginId, String password, String nickname) {
        return post("/api/v1/auth/signup")
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"loginId\":\"" + loginId + "\",\"password\":\"" + password
                        + "\",\"nickname\":\"" + nickname + "\"}");
    }

    private org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder login(
            String loginId, String password) {
        return post("/api/v1/auth/login")
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"loginId\":\"" + loginId + "\",\"password\":\"" + password + "\"}");
    }

    private String loginToken(String loginId, String password) throws Exception {
        String body = mockMvc.perform(login(loginId, password))
                .andExpect(status().isOk())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCESS_TOKEN.matcher(body);
        Assertions.assertTrue(matcher.find(), "login 응답에 accessToken 이 없다: " + body);
        return matcher.group(1);
    }

    private static int appearancePresetSeed(String body) {
        Matcher matcher = APPEARANCE_PRESET_SEED.matcher(body);
        Assertions.assertTrue(matcher.find(), "응답에 appearancePresetSeed 가 없다: " + body);
        return Integer.parseInt(matcher.group(1));
    }

    private static String uniqueEmail() {
        return "user-" + UUID.randomUUID().toString().substring(0, 8) + "@test.local";
    }

    private static String uniqueNickname() {
        return "nick-" + UUID.randomUUID().toString().substring(0, 8);
    }
}
