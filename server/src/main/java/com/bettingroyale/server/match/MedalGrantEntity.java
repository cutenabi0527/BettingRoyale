package com.bettingroyale.server.match;

import java.io.Serializable;
import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;

/**
 * 지급 원장 1건. PK = 멱등키 (matchId, accountId) — 계약(api.md 1절)의 중복 지급 방지를
 * UNIQUE 제약으로 강제한다. 스키마 원본은 Flyway V2.
 */
@Entity
@Table(name = "medal_grants")
@IdClass(MedalGrantEntity.Key.class)
public class MedalGrantEntity {

    /** 복합 PK — 필드명은 엔티티와 일치해야 한다. */
    public record Key(String matchId, String accountId) implements Serializable {
    }

    @Id
    @Column(name = "match_id", length = 36)
    private String matchId;

    @Id
    @Column(name = "account_id", length = 32)
    private String accountId;

    /** RANK 는 MySQL 8 예약어라 컬럼명을 피했다. */
    @Column(name = "player_rank", nullable = false)
    private int playerRank;

    @Column(name = "medals_awarded", nullable = false)
    private long medalsAwarded;

    /** 지급 시점 스냅샷. 응답의 newBalance 는 항상 현재 잔액이다 (api.md 3-3). */
    @Column(name = "balance_after", nullable = false)
    private long balanceAfter;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    /**
     * JPA 전용 — 이 엔티티는 <b>읽기 전용</b>이다.
     *
     * <p>행 생성은 {@link MedalGrantRepository#insertGrant} 의 명시적 INSERT 로만 한다.
     * {@code save()} 는 merge 라 기존 지급 기록을 UPDATE 로 덮어써 멱등키 PK 방어선을
     * 무력화한다(D-083). 그래서 공개 생성자를 두지 않는다.</p>
     */
    protected MedalGrantEntity() {
        // JPA 전용
    }

    public String accountId() {
        return accountId;
    }

    public int playerRank() {
        return playerRank;
    }

    public long medalsAwarded() {
        return medalsAwarded;
    }

    public long balanceAfter() {
        return balanceAfter;
    }
}
