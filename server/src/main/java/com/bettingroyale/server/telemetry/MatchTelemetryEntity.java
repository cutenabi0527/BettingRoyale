package com.bettingroyale.server.telemetry;

import java.io.Serializable;
import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;

/**
 * 매치 1건의 지표. PK = (matchId, source) — 같은 매치를 host 와 client 가 각각 보고할 수 있다.
 * 스키마 원본은 Flyway V9, 계약은 docs/server/api.md 9절.
 *
 * <p><b>Host 우선은 읽을 때 고른다</b>(S-GD-GSP9RQ 7-2). source 가 PK 에 있어 두 행이 공존하고,
 * 집계가 {@code source = 'host'} 를 먼저 본다. UPSERT 를 쓰지 않는 이유는 MySQL 8 과 H2 의
 * 동작이 갈리기 때문이다 — V7 이 그 함정으로 preview 배포를 막았다(S15P21A606-221).</p>
 */
@Entity
@Table(name = "match_telemetry")
@IdClass(MatchTelemetryEntity.Key.class)
public class MatchTelemetryEntity {

    /** 복합 PK — 필드명은 엔티티와 일치해야 한다. */
    public record Key(String matchId, String source) implements Serializable {
    }

    @Id
    @Column(name = "match_id", length = 36)
    private String matchId;

    @Id
    @Column(name = "source", length = 8)
    private String source;

    @Column(name = "reported_by", nullable = false, length = 32)
    private String reportedBy;

    @Column(name = "schema_version", nullable = false)
    private int schemaVersion;

    @Column(name = "map_id", nullable = false, length = 64)
    private String mapId;

    @Column(name = "player_count", nullable = false)
    private int playerCount;

    @Column(name = "round_count", nullable = false)
    private int roundCount;

    @Column(name = "ended_reason", nullable = false, length = 32)
    private String endedReason;

    @Column(name = "build_version", nullable = false, length = 64)
    private String buildVersion;

    @Column(name = "data_version", nullable = false, length = 64)
    private String dataVersion;

    @Column(name = "started_at", nullable = false)
    private Instant startedAt;

    /** 방이 중간에 터지면 채워지지 않는다. */
    @Column(name = "ended_at")
    private Instant endedAt;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    /**
     * JPA 전용 — 이 엔티티는 <b>읽기 전용</b>이다.
     *
     * <p>행 생성은 {@link MatchTelemetryRepository#insertMatch} 의 명시적 INSERT 로만 한다.
     * {@code save()} 는 merge 라 기존 보고를 UPDATE 로 덮어써 PK 방어선을 무력화한다(D-083).
     * 그래서 공개 생성자를 두지 않는다.</p>
     */
    protected MatchTelemetryEntity() {
        // JPA 전용
    }

    public String matchId() {
        return matchId;
    }

    public String source() {
        return source;
    }
}
