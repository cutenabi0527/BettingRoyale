package com.bettingroyale.server.match;

import java.time.Instant;
import java.util.List;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface MedalGrantRepository extends JpaRepository<MedalGrantEntity, MedalGrantEntity.Key> {

    List<MedalGrantEntity> findByMatchIdOrderByPlayerRankAsc(String matchId);

    /**
     * 지급 원장을 <b>INSERT 로만</b> 기록한다. 멱등키 PK(matchId, accountId)가 중복 지급의
     * 최종 방어선인데, {@code save()}(=merge)는 행이 있으면 UPDATE 로 조용히 덮어써
     * 그 방어선을 무력화한다 — 그래서 여기서도 명시적 INSERT 를 쓴다(D-083).
     */
    @Modifying
    @Query(value = "INSERT INTO medal_grants "
            + "(match_id, account_id, player_rank, medals_awarded, balance_after, created_at) "
            + "VALUES (:matchId, :accountId, :playerRank, :medalsAwarded, :balanceAfter, :createdAt)",
            nativeQuery = true)
    int insertGrant(
            @Param("matchId") String matchId,
            @Param("accountId") String accountId,
            @Param("playerRank") int playerRank,
            @Param("medalsAwarded") long medalsAwarded,
            @Param("balanceAfter") long balanceAfter,
            @Param("createdAt") Instant createdAt);
}
