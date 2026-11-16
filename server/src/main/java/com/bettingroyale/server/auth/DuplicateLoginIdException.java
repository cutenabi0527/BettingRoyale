package com.bettingroyale.server.auth;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 409 DUPLICATE_LOGIN_ID — 이미 존재하는 loginId(이메일)로 가입 시도 (api.md 3-6). */
public class DuplicateLoginIdException extends ApiException {

    public DuplicateLoginIdException(String loginId) {
        super(HttpStatus.CONFLICT, "DUPLICATE_LOGIN_ID",
                "이미 사용 중인 아이디입니다", "loginId=" + mask(loginId));
    }

    /**
     * 이메일은 서버 키(accountId·matchId)와 달리 개인정보다 — detail 은 4xx WARN 로그로도 남으므로
     * 평문을 두지 않는다. 요청자는 자기가 보낸 값을 이미 알아서 응답 쪽 손실도 없다.
     */
    private static String mask(String loginId) {
        int at = loginId.indexOf('@');
        if (at <= 0) {
            return "***";
        }
        String local = loginId.substring(0, at);
        return local.substring(0, Math.min(2, local.length())) + "***" + loginId.substring(at);
    }
}
