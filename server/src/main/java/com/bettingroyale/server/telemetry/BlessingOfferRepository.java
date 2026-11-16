package com.bettingroyale.server.telemetry;

import java.time.Instant;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface BlessingOfferRepository
        extends JpaRepository<BlessingOfferEntity, BlessingOfferEntity.Key> {

    /** 축복 제시를 <b>INSERT 로만</b> 기록한다 (D-083 — {@code save()} 는 merge 라 덮어쓴다). */
    @Modifying
    @Query(value = "INSERT INTO blessing_offers "
            + "(match_id, account_id, round_no, blessing_id, source, picked, reroll_count, "
            + " reroll_gold_spent, decision_millis, created_at) "
            + "VALUES (:matchId, :accountId, :roundNo, :blessingId, :source, :picked, :rerollCount, "
            + " :rerollGoldSpent, :decisionMillis, :createdAt)",
            nativeQuery = true)
    int insertOffer(
            @Param("matchId") String matchId,
            @Param("accountId") String accountId,
            @Param("roundNo") int roundNo,
            @Param("blessingId") String blessingId,
            @Param("source") String source,
            @Param("picked") int picked,
            @Param("rerollCount") int rerollCount,
            @Param("rerollGoldSpent") long rerollGoldSpent,
            @Param("decisionMillis") Integer decisionMillis,
            @Param("createdAt") Instant createdAt);
}
