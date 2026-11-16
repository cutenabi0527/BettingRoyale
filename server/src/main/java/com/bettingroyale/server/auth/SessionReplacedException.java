package com.bettingroyale.server.auth;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 401 SESSION_REPLACED — 같은 계정이 다른 곳에서 로그인해 이 토큰이 무효화됨 (api.md 3-7, S-BE-VGE4WD). */
public class SessionReplacedException extends ApiException {

    public SessionReplacedException(String accountId) {
        super(HttpStatus.UNAUTHORIZED, "SESSION_REPLACED",
                "다른 기기에서 로그인되어 로그아웃되었습니다", "accountId=" + accountId);
    }
}
