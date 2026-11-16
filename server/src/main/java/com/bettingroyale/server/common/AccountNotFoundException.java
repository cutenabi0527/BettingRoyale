package com.bettingroyale.server.common;

import org.springframework.http.HttpStatus;

public class AccountNotFoundException extends ApiException {

    public AccountNotFoundException(String accountId) {
        super(HttpStatus.NOT_FOUND, "ACCOUNT_NOT_FOUND", "계정이 존재하지 않습니다", "accountId=" + accountId);
    }
}
