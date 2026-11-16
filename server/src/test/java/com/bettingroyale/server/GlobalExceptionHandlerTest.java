package com.bettingroyale.server;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.Map;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.system.CapturedOutput;
import org.springframework.boot.test.system.OutputCaptureExtension;
import org.springframework.boot.webmvc.test.autoconfigure.WebMvcTest;
import org.springframework.context.annotation.Import;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

import com.bettingroyale.server.common.GlobalExceptionHandler;
import com.bettingroyale.server.common.ResultConflictException;

@WebMvcTest(controllers = TestThrowingController.class)
@Import({ GlobalExceptionHandler.class, TestThrowingController.class })
@ExtendWith(OutputCaptureExtension.class)
class GlobalExceptionHandlerTest {

    @Autowired
    private MockMvc mockMvc;

    @Test
    void 잘못된_JSON_본문은_파서_내부_메시지를_노출하지_않는다() throws Exception {
        mockMvc.perform(post("/test/echo")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"nickname\":"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("VALIDATION_ERROR"))
                .andExpect(jsonPath("$.detail").value("JSON 문법 또는 필드 타입을 확인하세요"));
    }

    @Test
    void 없는_경로는_500이_아니라_404_NOT_FOUND_다() throws Exception {
        mockMvc.perform(get("/api/v1/nope"))
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("NOT_FOUND"));
    }

    @Test
    void 지원하지_않는_메서드는_500이_아니라_405_METHOD_NOT_ALLOWED_다() throws Exception {
        mockMvc.perform(get("/test/echo"))
                .andExpect(status().isMethodNotAllowed())
                .andExpect(jsonPath("$.code").value("METHOD_NOT_ALLOWED"));
    }

    @Test
    void 사엑스엑스_오류는_코드와_상관_식별자를_WARN_로그에_남긴다(CapturedOutput output) throws Exception {
        mockMvc.perform(post("/test/conflict"))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("RESULT_CONFLICT"));

        assertTrue(output.getOut().contains("RESULT_CONFLICT"),
                "오류 코드가 로그에 남아야 matchId 와 상관 분석이 가능하다");
        assertTrue(output.getOut().contains("matchId=11111111-2222-4333-8444-555555555555"),
                "detail 의 matchId 가 로그에 남아야 grep 한 번으로 추적된다");
    }

    @Test
    void 내부_예외는_원본_메시지를_노출하지_않는다() throws Exception {
        mockMvc.perform(post("/test/fail"))
                .andExpect(status().isInternalServerError())
                .andExpect(jsonPath("$.code").value("INTERNAL_ERROR"))
                .andExpect(jsonPath("$.detail").value("예상하지 못한 오류가 발생했습니다"));
    }
}

@RestController
class TestThrowingController {
    @PostMapping("/test/echo")
    Map<String, Object> echo(@RequestBody Map<String, Object> payload) {
        return payload;
    }

    @PostMapping("/test/fail")
    void fail() {
        throw new IllegalStateException("db password=secret");
    }

    @PostMapping("/test/conflict")
    void conflict() {
        throw new ResultConflictException("11111111-2222-4333-8444-555555555555", "log-probe");
    }
}
