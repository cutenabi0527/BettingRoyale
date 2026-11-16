package com.bettingroyale.server.common;

import java.util.Map;

import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/** 계약(docs/server/api.md 3-1) 고정 경로. Actuator 상세 상태는 /actuator/health 에 따로 있다. */
@RestController
public class HealthController {

    @GetMapping("/health")
    public Map<String, String> health() {
        return Map.of("status", "UP");
    }
}
