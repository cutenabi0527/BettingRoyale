package com.bettingroyale.server.settings;

import java.time.Instant;
import java.util.Optional;

import org.springframework.dao.ConcurrencyFailureException;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.stereotype.Service;

import jakarta.persistence.LockTimeoutException;
import jakarta.persistence.PessimisticLockException;

/**
 * 설정 조회·저장 유스케이스 (docs/server/api.md 7절).
 *
 * <p>계정은 호출자가 이미 JWT 로 확인한 계정만 넘긴다 — 이 서비스는 계정 ID 를 요청 본문이나 경로에서 받지 않는다.
 * 검증은 트랜잭션 밖에서 먼저 끝내고, 잠금이 필요한 구간만 {@link UserSettingsStore} 의 트랜잭션에 맡긴다.
 *
 * <p>재시도는 트랜잭션 밖에서 해야 의미가 있으므로(실패한 트랜잭션은 이미 롤백 대상이다) 저장 빈을 분리했다.
 */
@Service
public class UserSettingsService {

    /** 잠금 경합·중복키는 드물게 발생할 수 있는 일시적 실패다 — 짧게 몇 번만 다시 시도한다. */
    private static final int MAX_WRITE_ATTEMPTS = 3;
    private static final long RETRY_BACKOFF_MILLIS = 10L;

    private final SettingsDocumentValidator validator;
    private final UserSettingsStore store;

    UserSettingsService(SettingsDocumentValidator validator, UserSettingsStore store) {
        this.validator = validator;
        this.store = store;
    }

    /** 저장 전이면 비어 있다 — 컨트롤러가 204 로 옮긴다. */
    public Optional<StoredSettings> read(String accountId) {
        return store.read(accountId);
    }

    /** 검증 실패는 400 VALIDATION_ERROR, 성공은 저장 후의 정본이다. */
    public StoredSettings write(String accountId, String rawBody) {
        ValidatedSettings incoming = validator.validate(rawBody, Instant.now());

        RuntimeException lastFailure = null;
        for (int attempt = 1; attempt <= MAX_WRITE_ATTEMPTS; attempt++) {
            try {
                return store.write(accountId, incoming);
            } catch (ConcurrencyFailureException | DataIntegrityViolationException
                    | PessimisticLockException | LockTimeoutException ex) {
                // 계정 행 잠금이 최초 삽입을 직렬화하지만, 잠금 대기 만료·데드락 희생자는 여전히 가능하다.
                // JPA 예외가 Spring 예외로 번역되지 않고 올라오는 경로가 있어 양쪽을 모두 잡는다.
                lastFailure = ex;
                backoff(attempt);
            }
        }
        throw lastFailure;
    }

    private static void backoff(int attempt) {
        try {
            Thread.sleep(RETRY_BACKOFF_MILLIS * attempt);
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("설정 저장 재시도가 중단되었습니다", ex);
        }
    }
}
