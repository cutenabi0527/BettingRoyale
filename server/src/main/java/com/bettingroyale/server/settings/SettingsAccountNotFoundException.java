package com.bettingroyale.server.settings;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/**
 * 토큰은 유효한데 계정 행이 사라진 경우(동시 삭제 등)의 방어적 경로 — api.md 2절의 404 ACCOUNT_NOT_FOUND.
 * 정상 흐름에서는 발생하지 않는다: 계정은 {@code AuthService.authenticate} 가 이미 확인했다.
 */
public class SettingsAccountNotFoundException extends ApiException {

    public SettingsAccountNotFoundException(String accountId) {
        super(HttpStatus.NOT_FOUND, "ACCOUNT_NOT_FOUND", "계정이 존재하지 않습니다", "accountId=" + accountId);
    }
}
