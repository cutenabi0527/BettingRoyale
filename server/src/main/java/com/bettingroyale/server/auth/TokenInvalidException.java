package com.bettingroyale.server.auth;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 401 TOKEN_INVALID — Authorization 헤더 누락·형식 오류·서명 불일치 (api.md 2절). */
public class TokenInvalidException extends ApiException {

    public TokenInvalidException(String detail) {
        super(HttpStatus.UNAUTHORIZED, "TOKEN_INVALID",
                "인증 토큰이 유효하지 않습니다", detail);
    }
}
