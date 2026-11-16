package com.bettingroyale.server.telemetry;

import java.time.Instant;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface MatchParticipantRepository
        extends JpaRepository<MatchParticipantEntity, MatchParticipantEntity.Key> {

    /** 참가자 지표를 <b>INSERT 로만</b> 기록한다 (D-083 — {@code save()} 는 merge 라 덮어쓴다). */
    @Modifying
    @Query(value = "INSERT INTO match_participants "
            + "(match_id, account_id, source, weapon, final_rank, final_score, kills, deaths, assists, "
            + " damage_dealt, damage_taken, gold_earned, gold_spent, survival_seconds, created_at) "
            + "VALUES (:matchId, :accountId, :source, :weapon, :finalRank, :finalScore, :kills, :deaths, :assists, "
            + " :damageDealt, :damageTaken, :goldEarned, :goldSpent, :survivalSeconds, :createdAt)",
            nativeQuery = true)
    int insertParticipant(
            @Param("matchId") String matchId,
            @Param("accountId") String accountId,
            @Param("source") String source,
            @Param("weapon") String weapon,
            @Param("finalRank") int finalRank,
            @Param("finalScore") int finalScore,
            @Param("kills") int kills,
            @Param("deaths") int deaths,
            @Param("assists") int assists,
            @Param("damageDealt") long damageDealt,
            @Param("damageTaken") long damageTaken,
            @Param("goldEarned") long goldEarned,
            @Param("goldSpent") long goldSpent,
            @Param("survivalSeconds") int survivalSeconds,
            @Param("createdAt") Instant createdAt);
}
