package com.bettingroyale.server.settings;

import java.time.Instant;
import java.util.Optional;

import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.bettingroyale.server.account.AccountEntity;

import jakarta.persistence.EntityManager;
import jakarta.persistence.LockModeType;
import jakarta.persistence.PersistenceContext;

/**
 * 설정 정본의 트랜잭션 경계 (docs/server/api.md 7절).
 *
 * <p>쓰기는 <b>계정 행 비관적 잠금</b> 아래에서만 한다. 같은 계정의 최초 PUT 두 개가 동시에 오면
 * 둘 다 "행이 없다"를 보고 각자 INSERT 해 하나가 중복키로 깨지는데, 계정 행을 먼저 잠그면 두 번째 요청은
 * 첫 번째의 커밋을 본 뒤 일반적인 LWW 갱신 경로로 들어간다. 잠금 대상이 설정 행이 아니라 계정 행인 이유는
 * <b>아직 존재하지 않는 행은 잠글 수 없기</b> 때문이다.
 */
@Service
public class UserSettingsStore {

    private final UserSettingsRepository repository;

    /**
     * 생성자 주입 대신 {@code @PersistenceContext} 를 쓴다 — 트랜잭션 바운드 EntityManager 프록시 주입은
     * 이 애너테이션이 정본 경로다.
     */
    @PersistenceContext
    private EntityManager entityManager;

    public UserSettingsStore(UserSettingsRepository repository) {
        this.repository = repository;
    }

    @Transactional(readOnly = true)
    public Optional<StoredSettings> read(String accountId) {
        return repository.findById(accountId).map(StoredSettings::of);
    }

    /**
     * 정본을 기록하고 <b>기록 후의 정본</b>을 돌려준다. 들어온 문서가 LWW 비교에서 지면 기존 정본을 그대로 돌려준다
     * (덮어쓰지 않는다). 응답 코드는 어느 경우든 200 이다 — 클라이언트는 돌려받은 문서로 로컬을 맞춘다.
     */
    @Transactional
    public StoredSettings write(String accountId, ValidatedSettings incoming) {
        AccountEntity account = entityManager.find(AccountEntity.class, accountId, LockModeType.PESSIMISTIC_WRITE);
        if (account == null) {
            throw new SettingsAccountNotFoundException(accountId);
        }

        Instant storedAt = Instant.now();
        UserSettingsEntity existing = entityManager.find(UserSettingsEntity.class, accountId);
        if (existing == null) {
            // 배정 PK 신규 행 — merge(SELECT 후 INSERT) 가 아니라 명시적 INSERT 로 기록한다.
            UserSettingsEntity created = new UserSettingsEntity(accountId, incoming, storedAt);
            entityManager.persist(created);
            entityManager.flush();
            return StoredSettings.of(created);
        }

        if (incoming.envelopeVersion() < existing.envelopeVersion() || !incomingWins(incoming, existing)) {
            // 하위 스키마, 오래된 쓰기, 같은 (updatedAt, deviceId) 재시도는 기존 본문을 유지한다.
            return StoredSettings.of(existing);
        }
        existing.replaceWith(incoming, storedAt);
        entityManager.flush();
        return StoredSettings.of(existing);
    }

    /**
     * 결정적 LWW: 튜플 {@code (updatedAt, deviceId)} 가 큰 쪽이 이긴다.
     * 같은 튜플은 동일 저장의 재시도로 보고 기존 정본을 유지한다 — 어느 노드에서 판정해도 결과가 같다.
     */
    static boolean incomingWins(ValidatedSettings incoming, UserSettingsEntity existing) {
        int byTime = incoming.updatedAt().compareTo(existing.updatedAt());
        if (byTime != 0) {
            return byTime > 0;
        }
        return incoming.deviceId().compareTo(existing.deviceId()) > 0;
    }
}
