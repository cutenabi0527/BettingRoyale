package com.bettingroyale.server.common;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.HttpRequestMethodNotSupportedException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.servlet.resource.NoResourceFoundException;

@RestControllerAdvice
public class GlobalExceptionHandler {

    private static final Logger LOGGER = LoggerFactory.getLogger(GlobalExceptionHandler.class);

    public record ApiError(String code, String message, String detail) {
    }

    @ExceptionHandler(ApiException.class)
    public ResponseEntity<ApiError> handleApi(ApiException ex) {
        // detail 은 로그 전용 필드다 (api.md 2절) — matchId·accountId 상관 분석은 이 로그를 grep 한다 (-36).
        LOGGER.warn("API error {} {}: {}", ex.status().value(), ex.code(), ex.detail());
        return ResponseEntity.status(ex.status())
                .body(new ApiError(ex.code(), ex.getMessage(), ex.detail()));
    }

    @ExceptionHandler(MethodArgumentNotValidException.class)
    public ResponseEntity<ApiError> handleValidation(MethodArgumentNotValidException ex) {
        String detail = ex.getBindingResult().getFieldErrors().stream()
                .map(error -> error.getField() + ": " + error.getDefaultMessage())
                .reduce((a, b) -> a + "; " + b)
                .orElse("요청 필드 검증에 실패했습니다");
        LOGGER.warn("API error 400 VALIDATION_ERROR: {}", detail);
        return ResponseEntity.status(HttpStatus.BAD_REQUEST)
                .body(new ApiError("VALIDATION_ERROR", "요청 형식이 올바르지 않습니다", detail));
    }

    @ExceptionHandler(HttpMessageNotReadableException.class)
    public ResponseEntity<ApiError> handleUnreadable(HttpMessageNotReadableException ex) {
        LOGGER.warn("API error 400 VALIDATION_ERROR: unreadable request body");
        return ResponseEntity.status(HttpStatus.BAD_REQUEST)
                .body(new ApiError("VALIDATION_ERROR", "요청 본문을 해석할 수 없습니다", "JSON 문법 또는 필드 타입을 확인하세요"));
    }

    /**
     * 없는 경로·잘못된 메서드는 아래 Exception 핸들러가 삼키면 500 이 된다 — 계약(api.md 2절)에서
     * INTERNAL_ERROR 는 유일한 "재시도 가능" 코드라, 왜곡되면 클라이언트가 영구 실패를 무한 재시도한다 (v0.3).
     */
    @ExceptionHandler(NoResourceFoundException.class)
    public ResponseEntity<ApiError> handleNoResource(NoResourceFoundException ex) {
        LOGGER.warn("API error 404 NOT_FOUND: path=/{}", ex.getResourcePath());
        return ResponseEntity.status(HttpStatus.NOT_FOUND)
                .body(new ApiError("NOT_FOUND", "요청한 경로가 없습니다", "path=/" + ex.getResourcePath()));
    }

    @ExceptionHandler(HttpRequestMethodNotSupportedException.class)
    public ResponseEntity<ApiError> handleMethodNotSupported(HttpRequestMethodNotSupportedException ex) {
        LOGGER.warn("API error 405 METHOD_NOT_ALLOWED: method={}", ex.getMethod());
        return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED)
                .body(new ApiError("METHOD_NOT_ALLOWED", "지원하지 않는 HTTP 메서드입니다", "method=" + ex.getMethod()));
    }

    @ExceptionHandler(Exception.class)
    public ResponseEntity<ApiError> handleUnknown(Exception ex) {
        LOGGER.error("Unhandled API exception", ex);
        return ResponseEntity.status(HttpStatus.INTERNAL_SERVER_ERROR)
                .body(new ApiError("INTERNAL_ERROR", "서버 내부 오류입니다", "예상하지 못한 오류가 발생했습니다"));
    }
}
