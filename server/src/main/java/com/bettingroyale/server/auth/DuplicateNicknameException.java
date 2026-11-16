package com.bettingroyale.server.auth;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 409 DUPLICATE_NICKNAME — 이미 존재하는 닉네임으로 가입 시도 (api.md 3-6, S-BE-VGE4WD). */
public class DuplicateNicknameException extends ApiException {

    public DuplicateNicknameException(String nickname) {
        super(HttpStatus.CONFLICT, "DUPLICATE_NICKNAME",
                "이미 사용 중인 닉네임입니다", "nickname=" + nickname);
    }
}
