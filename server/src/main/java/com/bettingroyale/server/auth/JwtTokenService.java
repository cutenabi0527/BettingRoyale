package com.bettingroyale.server.auth;

import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.Date;

import javax.crypto.SecretKey;

import org.springframework.stereotype.Component;

import io.jsonwebtoken.Claims;
import io.jsonwebtoken.ExpiredJwtException;
import io.jsonwebtoken.JwtException;
import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;

/**
 * HS256 JWT 발급·검증 (api.md 3-7). 클라이언트는 토큰을 해석하지 않는다 — 클레임 구조는 서버 내부다.
 * sub = accountId, ver = 세션 버전(동시 로그인 차단, S-BE-VGE4WD).
 */
@Component
public class JwtTokenService {

    /** 파싱 결과 — 세션 버전 비교는 호출자(AuthService)가 DB 값과 대조한다. */
    public record ParsedToken(String accountId, long sessionVersion) {
    }

    private static final String SESSION_VERSION_CLAIM = "ver";

    private final SecretKey key;
    private final long expiresInSeconds;

    public JwtTokenService(AuthJwtProperties properties) {
        // 잘못된 키로 기동 후 첫 로그인에서야 터지지 않도록 여기서 즉시 검증한다 (HS256 최소 256bit).
        this.key = Keys.hmacShaKeyFor(properties.secret().getBytes(StandardCharsets.UTF_8));
        this.expiresInSeconds = properties.expiresInSeconds();
    }

    public long expiresInSeconds() {
        return expiresInSeconds;
    }

    public String issue(String accountId, long sessionVersion) {
        Instant now = Instant.now();
        return Jwts.builder()
                .subject(accountId)
                .claim(SESSION_VERSION_CLAIM, sessionVersion)
                .issuedAt(Date.from(now))
                .expiration(Date.from(now.plusSeconds(expiresInSeconds)))
                .signWith(key)
                .compact();
    }

    /**
     * @throws TokenExpiredException 만료 (401 TOKEN_EXPIRED)
     * @throws TokenInvalidException 서명 불일치·형식 오류 (401 TOKEN_INVALID)
     */
    public ParsedToken parse(String token) {
        try {
            Claims claims = Jwts.parser().verifyWith(key).build()
                    .parseSignedClaims(token).getPayload();
            Long version = claims.get(SESSION_VERSION_CLAIM, Long.class);
            if (claims.getSubject() == null || version == null) {
                throw new TokenInvalidException("필수 클레임 누락");
            }
            return new ParsedToken(claims.getSubject(), version);
        } catch (ExpiredJwtException ex) {
            throw new TokenExpiredException();
        } catch (JwtException | IllegalArgumentException ex) {
            throw new TokenInvalidException(ex.getMessage());
        }
    }
}
