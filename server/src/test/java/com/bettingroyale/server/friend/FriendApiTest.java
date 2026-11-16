package com.bettingroyale.server.friend;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.List;
import java.util.Map;
import java.util.UUID;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.test.context.SpringBootTest;

import tools.jackson.databind.ObjectMapper;

/**
 * 친구 요청·관계의 실제 HTTP 검증 (docs/server/api.md 3-14~3-18, S15P21A606-202).
 *
 * <p>UserSettingsApiTest 와 같은 방식이다 — 임베디드 서버를 실제 포트로 띄우고 계정도 실제
 * signup/login 으로 만든다.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class FriendApiTest {

    private static final String REQUESTS = "/api/v1/friends/requests";
    private static final String FRIENDS = "/api/v1/friends";
    private static final String PASSWORD = UUID.randomUUID().toString();

    @Value("${local.server.port}")
    private int port;

    @Autowired
    private ObjectMapper mapper;

    private final HttpClient client = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .build();

    @Test
    void 요청을_보내고_수락하면_양쪽_친구_목록에_들어간다() {
        Account a = newAccount();
        Account b = newAccount();

        HttpResponse<String> sent = send(a.token(), b.nickname());
        assertEquals(201, sent.statusCode(), sent.body());
        assertEquals("PENDING", json(sent.body()).get("state"));

        List<Map<String, Object>> incoming = incoming(b.token());
        assertEquals(1, incoming.size());
        long requestId = ((Number) incoming.get(0).get("requestId")).longValue();

        HttpResponse<String> accepted = request(
                "POST", REQUESTS + "/" + requestId + "/accept", bearer(b), null);
        assertEquals(200, accepted.statusCode(), accepted.body());

        assertTrue(nicknamesOf(a.token()).contains(b.nickname()), "A 의 목록에 B 가 있어야 한다");
        assertTrue(nicknamesOf(b.token()).contains(a.nickname()), "B 의 목록에 A 가 있어야 한다");
    }

    /** api.md 3-15 — 둘 다 수락 대기로 두면 서로 상대를 기다리며 멈춘다. */
    @Test
    void 서로_보내면_수락_없이_바로_친구가_된다() {
        Account a = newAccount();
        Account b = newAccount();

        assertEquals(201, send(a.token(), b.nickname()).statusCode());
        HttpResponse<String> reciprocal = send(b.token(), a.nickname());

        assertEquals(200, reciprocal.statusCode(), reciprocal.body());
        assertEquals("ACCEPTED", json(reciprocal.body()).get("state"));
        assertTrue(nicknamesOf(a.token()).contains(b.nickname()));
        assertTrue(nicknamesOf(b.token()).contains(a.nickname()));
        assertTrue(incoming(a.token()).isEmpty(), "대기 요청이 남으면 안 된다");
        assertTrue(incoming(b.token()).isEmpty(), "대기 요청이 남으면 안 된다");
    }

    /** api.md 3-17 — 보낸 사람이 수락하면 404 다. 403 이면 그 id 가 존재한다고 알려주게 된다. */
    @Test
    void 보낸_사람은_자기_요청을_수락할_수_없고_404_다() {
        Account a = newAccount();
        Account b = newAccount();
        long requestId = ((Number) json(send(a.token(), b.nickname()).body()).get("requestId")).longValue();

        HttpResponse<String> response = request(
                "POST", REQUESTS + "/" + requestId + "/accept", bearer(a), null);

        assertEquals(404, response.statusCode(), response.body());
    }

    @Test
    void 자기_자신에게는_보낼_수_없다() {
        Account a = newAccount();
        assertEquals(400, send(a.token(), a.nickname()).statusCode());
    }

    @Test
    void 없는_닉네임이면_404_다() {
        Account a = newAccount();
        assertEquals(404, send(a.token(), "ghost" + UUID.randomUUID()).statusCode());
    }

    @Test
    void 같은_방향으로_두_번_보내면_409_다() {
        Account a = newAccount();
        Account b = newAccount();
        assertEquals(201, send(a.token(), b.nickname()).statusCode());
        assertEquals(409, send(a.token(), b.nickname()).statusCode());
    }

    @Test
    void 친구를_삭제하면_양쪽에서_사라진다() {
        Account a = newAccount();
        Account b = newAccount();
        send(a.token(), b.nickname());
        long requestId = ((Number) incoming(b.token()).get(0).get("requestId")).longValue();
        request("POST", REQUESTS + "/" + requestId + "/accept", bearer(b), null);

        HttpResponse<String> removed = request("DELETE", FRIENDS + "/" + b.accountId(), bearer(a), null);

        assertEquals(204, removed.statusCode(), removed.body());
        assertFalse(nicknamesOf(a.token()).contains(b.nickname()));
        assertFalse(nicknamesOf(b.token()).contains(a.nickname()), "대칭 관계라 양쪽에서 사라져야 한다");
    }

    /** api.md 3-14 — login_id 는 이메일이라 친구 응답에 실으면 이메일이 새어 나간다. */
    @Test
    void 친구_응답에_이메일이_들어가지_않는다() {
        Account a = newAccount();
        Account b = newAccount();
        send(a.token(), b.nickname());
        long requestId = ((Number) incoming(b.token()).get(0).get("requestId")).longValue();
        request("POST", REQUESTS + "/" + requestId + "/accept", bearer(b), null);

        String friends = request("GET", FRIENDS, bearer(a), null).body();
        String requests = request("GET", REQUESTS, bearer(a), null).body();

        assertFalse(friends.contains(b.loginId()), "친구 목록에 이메일이 있으면 안 된다: " + friends);
        assertFalse(friends.contains("loginId"), "loginId 필드 자체가 없어야 한다: " + friends);
        assertFalse(requests.contains("loginId"), "요청 목록에도 없어야 한다: " + requests);
    }

    @Test
    void 토큰이_없으면_401_이다() {
        assertEquals(401, request("GET", FRIENDS, null, null).statusCode());
        assertEquals(401, request("GET", REQUESTS, null, null).statusCode());
    }

    // ---------------------------------------------------------------- 헬퍼

    private record Account(String loginId, String accountId, String nickname, String token) {
    }

    private Account newAccount() {
        String suffix = UUID.randomUUID().toString().replace("-", "").substring(0, 10);
        String loginId = "friend-" + suffix + "@example.com";
        String nickname = "f" + suffix;
        String body = "{\"loginId\":\"%s\",\"password\":\"%s\",\"nickname\":\"%s\"}"
                .formatted(loginId, PASSWORD, nickname);
        HttpResponse<String> signup = request("POST", "/api/v1/auth/signup", null, body);
        assertEquals(201, signup.statusCode(), signup.body());

        HttpResponse<String> login = request("POST", "/api/v1/auth/login", null,
                "{\"loginId\":\"%s\",\"password\":\"%s\"}".formatted(loginId, PASSWORD));
        assertEquals(200, login.statusCode(), login.body());
        Map<String, Object> parsed = json(login.body());
        @SuppressWarnings("unchecked")
        Map<String, Object> account = (Map<String, Object>) parsed.get("account");
        return new Account(loginId, (String) account.get("accountId"), nickname,
                (String) parsed.get("accessToken"));
    }

    private HttpResponse<String> send(String token, String targetNickname) {
        return request("POST", REQUESTS, "Bearer " + token,
                "{\"targetNickname\":\"%s\"}".formatted(targetNickname));
    }

    @SuppressWarnings("unchecked")
    private List<Map<String, Object>> incoming(String token) {
        HttpResponse<String> response = request("GET", REQUESTS, "Bearer " + token, null);
        assertEquals(200, response.statusCode(), response.body());
        return (List<Map<String, Object>>) json(response.body()).get("incoming");
    }

    @SuppressWarnings("unchecked")
    private List<String> nicknamesOf(String token) {
        HttpResponse<String> response = request("GET", FRIENDS, "Bearer " + token, null);
        assertEquals(200, response.statusCode(), response.body());
        List<Map<String, Object>> friends = (List<Map<String, Object>>) json(response.body()).get("friends");
        return friends.stream().map(f -> (String) f.get("nickname")).toList();
    }

    private static String bearer(Account account) {
        return "Bearer " + account.token();
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> json(String body) {
        assertNotNull(body);
        return mapper.readValue(body, Map.class);
    }

    private HttpResponse<String> request(String method, String path, String authorization, String body) {
        HttpRequest.Builder builder = HttpRequest.newBuilder(URI.create("http://localhost:" + port + path))
                .timeout(Duration.ofSeconds(60))
                .header("Content-Type", "application/json; charset=utf-8")
                .header("Accept", "application/json");
        if (authorization != null) {
            builder.header("Authorization", authorization);
        }
        HttpRequest.BodyPublisher payload = body == null
                ? HttpRequest.BodyPublishers.noBody()
                : HttpRequest.BodyPublishers.ofString(body, StandardCharsets.UTF_8);
        try {
            return client.send(builder.method(method, payload).build(),
                    HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        } catch (IOException ex) {
            throw new IllegalStateException("HTTP 호출 실패: " + method + " " + path, ex);
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("HTTP 호출 중단: " + method + " " + path, ex);
        }
    }
}
