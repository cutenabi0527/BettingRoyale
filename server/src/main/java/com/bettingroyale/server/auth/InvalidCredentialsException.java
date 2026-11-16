package com.bettingroyale.server.auth;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/**
 * 401 INVALID_CREDENTIALS — 로그인 실패 (api.md 3-7).
 * 아이디 부재와 비밀번호 불일치를 구분하지 않는다(계정 열거 방지) — detail 에도 남기지 않는다.
 */
public class InvalidCredentialsException extends ApiException {

    public InvalidCredentialsException() {
        super(HttpStatus.UNAUTHORIZED, "INVALID_CREDENTIALS",
                "아이디 또는 비밀번호가 올바르지 않습니다", null);
    }
}
