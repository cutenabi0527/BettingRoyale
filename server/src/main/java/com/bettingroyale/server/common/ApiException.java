package com.bettingroyale.server.common;

import org.springframework.http.HttpStatus;

/** 계약(docs/server/api.md 2절)의 오류 모델로 변환되는 예외의 공통 부모. */
public abstract class ApiException extends RuntimeException {

    private final HttpStatus status;
    private final String code;
    private final String detail;

    protected ApiException(HttpStatus status, String code, String message, String detail) {
        super(message);
        this.status = status;
        this.code = code;
        this.detail = detail;
    }

    public HttpStatus status() {
        return status;
    }

    public String code() {
        return code;
    }

    public String detail() {
        return detail;
    }
}
