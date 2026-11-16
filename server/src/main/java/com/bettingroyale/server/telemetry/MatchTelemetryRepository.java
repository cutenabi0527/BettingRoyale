package com.bettingroyale.server.telemetry;

import java.time.Instant;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface MatchTelemetryRepository
        extends JpaRepository<MatchTelemetryEntity, MatchTelemetryEntity.Key> {

    /**
     * 매치 지표를 <b>INSERT 로만</b> 기록한다 (D-083).
     *
     * <p>⚠️ {@code save()} 를 쓰면 안 된다. PK 를 우리가 지정하는 엔티티라 Spring Data 는
     * merge 를 부르고, merge 는 행이 있으면 UPDATE 한다 — 재전송이 기존 보고를 덮어쓴다.</p>
     */
    @Modifying
    @Query(value = "INSERT INTO match_telemetry "
            + "(match_id, source, reported_by, schema_version, map_id, player_count, round_count, "
            + " ended_reason, build_version, data_version, started_at, ended_at, created_at) "
            + "VALUES (:matchId, :source, :reportedBy, :schemaVersion, :mapId, :playerCount, :roundCount, "
            + " :endedReason, :buildVersion, :dataVersion, :startedAt, :endedAt, :createdAt)",
            nativeQuery = true)
    int insertMatch(
            @Param("matchId") String matchId,
            @Param("source") String source,
            @Param("reportedBy") String reportedBy,
            @Param("schemaVersion") int schemaVersion,
            @Param("mapId") String mapId,
            @Param("playerCount") int playerCount,
            @Param("roundCount") int roundCount,
            @Param("endedReason") String endedReason,
            @Param("buildVersion") String buildVersion,
            @Param("dataVersion") String dataVersion,
            @Param("startedAt") Instant startedAt,
            @Param("endedAt") Instant endedAt,
            @Param("createdAt") Instant createdAt);
}
