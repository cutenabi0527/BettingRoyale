package com.bettingroyale.server;

import static org.hamcrest.Matchers.containsString;
import static org.hamcrest.Matchers.not;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.util.UUID;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.context.TestPropertySource;
import org.springframework.test.web.servlet.MockMvc;

/**
 * docs/server/api.md 계약 검증.
 * 지급량은 기획이 바꿀 수 있으므로(스펙 S-BE-ZK5DKZ) 운영 설정에 의존하지 않고 테스트 값으로 고정한다.
 */
@SpringBootTest
@AutoConfigureMockMvc
@TestPropertySource(properties = {
        "medal.rank-rewards.1=100",
        "medal.rank-rewards.2=60",
        "medal.rank-rewards.3=40",
        "medal.rank-rewards.4=20",
        "medal.default-reward=10"
})
class ApiContractTest {

    private static final Pattern ACCOUNT_ID = Pattern.compile("\"accountId\":\"([^\"]+)\"");

    @Autowired
    private MockMvc mockMvc;

    @Test
    void health_는_UP_을_반환한다() throws Exception {
        mockMvc.perform(get("/health"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("UP"));
    }

    @Test
    void identify_는_같은_닉네임에_같은_계정을_돌려준다() throws Exception {
        String nickname = uniqueNickname();
        String first = identify(nickname);
        String second = identify(nickname);
        org.junit.jupiter.api.Assertions.assertEquals(first, second);
    }

    @Test
    void 결과_제출은_순위_정책대로_메달을_지급한다() throws Exception {
        String winnerNickname = uniqueNickname();
        String winner = identify(winnerNickname);
        String runnerUp = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(winner, winner, 1, runnerUp, 2)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.matchId").value(matchId))
                .andExpect(jsonPath("$.receipts[0].nickname").value(winnerNickname))
                .andExpect(jsonPath("$.receipts[0].medalsAwarded").value(100))
                .andExpect(jsonPath("$.receipts[0].newBalance").value(100))
                .andExpect(jsonPath("$.receipts[0].duplicate").value(false))
                .andExpect(jsonPath("$.receipts[1].medalsAwarded").value(60));

        mockMvc.perform(get("/api/v1/accounts/{id}/balance", winner))
                .andExpect(jsonPath("$.medalBalance").value(100));
    }

    @Test
    void 저장된_결과는_GET_으로_rank_순으로_조회된다() throws Exception {
        String winnerNickname = uniqueNickname();
        String winner = identify(winnerNickname);
        String runnerUp = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(winner, runnerUp, 2, winner, 1)))
                .andExpect(status().isOk());

        mockMvc.perform(get("/api/v1/matches/{matchId}/results", matchId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.matchId").value(matchId))
                .andExpect(jsonPath("$.receipts[0].rank").value(1))
                .andExpect(jsonPath("$.receipts[0].nickname").value(winnerNickname))
                .andExpect(jsonPath("$.receipts[0].medalsAwarded").value(100))
                .andExpect(jsonPath("$.receipts[0].duplicate").value(true))
                .andExpect(jsonPath("$.receipts[1].rank").value(2));
    }

    @Test
    void 기록_없는_매치_조회는_404_다() throws Exception {
        mockMvc.perform(get("/api/v1/matches/{matchId}/results", UUID.randomUUID().toString()))
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("MATCH_NOT_FOUND"));
    }

    @Test
    void 충돌_후_GET_은_최초_제출_결과를_돌려준다() throws Exception {
        String player = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(player, player, 1)))
                .andExpect(status().isOk());

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(player, player, 2)))
                .andExpect(status().isConflict());

        mockMvc.perform(get("/api/v1/matches/{matchId}/results", matchId))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.receipts[0].rank").value(1))
                .andExpect(jsonPath("$.receipts[0].medalsAwarded").value(100));
    }

    @Test
    void 같은_결과를_두_번_제출해도_잔액은_한_번만_오른다() throws Exception {
        String winner = identify(uniqueNickname());
        String matchA = UUID.randomUUID().toString();
        String matchB = UUID.randomUUID().toString();
        String bodyA = resultBody(winner, winner, 1);

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchA)
                        .contentType(MediaType.APPLICATION_JSON).content(bodyA))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.receipts[0].duplicate").value(false))
                .andExpect(jsonPath("$.receipts[0].newBalance").value(100));

        // 다른 매치로 잔액이 변한 뒤 재전송해도 newBalance 는 과거 스냅샷이 아니라 현재 잔액이어야 한다.
        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchB)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(winner, winner, 2)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.receipts[0].newBalance").value(160));

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchA)
                        .contentType(MediaType.APPLICATION_JSON).content(bodyA))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.receipts[0].duplicate").value(true))
                .andExpect(jsonPath("$.receipts[0].medalsAwarded").value(100))
                .andExpect(jsonPath("$.receipts[0].newBalance").value(160));

        mockMvc.perform(get("/api/v1/accounts/{id}/balance", winner))
                .andExpect(jsonPath("$.medalBalance").value(160));
    }

    @Test
    void matchId_가_UUID_형식이_아니면_400_이다() throws Exception {
        String player = identify(uniqueNickname());

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", "1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(player, player, 1)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));

        mockMvc.perform(get("/api/v1/matches/{matchId}/results", "match-ghost"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 같은_rank_가_두_번_들어가면_400_이다() throws Exception {
        String first = identify(uniqueNickname());
        String second = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(first, first, 1, second, 1)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));

        mockMvc.perform(get("/api/v1/accounts/{id}/balance", first))
                .andExpect(jsonPath("$.medalBalance").value(0));
    }

    @Test
    void 같은_매치에_다른_순위를_재제출하면_409_충돌이다() throws Exception {
        String player = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(player, player, 1)))
                .andExpect(status().isOk());

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(player, player, 2)))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("RESULT_CONFLICT"));

        mockMvc.perform(get("/api/v1/accounts/{id}/balance", player))
                .andExpect(jsonPath("$.medalBalance").value(100));
    }

    @Test
    void 없는_계정이_결과에_섞이면_404_이고_아무도_지급받지_않는다() throws Exception {
        String known = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(known, known, 1, "acc_ghost", 2)))
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("ACCOUNT_NOT_FOUND"));

        mockMvc.perform(get("/api/v1/accounts/{id}/balance", known))
                .andExpect(jsonPath("$.medalBalance").value(0));
    }

    @Test
    void 결과_목록에_같은_계정이_두_번_들어가면_400_이다() throws Exception {
        String player = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(player, player, 1, player, 2)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 제출자가_결과_참가자가_아니면_400_이고_아무도_지급받지_않는다() throws Exception {
        String outsider = identify(uniqueNickname());
        String first = identify(uniqueNickname());
        String second = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(outsider, first, 1, second, 2)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));

        mockMvc.perform(get("/api/v1/accounts/{id}/balance", first))
                .andExpect(jsonPath("$.medalBalance").value(0));
        mockMvc.perform(get("/api/v1/accounts/{id}/balance", second))
                .andExpect(jsonPath("$.medalBalance").value(0));
    }

    @Test
    void 최초_결과_확정_뒤_제출자_교체나_참가자_추가는_409_이고_추가_지급이_없다() throws Exception {
        String first = identify(uniqueNickname());
        String second = identify(uniqueNickname());
        String appended = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();
        String original = resultBody(first, first, 1, second, 2);

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(original))
                .andExpect(status().isOk());

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(second, first, 1, second, 2)))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("RESULT_CONFLICT"));

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(first, first, 1, second, 2, appended, 3)))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("RESULT_CONFLICT"));

        mockMvc.perform(get("/api/v1/accounts/{id}/balance", first))
                .andExpect(jsonPath("$.medalBalance").value(100));
        mockMvc.perform(get("/api/v1/accounts/{id}/balance", second))
                .andExpect(jsonPath("$.medalBalance").value(60));
        mockMvc.perform(get("/api/v1/accounts/{id}/balance", appended))
                .andExpect(jsonPath("$.medalBalance").value(0));
    }

    @Test
    void 순위가_1_미만이면_400_이다() throws Exception {
        String player = identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();

        mockMvc.perform(post("/api/v1/matches/{matchId}/results", matchId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(resultBody(player, player, 0)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"));
    }

    @Test
    void 없는_계정의_잔액_조회는_404_다() throws Exception {
        mockMvc.perform(get("/api/v1/accounts/{id}/balance", "acc_ghost"))
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("ACCOUNT_NOT_FOUND"));
    }

    @Test
    void 잘못된_JSON_본문은_파서_내부_예외를_노출하지_않는다() throws Exception {
        mockMvc.perform(post("/api/v1/accounts/identify")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"nickname\":"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"))
                .andExpect(jsonPath("$.detail").value("JSON 문법 또는 필드 타입을 확인하세요"))
                .andExpect(jsonPath("$.detail").value(not(containsString("Json"))));
    }

    private String identify(String nickname) throws Exception {
        String response = mockMvc.perform(post("/api/v1/accounts/identify")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"nickname\":\"" + nickname + "\"}"))
                .andExpect(status().isOk())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCOUNT_ID.matcher(response);
        org.junit.jupiter.api.Assertions.assertTrue(matcher.find(), "identify 응답에 accountId 가 없다: " + response);
        return matcher.group(1);
    }

    /** (submitter, accountId, rank) 쌍들의 결과 제출 본문을 만든다. */
    private String resultBody(String submitter, Object... accountRankPairs) {
        StringBuilder results = new StringBuilder();
        for (int i = 0; i < accountRankPairs.length; i += 2) {
            if (i > 0) {
                results.append(',');
            }
            results.append("{\"accountId\":\"").append(accountRankPairs[i])
                    .append("\",\"rank\":").append(accountRankPairs[i + 1]).append('}');
        }
        return "{\"submitterAccountId\":\"" + submitter + "\",\"results\":[" + results + "]}";
    }

    private static String uniqueNickname() {
        return "tester-" + UUID.randomUUID().toString().substring(0, 8);
    }
}
