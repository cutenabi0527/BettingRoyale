package com.bettingroyale.server.settings;

import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/**
 * 계정별 설정 정본 1행 (docs/server/api.md 7절, S-UX-8FGK29).
 * 스키마 원본은 Flyway V4 — 여기서는 매핑만 한다. PK 는 계정이 소유한 값이라 직접 배정(assigned)이며,
 * 신규 행은 {@link UserSettingsStore} 가 EntityManager.persist 로 명시 INSERT 한다.
 *
 * <p>비교 메타데이터({@code updatedAt}, {@code deviceId})는 문서 안에도 있지만 별도 컬럼으로도 둔다 —
 * LWW 판정을 JSON 파싱 없이 하고, DB 정밀도(밀리초)와 비교 정밀도를 일치시키기 위해서다.
 */
@Entity
@Table(name = "account_settings")
public class UserSettingsEntity {

    @Id
    @Column(name = "account_id", length = 32)
    private String accountId;

    /** 봉투(envelope) 버전 — 현재 지원 값은 1 하나다 (api.md 7절). */
    @Column(name = "envelope_version", nullable = false)
    private int envelopeVersion;

    /** 클라이언트 스키마 버전(metadata.settingsVersion). 서버는 음수만 거부하고 해석하지 않는다. */
    @Column(name = "settings_version", nullable = false)
    private long settingsVersion;

    /** LWW 비교 1순위. 밀리초로 절삭해 저장·비교한다. */
    @Column(name = "updated_at", nullable = false)
    private Instant updatedAt;

    /** LWW 비교 2순위(같은 updatedAt 의 결정적 타이브레이커). 불투명한 설치 식별자다. */
    @Column(name = "device_id", nullable = false, length = 128)
    private String deviceId;

    /** 검증을 통과한 정본 JSON 문서 원문. 알 수 없는 category/option 필드도 그대로 들어 있다. */
    @Column(name = "document", nullable = false, length = 65536)
    private String document;

    /** 서버가 이 정본을 받아들인 시각. 진단용이며 LWW 비교에는 쓰지 않는다. */
    @Column(name = "stored_at", nullable = false)
    private Instant storedAt;

    protected UserSettingsEntity() {
        // JPA 전용
    }

    UserSettingsEntity(String accountId, ValidatedSettings settings, Instant storedAt) {
        this.accountId = accountId;
        this.envelopeVersion = settings.envelopeVersion();
        this.settingsVersion = settings.settingsVersion();
        this.updatedAt = settings.updatedAt();
        this.deviceId = settings.deviceId();
        this.document = settings.document();
        this.storedAt = storedAt;
    }

    /** LWW 비교에서 들어온 문서가 이긴 경우에만 호출한다. */
    void replaceWith(ValidatedSettings settings, Instant storedAt) {
        this.envelopeVersion = settings.envelopeVersion();
        this.settingsVersion = settings.settingsVersion();
        this.updatedAt = settings.updatedAt();
        this.deviceId = settings.deviceId();
        this.document = settings.document();
        this.storedAt = storedAt;
    }

    public String accountId() {
        return accountId;
    }

    public int envelopeVersion() {
        return envelopeVersion;
    }

    public long settingsVersion() {
        return settingsVersion;
    }

    public Instant updatedAt() {
        return updatedAt;
    }

    public String deviceId() {
        return deviceId;
    }

    public String document() {
        return document;
    }

    public Instant storedAt() {
        return storedAt;
    }
}
