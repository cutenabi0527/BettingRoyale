package com.bettingroyale.server.common;

import org.springframework.http.HttpStatus;

public class BadRequestException extends ApiException {

    public BadRequestException(String message, String detail) {
        super(HttpStatus.BAD_REQUEST, "VALIDATION_ERROR", message, detail);
    }
}
