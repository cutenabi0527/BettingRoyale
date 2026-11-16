package com.bettingroyale.server;

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
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

/**
 * docs/server/api.md 9절(대전 지표 수집) 계약 검증. 스펙은 S-GD-GSP9RQ.
 *
 * <p>이 시험이 지키는 핵심은 <b>재전송이 안전하다</b> 는 것이다 — 7-3 이 "실패하면 로컬 버퍼에
 * 두고 다음 실행에서 재전송" 을 규정하므로, 같은 배치를 두 번 보내도 행이 늘면 안 된다.</p>
 */
@SpringBootTest
@AutoConfigureMockMvc
class TelemetryApiTest {

    private static final Pattern ACCESS_TOKEN = Pattern.compile("\"accessToken\":\"([^\"]+)\"");

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private JdbcTemplate jdbc;

    @Test
    void 배치를_받아_202_와_수용_건수를_돌려준다() throws Exception {
        String token = newAccountToken();
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(ingest(token, matchId, hostBatch()))
                .andExpect(status().isAccepted())
                .andExpect(jsonPath("$.matchId").value(matchId))
                .andExpect(jsonPath("$.accepted.match").value(1))
                .andExpect(jsonPath("$.accepted.participants").value(2))
                .andExpect(jsonPath("$.accepted.blessingOffers").value(2))
                .andExpect(jsonPath("$.ignoredDuplicates.match").value(0));

        Assertions.assertEquals(1, countRows("match_telemetry", matchId));
        Assertions.assertEquals(2, countRows("match_participants", matchId));
        Assertions.assertEquals(2, countRows("blessing_offers", matchId));
    }

    /** 재전송이 안전해야 한다 — 7-3 의 로컬 버퍼 재전송이 이것에 기댄다. */
    @Test
    void 같은_배치를_다시_보내도_행이_늘지_않는다() throws Exception {
        String token = newAccountToken();
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(ingest(token, matchId, hostBatch())).andExpect(status().isAccepted());
        mockMvc.perform(ingest(token, matchId, hostBatch()))
                .andExpect(status().isAccepted())
                .andExpect(jsonPath("$.accepted.match").value(0))
                .andExpect(jsonPath("$.accepted.participants").value(0))
                .andExpect(jsonPath("$.ignoredDuplicates.match").value(1))
                .andExpect(jsonPath("$.ignoredDuplicates.participants").value(2))
                .andExpect(jsonPath("$.ignoredDuplicates.blessingOffers").value(2));

        Assertions.assertEquals(1, countRows("match_telemetry", matchId));
        Assertions.assertEquals(2, countRows("match_participants", matchId));
        Assertions.assertEquals(2, countRows("blessing_offers", matchId));
    }

    /** Host 우선은 읽을 때 고른다 — 두 source 의 행은 공존해야 한다 (9-2). */
    @Test
    void host_와_client_보고가_함께_남는다() throws Exception {
        String token = newAccountToken();
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(ingest(token, matchId, hostBatch())).andExpect(status().isAccepted());
        mockMvc.perform(ingest(token, matchId, clientBatch()))
                .andExpect(status().isAccepted())
                .andExpect(jsonPath("$.accepted.match").value(1));

        Assertions.assertEquals(2, countRows("match_telemetry", matchId));
        Assertions.assertEquals(Integer.valueOf(1), jdbc.queryForObject(
                "SELECT COUNT(*) FROM match_telemetry WHERE match_id = ? AND source = 'host'",
                Integer.class, matchId));
    }

    /** 방이 중간에 터지면 endedAt 이 없고, host 배치에는 decisionMillis 가 없다. */
    @Test
    void ended_at_과_decision_millis_가_없어도_저장된다() throws Exception {
        String token = newAccountToken();
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(ingest(token, matchId, abandonedBatch()))
                .andExpect(status().isAccepted())
                .andExpect(jsonPath("$.accepted.match").value(1));

        Assertions.assertEquals(Integer.valueOf(1), jdbc.queryForObject(
                "SELECT COUNT(*) FROM match_telemetry WHERE match_id = ? AND ended_at IS NULL",
                Integer.class, matchId));
    }

    @Test
    void 알_수_없는_source_는_400() throws Exception {
        String token = newAccountToken();
        String body = hostBatch().replace("\"source\": \"host\"", "\"source\": \"spectator\"");
        mockMvc.perform(ingest(token, UUID.randomUUID().toString(), body))
                .andExpect(status().isBadRequest());
    }

    /** 둔기는 예약된 확장 지점이라 값으로 올 수 없다 (S-GD-T8H7W7). */
    @Test
    void 알_수_없는_weapon_은_400() throws Exception {
        String token = newAccountToken();
        String body = hostBatch().replace("\"weapon\": \"Sword\"", "\"weapon\": \"Mace\"");
        mockMvc.perform(ingest(token, UUID.randomUUID().toString(), body))
                .andExpect(status().isBadRequest());
    }

    /** 3-3 과 같은 검증이다 — 짧은 문자열이면 PK 가 매치 간에 겹친다. */
    @Test
    void uuid_가_아닌_matchId_는_400() throws Exception {
        String token = newAccountToken();
        mockMvc.perform(ingest(token, "match-1", hostBatch()))
                .andExpect(status().isBadRequest());
    }

    @Test
    void 토큰이_없으면_401() throws Exception {
        mockMvc.perform(post("/api/v1/telemetry/matches/" + UUID.randomUUID())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(hostBatch()))
                .andExpect(status().isUnauthorized());
    }

    // ── 도우미 ──────────────────────────────────────────────────────────────

    private MockHttpServletRequestBuilder ingest(String token, String matchId, String body) {
        return post("/api/v1/telemetry/matches/" + matchId)
                .header("Authorization", "Bearer " + token)
                .contentType(MediaType.APPLICATION_JSON)
                .content(body);
    }

    private int countRows(String table, String matchId) {
        Integer count = jdbc.queryForObject(
                "SELECT COUNT(*) FROM " + table + " WHERE match_id = ?", Integer.class, matchId);
        return count == null ? 0 : count;
    }

    private static String hostBatch() {
        return "{"
                + "\"source\": \"host\","
                + "\"schemaVersion\": 1,"
                + "\"match\": {"
                + "  \"mapId\": \"Map_Default_Preparation_02\", \"playerCount\": 8, \"roundCount\": 3,"
                + "  \"endedReason\": \"completed\", \"buildVersion\": \"br-proto-3\","
                + "  \"dataVersion\": \"20260921-101500\","
                + "  \"startedAt\": \"2026-09-21T10:15:00Z\", \"endedAt\": \"2026-09-21T10:23:40Z\""
                + "},"
                + "\"participants\": ["
                + "  { \"accountId\": \"acc_one\", \"weapon\": \"Sword\", \"finalRank\": 1, \"finalScore\": 245,"
                + "    \"kills\": 6, \"deaths\": 2, \"assists\": 3, \"damageDealt\": 4120,"
                + "    \"damageTaken\": 2980, \"goldEarned\": 1800, \"goldSpent\": 1500,"
                + "    \"survivalSeconds\": 520 },"
                + "  { \"accountId\": \"acc_two\", \"weapon\": \"Bow\", \"finalRank\": 2, \"finalScore\": 180,"
                + "    \"kills\": 3, \"deaths\": 4, \"assists\": 2, \"damageDealt\": 3100,"
                + "    \"damageTaken\": 3400, \"goldEarned\": 1400, \"goldSpent\": 1200,"
                + "    \"survivalSeconds\": 470 }"
                + "],"
                + "\"blessingOffers\": ["
                + "  { \"accountId\": \"acc_one\", \"roundNo\": 1, \"blessingId\": \"bls_swift_step\","
                + "    \"picked\": true, \"rerollCount\": 1, \"rerollGoldSpent\": 50 },"
                + "  { \"accountId\": \"acc_one\", \"roundNo\": 1, \"blessingId\": \"bls_iron_skin\","
                + "    \"picked\": false, \"rerollCount\": 1, \"rerollGoldSpent\": 50 }"
                + "]"
                + "}";
    }

    private static String clientBatch() {
        return hostBatch().replace("\"source\": \"host\"", "\"source\": \"client\"");
    }

    /** 방이 중간에 터진 배치 — endedAt 이 없다. */
    private static String abandonedBatch() {
        return hostBatch()
                .replace(", \"endedAt\": \"2026-09-21T10:23:40Z\"", "")
                .replace("\"completed\"", "\"abandoned\"");
    }

    private String newAccountToken() throws Exception {
        String email = "tm-" + UUID.randomUUID().toString().substring(0, 8) + "@example.com";
        String password = "password-1";
        String nickname = "tm-" + UUID.randomUUID().toString().substring(0, 8);
        mockMvc.perform(post("/api/v1/auth/signup")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"loginId\":\"" + email + "\",\"password\":\"" + password
                                + "\",\"nickname\":\"" + nickname + "\"}"))
                .andExpect(status().isCreated());

        String body = mockMvc.perform(post("/api/v1/auth/login")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"loginId\":\"" + email + "\",\"password\":\"" + password + "\"}"))
                .andExpect(status().isOk())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCESS_TOKEN.matcher(body);
        Assertions.assertTrue(matcher.find(), "login 응답에 accessToken 이 없다: " + body);
        return matcher.group(1);
    }
}
