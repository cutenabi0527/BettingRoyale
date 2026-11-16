package com.bettingroyale.server.settings;

import java.util.Optional;

import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.auth.AuthService;

/**
 * 계정 설정 동기화 (docs/server/api.md 7절, S-UX-8FGK29).
 *
 * <p>🔒 두 엔드포인트 모두 Bearer JWT 필수다. <b>계정은 토큰에서만</b> 정한다 — 경로에도 본문에도 계정 ID 를 받지
 * 않으므로 남의 설정을 읽거나 쓸 경로 자체가 없다. 토큰 실패는 3-8 과 같은 401 규약
 * ({@code TOKEN_INVALID}/{@code TOKEN_EXPIRED}/{@code SESSION_REPLACED})을 그대로 쓴다.
 *
 * <p>본문은 저장된 정본 JSON 문자열을 그대로 돌려준다 — 재직렬화하지 않아야 저장 바이트와 응답 바이트가 같다.
 * 설정 내용·토큰·본문은 로그에 남기지 않는다(로거를 두지 않는 것으로 보장한다).
 */
@RestController
@RequestMapping("/api/v1/accounts/me/settings")
public class UserSettingsController {

    private final AuthService auth;
    private final UserSettingsService settings;

    public UserSettingsController(AuthService auth, UserSettingsService settings) {
        this.auth = auth;
        this.settings = settings;
    }

    /** 저장 전이면 204(본문 없음), 저장 후면 200 과 정본 문서. */
    @GetMapping(produces = MediaType.APPLICATION_JSON_VALUE)
    public ResponseEntity<String> get(
            @RequestHeader(value = "Authorization", required = false) String authorization) {
        AccountEntity account = auth.authenticate(authorization);
        Optional<StoredSettings> stored = settings.read(account.accountId());
        if (stored.isEmpty()) {
            return ResponseEntity.status(HttpStatus.NO_CONTENT).build();
        }
        return document(stored.get());
    }

    /**
     * 항상 200 과 <b>저장 후의 정본</b>을 돌려준다. 들어온 문서가 LWW 에서 지면 기존 정본이 그대로 돌아오며,
     * 클라이언트는 그 문서로 로컬을 맞춘다. 검증 실패는 400 VALIDATION_ERROR 다.
     */
    @PutMapping(produces = MediaType.APPLICATION_JSON_VALUE)
    public ResponseEntity<String> put(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @RequestBody(required = false) String body) {
        AccountEntity account = auth.authenticate(authorization);
        // 본문 파싱 전에 인증부터 한다 — 인증되지 않은 요청에 파싱 비용을 쓰지 않는다.
        return document(settings.write(account.accountId(), body));
    }

    private static ResponseEntity<String> document(StoredSettings stored) {
        return ResponseEntity.ok()
                .contentType(MediaType.APPLICATION_JSON)
                .body(stored.document());
    }
}
