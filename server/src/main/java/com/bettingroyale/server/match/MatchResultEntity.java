package com.bettingroyale.server.match;

import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/**
 * 매치당 1행. 최초 성공 제출이 제출자·결과 집합을 고정한다 (api.md 3-3).
 * PK(match_id)가 동시 최초 제출을 DB 수준에서 직렬화한다 — 둘 중 하나만 INSERT 에 성공한다.
 * 스키마 원본은 Flyway V2.
 */
@Entity
@Table(name = "match_results")
public class MatchResultEntity {

    @Id
    @Column(name = "match_id", length = 36)
    private String matchId;

    @Column(name = "submitter_account_id", nullable = false, length = 32)
    private String submitterAccountId;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    /**
     * JPA 전용 — 이 엔티티는 <b>읽기 전용</b>이다.
     *
     * <p>행 생성은 {@link MatchResultRepository#insertSignature} 의 명시적 INSERT 로만 한다.
     * 새 인스턴스를 만들어 {@code save()} 하면 merge 가 되어 행이 있을 때 UPDATE 로 성공해버리고,
     * 동시 최초 제출의 직렬화가 무너진다(D-083). 그래서 공개 생성자를 두지 않는다.</p>
     */
    protected MatchResultEntity() {
        // JPA 전용
    }

    public String matchId() {
        return matchId;
    }

    public String submitterAccountId() {
        return submitterAccountId;
    }
}
