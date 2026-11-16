package com.bettingroyale.server.auth;

import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * JWT 파라미터. 만료 12시간은 ⚠️ 임시값(S-BE-JNTJTE) — application.yml 에서만 바꾼다.
 * secret 은 배포에서 env(JWT_SECRET)로 주입한다 (D-036).
 */
@ConfigurationProperties(prefix = "auth.jwt")
public record AuthJwtProperties(String secret, long expiresInSeconds) {
}
