package com.bettingroyale.server.auth;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 401 TOKEN_EXPIRED — 만료 시 재로그인 유도, refresh token 없음 (api.md 2절, O-012). */
public class TokenExpiredException extends ApiException {

    public TokenExpiredException() {
        super(HttpStatus.UNAUTHORIZED, "TOKEN_EXPIRED",
                "인증이 만료되었습니다. 다시 로그인해 주세요", null);
    }
}
