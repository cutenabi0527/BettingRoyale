package com.bettingroyale.server.match;

import java.time.Instant;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface MatchResultRepository extends JpaRepository<MatchResultEntity, String> {

    /**
     * 최초 제출 서명을 <b>INSERT 로만</b> 기록한다. 이미 있으면 PK 충돌로 실패한다 —
     * 그 실패가 동시 최초 제출의 직렬화 장치다(MedalLedgerStore 참조).
     *
     * <p>⚠️ {@code save()} 를 쓰면 안 된다. PK 를 우리가 지정하는 엔티티라 Spring Data 는 이것을
     * "새 것이 아님"으로 보고 {@code merge} 를 부르는데, merge 는 <b>행이 있으면 UPDATE</b> 한다.
     * 그래서 경합에 진 제출이 PK 충돌 없이 승자의 서명을 덮어쓰고 잔액을 이중 지급했다
     * (2026-09-09 실측, D-083).</p>
     */
    @Modifying
    @Query(value = "INSERT INTO match_results (match_id, submitter_account_id, created_at) "
            + "VALUES (:matchId, :submitterAccountId, :createdAt)", nativeQuery = true)
    int insertSignature(
            @Param("matchId") String matchId,
            @Param("submitterAccountId") String submitterAccountId,
            @Param("createdAt") Instant createdAt);
}
