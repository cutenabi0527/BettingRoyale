package com.bettingroyale.server.account;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 소유하지 않은 초상화를 프로필로 지정하려 했다 (S-UX-RQ5N9E). */
public class PortraitNotOwnedException extends ApiException {

    public PortraitNotOwnedException(String accountId, String portraitId) {
        super(HttpStatus.FORBIDDEN, "PORTRAIT_NOT_OWNED",
                "소유하지 않은 프로필 초상화입니다",
                "accountId=" + accountId + ", portraitId=" + portraitId);
    }
}
