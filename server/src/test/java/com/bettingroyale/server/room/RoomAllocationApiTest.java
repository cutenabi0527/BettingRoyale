package com.bettingroyale.server.room;

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

/** docs/server/api.md 10절 계약 — 인증 경계 (S15P21A606-321). */
@SpringBootTest
@AutoConfigureMockMvc
class RoomAllocationApiTest {

    private static final Pattern ACCESS_TOKEN = Pattern.compile("\"accessToken\":\"([^\"]+)\"");
    private static final Pattern TICKET_ID = Pattern.compile("\"ticketId\":\"([^\"]+)\"");

    @Autowired
    private MockMvc mockMvc;

    @Test
    void 토큰_없이_방을_요청하면_401_이다() throws Exception {
        mockMvc.perform(post("/api/v1/rooms/tickets").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"title\":\"방\",\"isPublic\":true,\"maxPlayers\":6,\"password\":\"\"}"))
                .andExpect(status().isUnauthorized());
    }

    @Test
    void 로그인한_사람은_티켓을_내고_조회하고_취소한다() throws Exception {
        String token = signupAndLogin();
        String body = mockMvc.perform(post("/api/v1/rooms/tickets").header("Authorization", "Bearer " + token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"title\":\"방\",\"isPublic\":true,\"maxPlayers\":6,\"password\":\"\"}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("QUEUED"))
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = TICKET_ID.matcher(body);
        Assertions.assertTrue(matcher.find(), body);
        String ticketId = matcher.group(1);

        mockMvc.perform(get("/api/v1/rooms/tickets/" + ticketId).header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.ticketId").value(ticketId));
        mockMvc.perform(post("/api/v1/rooms/tickets/" + ticketId + "/cancel").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("CANCELED"));
    }

    @Test
    void 서버_키가_틀리면_하트비트는_401_이다() throws Exception {
        mockMvc.perform(post("/api/v1/game-servers/heartbeat").header(GameServerController.SERVER_KEY_HEADER, "wrong")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"serverId\":\"s-api\",\"state\":\"IDLE\"}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value("SERVER_KEY_INVALID"));
    }

    @Test
    void 서버_키가_맞으면_하트비트를_받는다() throws Exception {
        mockMvc.perform(post("/api/v1/game-servers/heartbeat")
                        .header(GameServerController.SERVER_KEY_HEADER, "test-game-server-key")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"serverId\":\"s-api\",\"state\":\"BUSY\",\"ticketId\":\"\",\"roomCode\":\"\"}"))
                .andExpect(status().isOk());
    }

    private String signupAndLogin() throws Exception {
        String suffix = UUID.randomUUID().toString().substring(0, 8);
        String email = "room-" + suffix + "@test.local";
        mockMvc.perform(post("/api/v1/auth/signup").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"loginId\":\"" + email + "\",\"password\":\"password-1\",\"nickname\":\"room-" + suffix + "\"}"))
                .andExpect(status().isCreated());
        String body = mockMvc.perform(post("/api/v1/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"loginId\":\"" + email + "\",\"password\":\"password-1\"}"))
                .andExpect(status().isOk())
                .andReturn().getResponse().getContentAsString();
        Matcher matcher = ACCESS_TOKEN.matcher(body);
        Assertions.assertTrue(matcher.find(), body);
        return matcher.group(1);
    }
}
