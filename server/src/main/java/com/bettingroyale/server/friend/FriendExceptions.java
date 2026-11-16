package com.bettingroyale.server.friend;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 친구 기능의 계약 오류 (docs/server/api.md 3-15~3-18). */
public final class FriendExceptions {

    private FriendExceptions() {
    }

    /**
     * 대상이 없거나, <b>내가 처리할 권한이 없는 요청</b>이다.
     *
     * <p>권한 없음에 403 이 아니라 404 를 쓴다 — 403 은 "그 id 는 존재한다" 를 알려주므로
     * 남의 요청 id 를 훑어 존재 여부를 알아낼 수 있다 (api.md 3-17).
     */
    public static final class NotFound extends ApiException {
        public NotFound(String detail) {
            super(HttpStatus.NOT_FOUND, "NOT_FOUND", "대상을 찾을 수 없습니다", detail);
        }
    }

    /** 이미 친구이거나 같은 방향의 대기 요청이 있다. */
    public static final class Conflict extends ApiException {
        public Conflict(String detail) {
            super(HttpStatus.CONFLICT, "CONFLICT", "이미 처리된 관계입니다", detail);
        }
    }

    /** 자기 자신에게 보내기, 닉네임 형식 위반 등. */
    public static final class Invalid extends ApiException {
        public Invalid(String detail) {
            super(HttpStatus.BAD_REQUEST, "VALIDATION_ERROR", "요청이 올바르지 않습니다", detail);
        }
    }
}
