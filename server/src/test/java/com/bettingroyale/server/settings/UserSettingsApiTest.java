package com.bettingroyale.server.settings;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.test.context.SpringBootTest;

import tools.jackson.databind.ObjectMapper;

/**
 * 계정 설정 동기화의 실제 HTTP 검증 (docs/server/api.md 7절, S-UX-8FGK29).
 *
 * <p>모의 클라우드를 쓰지 않는다 — 임베디드 서버를 실제 포트로 띄우고 JDK HttpClient 로 호출한다.
 * 계정은 실제 signup/login 으로 만들고 토큰도 실제 발급분을 쓴다.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class UserSettingsApiTest {

    private static final String SETTINGS_PATH = "/api/v1/accounts/me/settings";
    private static final String PASSWORD = UUID.randomUUID().toString();

    @Value("${local.server.port}")
    private int port;

    @Autowired
    private ObjectMapper mapper;

    @Autowired
    private UserSettingsRepository repository;

    @Autowired
    private UserSettingsStore settingsStore;

    private final HttpClient client = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .build();

    @Test
    void preservesClearedBindingAndAutomaticDisplayValue() {
        Account account = newAccount();
        String body = document(nowMillis(), "device-defaults", "0.8")
                .replace("<Mouse>/leftButton", "")
                .replace("\"graphics\": { \"values\": [] }",
                        "\"graphics\": { \"values\": [{\"id\":\"graphics.resolution\",\"value\":\"\"}] }");
        HttpResponse<String> saved = put(account.token(), body);
        assertEquals(200, saved.statusCode(), saved.body());
        HttpResponse<String> loaded = get(account.token());
        assertEquals(200, loaded.statusCode());
        assertEquals(saved.body(), loaded.body());
        assertTrue(loaded.body().contains("\"path\":\"\""));
        assertTrue(loaded.body().contains("\"value\":\"\""));
    }

    // ---------------------------------------------------------------- 인증

    @Test
    void rejectsRequestsWithoutToken() {
        HttpResponse<String> get = request("GET", SETTINGS_PATH, null, null);
        assertEquals(401, get.statusCode(), get.body());
        assertEquals("TOKEN_INVALID", code(get));

        HttpResponse<String> put = request("PUT", SETTINGS_PATH, null, document(nowMillis(), "device-a", "0.8"));
        assertEquals(401, put.statusCode(), put.body());
        assertEquals("TOKEN_INVALID", code(put));
    }

    @Test
    void rejectsMalformedOrForgedToken() {
        assertEquals(401, request("GET", SETTINGS_PATH, "Basic abcdef", null).statusCode());
        assertEquals("TOKEN_INVALID", code(request("GET", SETTINGS_PATH, "Basic abcdef", null)));

        HttpResponse<String> forged = request("GET", SETTINGS_PATH, "Bearer not-a-real-token", null);
        assertEquals(401, forged.statusCode(), forged.body());
        assertEquals("TOKEN_INVALID", code(forged));
    }

    @Test
    void rejectsReplacedSession() {
        Account account = newAccount();
        assertEquals(204, get(account.token()).statusCode());

        Account relogin = login(account.loginId());
        assertNotEquals(account.token(), relogin.token());

        HttpResponse<String> stale = get(account.token());
        assertEquals(401, stale.statusCode(), stale.body());
        assertEquals("SESSION_REPLACED", code(stale));

        HttpResponse<String> staleWrite = put(account.token(), document(nowMillis(), "device-a", "0.8"));
        assertEquals(401, staleWrite.statusCode(), staleWrite.body());
        assertEquals("SESSION_REPLACED", code(staleWrite));

        // 새 토큰은 같은 계정의 설정에 정상 접근한다.
        assertEquals(204, get(relogin.token()).statusCode());
    }

    // ---------------------------------------------------------------- 저장·조회

    @Test
    void getBeforeFirstWriteIsNoContent() {
        Account account = newAccount();
        HttpResponse<String> response = get(account.token());
        assertEquals(204, response.statusCode());
        assertTrue(response.body() == null || response.body().isEmpty(), "204 응답에는 본문이 없다");
    }

    @Test
    void storesCanonicalDocumentAndPreservesUnknownFields() {
        Account account = newAccount();
        String updatedAt = nowMillis();
        String body = """
                {
                  "version": 1,
                  "gameplay": { "values": [ { "id": "gameplay.damageNumbers", "value": "true" } ] },
                  "hud": {
                    "values": [ { "id": "hud.scale", "value": "0.8", "clientOnlyHint": "preview" } ],
                    "bindings": [],
                    "futureSection": { "nested": [ "keep", "me" ] }
                  },
                  "keyboardMouse": { "bindings": [ { "actionId": "attack", "path": "<Mouse>/leftButton" } ] },
                  "somethingNewerClientKnows": { "values": [ { "id": "x.y", "value": "1" } ] },
                  "metadata": { "settingsVersion": 1, "updatedAt": "%s", "deviceId": "device-a", "clientBuild": "1.2.3" }
                }
                """.formatted(updatedAt);

        HttpResponse<String> stored = put(account.token(), body);
        assertEquals(200, stored.statusCode(), stored.body());
        assertEquals("0.8", hudScale(stored.body()));

        // 미지 category/option 필드가 남아 있어야 한다.
        Map<String, Object> document = json(stored.body());
        assertTrue(document.containsKey("somethingNewerClientKnows"), "미지 category 가 보존되어야 한다");
        assertEquals("preview", object(firstValue(document, "hud")).get("clientOnlyHint"));
        assertTrue(object(document.get("hud")).containsKey("futureSection"), "미지 하위 객체가 보존되어야 한다");
        assertEquals("1.2.3", object(document.get("metadata")).get("clientBuild"));

        // 저장 후 GET 은 200 과 바이트가 같은 정본이다.
        HttpResponse<String> fetched = get(account.token());
        assertEquals(200, fetched.statusCode());
        assertEquals(stored.body(), fetched.body());

        // 메모리가 아니라 설정 전용 테이블에 들어 있다.
        Optional<UserSettingsEntity> row = repository.findById(account.accountId());
        assertTrue(row.isPresent(), "설정 행이 저장소에 있어야 한다");
        assertEquals(stored.body(), row.get().document());
        assertEquals("device-a", row.get().deviceId());
        assertEquals(1, row.get().envelopeVersion());
    }

    @Test
    void settingsAreIsolatedPerAccount() {
        Account first = newAccount();
        Account second = newAccount();
        String updatedAt = nowMillis();

        assertEquals(200, put(first.token(), document(updatedAt, "device-a", "0.8")).statusCode());

        // 다른 계정은 아직 저장 전이다 — 남의 문서가 보이면 안 된다.
        assertEquals(204, get(second.token()).statusCode());

        assertEquals(200, put(second.token(), document(updatedAt, "device-b", "1.3")).statusCode());
        assertEquals("0.8", hudScale(get(first.token()).body()));
        assertEquals("1.3", hudScale(get(second.token()).body()));
    }

    @Test
    void accountIsResolvedFromTokenOnly() {
        Account victim = newAccount();
        Account attacker = newAccount();

        // 본문이 남의 계정 ID 를 말해도 권위가 없다 — 저장은 토큰의 계정에만 일어난다.
        String body = """
                {
                  "version": 1,
                  "account": { "values": [ { "id": "accountId", "value": "%s" } ] },
                  "hud": { "values": [ { "id": "hud.scale", "value": "1.1" } ] },
                  "metadata": { "settingsVersion": 1, "updatedAt": "%s", "deviceId": "device-attacker" }
                }
                """.formatted(victim.accountId(), nowMillis());

        assertEquals(200, put(attacker.token(), body).statusCode());
        assertEquals(204, get(victim.token()).statusCode(), "피해 계정의 설정은 생기지 않아야 한다");
        assertTrue(repository.findById(victim.accountId()).isEmpty());
        assertEquals("1.1", hudScale(get(attacker.token()).body()));
    }

    // ---------------------------------------------------------------- LWW

    @Test
    void newerWriteWinsAndStaleWriteIsIgnored() {
        Account account = newAccount();
        Instant base = Instant.now().truncatedTo(ChronoUnit.MILLIS);
        String older = base.minusSeconds(60).toString();
        String newer = base.toString();

        assertEquals(200, put(account.token(), document(newer, "device-a", "1.2")).statusCode());

        HttpResponse<String> stale = put(account.token(), document(older, "device-b", "0.7"));
        assertEquals(200, stale.statusCode(), stale.body());
        assertEquals("1.2", hudScale(stale.body()), "오래된 쓰기는 최신 정본을 돌려받는다");
        assertEquals("1.2", hudScale(get(account.token()).body()));
        assertEquals("device-a", deviceId(get(account.token()).body()));

        HttpResponse<String> fresh = put(account.token(), document(base.plusSeconds(1).toString(), "device-b", "0.7"));
        assertEquals(200, fresh.statusCode(), fresh.body());
        assertEquals("0.7", hudScale(fresh.body()));
        assertEquals("device-b", deviceId(get(account.token()).body()));
    }

    @Test
    void sameTimestampIsBrokenByDeviceIdOrder() {
        Account account = newAccount();
        String updatedAt = nowMillis();

        assertEquals(200, put(account.token(), document(updatedAt, "device-m", "1.0")).statusCode());

        // 정렬이 앞서는 deviceId 는 진다 — 기존 정본을 그대로 돌려받는다.
        HttpResponse<String> loser = put(account.token(), document(updatedAt, "device-a", "0.7"));
        assertEquals(200, loser.statusCode());
        assertEquals("1.0", hudScale(loser.body()));
        assertEquals("device-m", deviceId(loser.body()));

        // 정렬이 뒤서는 deviceId 는 이긴다.
        HttpResponse<String> winner = put(account.token(), document(updatedAt, "device-z", "1.3"));
        assertEquals(200, winner.statusCode());
        assertEquals("1.3", hudScale(winner.body()));
        assertEquals("device-z", deviceId(get(account.token()).body()));
    }

    @Test
    void duplicateWriteKeepsStoredBody() {
        Account account = newAccount();
        String updatedAt = nowMillis();

        HttpResponse<String> first = put(account.token(), document(updatedAt, "device-a", "0.8"));
        assertEquals(200, first.statusCode());

        // 같은 (updatedAt, deviceId) 는 동일 저장의 재시도다 — 본문이 달라도 기존 정본을 유지한다.
        HttpResponse<String> retry = put(account.token(), document(updatedAt, "device-a", "1.25"));
        assertEquals(200, retry.statusCode());
        assertEquals(first.body(), retry.body());
        assertEquals("0.8", hudScale(get(account.token()).body()));
    }

    @Test
    void canonicalTimestampMatchesMillisecondStorageAndDeviceTieBreak() {
        Account account = newAccount();
        Instant second = Instant.now().minusSeconds(1).truncatedTo(ChronoUnit.SECONDS);
        Instant canonicalTime = second.plusMillis(123);

        HttpResponse<String> first = put(account.token(),
                document(second.plusNanos(123_900_000).toString(), "device-a", "0.8"));
        assertEquals(200, first.statusCode(), first.body());
        assertEquals(canonicalTime.toString(), object(json(first.body()).get("metadata")).get("updatedAt"));
        assertEquals(canonicalTime, repository.findById(account.accountId()).orElseThrow().updatedAt());

        // 같은 밀리초 안에서는 원문 나노초보다 DeviceId 순서가 우선한다.
        HttpResponse<String> winner = put(account.token(),
                document(second.plusNanos(123_100_000).toString(), "device-z", "1.3"));
        assertEquals(200, winner.statusCode(), winner.body());
        assertEquals("device-z", deviceId(winner.body()));
        assertEquals("1.3", hudScale(winner.body()));
        assertEquals(canonicalTime.toString(), object(json(winner.body()).get("metadata")).get("updatedAt"));
        assertEquals(winner.body(), get(account.token()).body());

        HttpResponse<String> retry = put(account.token(),
                document(second.plusNanos(123_999_999).toString(), "device-z", "0.7"));
        assertEquals(200, retry.statusCode(), retry.body());
        assertEquals(winner.body(), retry.body(), "절삭 후 같은 시각/기기의 재시도는 기존 정본을 유지한다");
    }

    @Test
    void canonicalTimestampIncludesZeroMilliseconds() {
        Account account = newAccount();
        String wholeSecond = Instant.now().minusSeconds(1).truncatedTo(ChronoUnit.SECONDS).toString();

        HttpResponse<String> stored = put(account.token(), document(wholeSecond, "device-a", "0.8"));
        assertEquals(200, stored.statusCode(), stored.body());
        assertEquals(wholeSecond.replace("Z", ".000Z"), object(json(stored.body()).get("metadata")).get("updatedAt"));
    }

    @Test
    void olderClientCannotOverwriteStoredFutureSchemaWithNewerTimestamp() {
        Account account = newAccount();
        Instant futureStoredAt = Instant.now().minusSeconds(1).truncatedTo(ChronoUnit.MILLIS);
        String futureBody = document(futureStoredAt.toString(), "device-future", "0.8")
                .replace("\"version\": 1", "\"version\": 2, \"futureOnly\": {\"keep\": true}");
        // 새 서버 버전이 저장한 뒤 구버전 서버/클라이언트가 실행되는 상황을 만든다.
        settingsStore.write(account.accountId(), new ValidatedSettings(futureBody, 2, 1,
                futureStoredAt, "device-future"));

        HttpResponse<String> fetched = get(account.token());
        assertEquals(200, fetched.statusCode(), fetched.body());
        assertEquals(futureBody, fetched.body());
        HttpResponse<String> lowerSchema = put(account.token(),
                document(futureStoredAt.plusSeconds(1).toString(), "device-z", "1.3"));
        assertEquals(200, lowerSchema.statusCode(), lowerSchema.body());
        assertEquals(futureBody, lowerSchema.body());
        assertEquals(futureBody, get(account.token()).body());
        assertEquals(2, repository.findById(account.accountId()).orElseThrow().envelopeVersion());
    }

    @Test
    void concurrentFirstWritesAreSerializedAndDeterministic() throws Exception {
        Account account = newAccount();
        String updatedAt = nowMillis();
        int writers = 4;

        ExecutorService pool = Executors.newFixedThreadPool(writers);
        CountDownLatch start = new CountDownLatch(1);
        List<Future<HttpResponse<String>>> futures = new ArrayList<>();
        try {
            for (int i = 1; i <= writers; i++) {
                String deviceId = "device-" + i;
                String scale = "0." + i;
                futures.add(pool.submit(() -> {
                    start.await(10, TimeUnit.SECONDS);
                    return put(account.token(), document(updatedAt, deviceId, scale));
                }));
            }
            start.countDown();
            for (Future<HttpResponse<String>> future : futures) {
                HttpResponse<String> response = future.get(60, TimeUnit.SECONDS);
                assertEquals(200, response.statusCode(), "동시 최초 쓰기는 충돌 없이 직렬화된다: " + response.body());
            }
        } finally {
            pool.shutdownNow();
        }

        // 결정적 승자: 같은 updatedAt 이면 deviceId 가 가장 큰 쪽이다.
        HttpResponse<String> stored = get(account.token());
        assertEquals(200, stored.statusCode());
        assertEquals("device-" + writers, deviceId(stored.body()));
        assertEquals("0." + writers, hudScale(stored.body()));
        assertEquals(stored.body(), repository.findById(account.accountId()).orElseThrow().document());
    }

    // ---------------------------------------------------------------- 검증

    @Test
    void rejectsInvalidDocuments() {
        Account account = newAccount();
        String updatedAt = nowMillis();

        assertRejected(account, null);
        assertRejected(account, "");
        assertRejected(account, "not json at all");
        assertRejected(account, "[1,2,3]");
        assertRejected(account, "{\"version\":1}");
        // 지원하지 않는 봉투 버전
        assertRejected(account, document(updatedAt, "device-a", "0.8").replace("\"version\": 1", "\"version\": 2"));
        assertRejected(account, document(updatedAt, "device-a", "0.8").replace("\"version\": 1", "\"version\": \"1\""));
        // metadata 결손·형식
        assertRejected(account, """
                { "version": 1, "hud": { "values": [] } }
                """);
        assertRejected(account, """
                { "version": 1, "metadata": "nope" }
                """);
        assertRejected(account, metadataDocument("{ \"settingsVersion\": -1, \"updatedAt\": \"" + updatedAt
                + "\", \"deviceId\": \"device-a\" }"));
        assertRejected(account, metadataDocument("{ \"settingsVersion\": 1, \"updatedAt\": \"어제\","
                + " \"deviceId\": \"device-a\" }"));
        assertRejected(account, metadataDocument("{ \"settingsVersion\": 1, \"updatedAt\": "
                + "\"2026-09-13T00:00:00.000+09:00\", \"deviceId\": \"device-a\" }"));
        assertRejected(account, metadataDocument("{ \"settingsVersion\": 1, \"updatedAt\": \"" + updatedAt
                + "\", \"deviceId\": \"\" }"));
        assertRejected(account, metadataDocument("{ \"settingsVersion\": 1, \"updatedAt\": \"" + updatedAt
                + "\", \"deviceId\": \"device a/../../etc\" }"));
        assertRejected(account, metadataDocument("{ \"settingsVersion\": 1, \"updatedAt\": \"" + updatedAt
                + "\", \"deviceId\": 12345 }"));
        // 서버 시각보다 5분 넘게 앞선 미래
        assertRejected(account, document(Instant.now().plusSeconds(10 * 60).truncatedTo(ChronoUnit.MILLIS).toString(),
                "device-a", "0.8"));
        // 비객체 category / 잘못된 항목 모양
        assertRejected(account, """
                { "version": 1, "hud": "0.8",
                  "metadata": { "settingsVersion": 1, "updatedAt": "%s", "deviceId": "device-a" } }
                """.formatted(updatedAt));
        assertRejected(account, """
                { "version": 1, "hud": { "values": { "id": "hud.scale" } },
                  "metadata": { "settingsVersion": 1, "updatedAt": "%s", "deviceId": "device-a" } }
                """.formatted(updatedAt));
        assertRejected(account, """
                { "version": 1, "hud": { "values": [ { "id": "hud.scale", "value": 0.8 } ] },
                  "metadata": { "settingsVersion": 1, "updatedAt": "%s", "deviceId": "device-a" } }
                """.formatted(updatedAt));
        assertRejected(account, """
                { "version": 1, "keyboardMouse": { "bindings": [ { "actionId": "attack" } ] },
                  "metadata": { "settingsVersion": 1, "updatedAt": "%s", "deviceId": "device-a" } }
                """.formatted(updatedAt));
        // 64KiB 초과 (나머지는 전부 적법한 문서다)
        assertRejected(account, oversizedDocument(updatedAt, "device-a"));

        // 거부된 요청은 어떤 상태도 남기지 않는다.
        assertEquals(204, get(account.token()).statusCode());
        assertTrue(repository.findById(account.accountId()).isEmpty());
    }

    @Test
    void acceptsTimestampWithinAllowedFutureSkew() {
        Account account = newAccount();
        String slightlyAhead = Instant.now().plusSeconds(60).truncatedTo(ChronoUnit.MILLIS).toString();

        HttpResponse<String> response = put(account.token(), document(slightlyAhead, "device-a", "0.9"));
        assertEquals(200, response.statusCode(), response.body());
        assertEquals("0.9", hudScale(response.body()));
    }

    @Test
    void unauthenticatedInvalidBodyStillFailsAuthenticationFirst() {
        HttpResponse<String> response = request("PUT", SETTINGS_PATH, null, "not json at all");
        assertEquals(401, response.statusCode(), response.body());
        assertEquals("TOKEN_INVALID", code(response));
    }

    private void assertRejected(Account account, String body) {
        HttpResponse<String> response = put(account.token(), body);
        assertEquals(400, response.statusCode(), "거부되어야 하는 본문: " + response.body());
        assertEquals("VALIDATION_ERROR", code(response));
        assertFalse(String.valueOf(response.body()).contains(PASSWORD), "오류 본문에 비밀번호가 없어야 한다");
    }

    // ---------------------------------------------------------------- 문서 생성기

    private static String nowMillis() {
        return Instant.now().truncatedTo(ChronoUnit.MILLIS).toString();
    }

    private static String document(String updatedAt, String deviceId, String hudScale) {
        return """
                {
                  "version": 1,
                  "gameplay": { "values": [ { "id": "gameplay.damageNumbers", "value": "true" } ] },
                  "hud": { "values": [ { "id": "hud.scale", "value": "%s" } ], "bindings": [] },
                  "keyboardMouse": { "values": [], "bindings": [ { "actionId": "attack", "path": "<Mouse>/leftButton" } ] },
                  "controller": { "values": [] },
                  "audio": { "values": [ { "id": "audio.master", "value": "0.5" } ] },
                  "graphics": { "values": [] },
                  "accessibility": { "values": [] },
                  "metadata": { "settingsVersion": 1, "updatedAt": "%s", "deviceId": "%s" }
                }
                """.formatted(hudScale, updatedAt, deviceId);
    }

    private static String metadataDocument(String metadata) {
        return """
                { "version": 1, "hud": { "values": [ { "id": "hud.scale", "value": "0.8" } ] }, "metadata": %s }
                """.formatted(metadata);
    }

    /** 항목 하나하나는 전부 적법하고 봉투 크기만 64KiB 를 넘는 문서. */
    private static String oversizedDocument(String updatedAt, String deviceId) {
        String filler = "x".repeat(1000);
        StringBuilder values = new StringBuilder();
        for (int i = 0; i < 500; i++) {
            if (i > 0) {
                values.append(',');
            }
            values.append("{\"id\":\"hud.pad").append(i).append("\",\"value\":\"").append(filler).append("\"}");
        }
        return "{\"version\":1,\"hud\":{\"values\":[" + values + "]},"
                + "\"metadata\":{\"settingsVersion\":1,\"updatedAt\":\"" + updatedAt
                + "\",\"deviceId\":\"" + deviceId + "\"}}";
    }

    // ---------------------------------------------------------------- HTTP

    private record Account(String loginId, String accountId, String token) {
    }

    private Account newAccount() {
        String suffix = UUID.randomUUID().toString().replace("-", "").substring(0, 10);
        String loginId = "settings-" + suffix + "@example.com";
        String nickname = "s" + suffix;
        String body = "{\"loginId\":\"%s\",\"password\":\"%s\",\"nickname\":\"%s\"}"
                .formatted(loginId, PASSWORD, nickname);
        HttpResponse<String> signup = request("POST", "/api/v1/auth/signup", null, body);
        assertEquals(201, signup.statusCode(), signup.body());
        return login(loginId);
    }

    private Account login(String loginId) {
        String body = "{\"loginId\":\"%s\",\"password\":\"%s\"}".formatted(loginId, PASSWORD);
        HttpResponse<String> response = request("POST", "/api/v1/auth/login", null, body);
        assertEquals(200, response.statusCode(), response.body());
        Map<String, Object> parsed = json(response.body());
        Map<String, Object> account = object(parsed.get("account"));
        return new Account(loginId, (String) account.get("accountId"), (String) parsed.get("accessToken"));
    }

    private HttpResponse<String> get(String token) {
        return request("GET", SETTINGS_PATH, "Bearer " + token, null);
    }

    private HttpResponse<String> put(String token, String body) {
        return request("PUT", SETTINGS_PATH, "Bearer " + token, body);
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
        HttpRequest httpRequest = builder.method(method, payload).build();
        try {
            return client.send(httpRequest, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        } catch (IOException ex) {
            throw new IllegalStateException("HTTP 호출 실패: " + method + " " + path, ex);
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("HTTP 호출 중단: " + method + " " + path, ex);
        }
    }

    // ---------------------------------------------------------------- JSON 도우미

    private String code(HttpResponse<String> response) {
        return (String) json(response.body()).get("code");
    }

    private String hudScale(String document) {
        return (String) object(firstValue(json(document), "hud")).get("value");
    }

    private String deviceId(String document) {
        return (String) object(json(document).get("metadata")).get("deviceId");
    }

    private static Object firstValue(Map<String, Object> document, String category) {
        List<?> values = (List<?>) object(document.get(category)).get("values");
        return values.get(0);
    }

    private Map<String, Object> json(String body) {
        try {
            return object(mapper.readValue(body, Map.class));
        } catch (Exception ex) {
            // Jackson 3 는 unchecked, Jackson 2 는 checked — 양쪽에서 컴파일되도록 Exception 으로 받는다.
            throw new IllegalStateException("응답 JSON 을 해석할 수 없습니다", ex);
        }
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> object(Object value) {
        return (Map<String, Object>) value;
    }
}
