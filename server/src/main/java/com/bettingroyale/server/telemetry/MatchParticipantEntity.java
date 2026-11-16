package com.bettingroyale.server.telemetry;

import java.io.Serializable;
import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;

/**
 * 참가자 1명의 매치 지표. PK = (matchId, accountId, source). 스키마 원본은 Flyway V9.
 *
 * <p>무기가 매치당 1개 값인 이유는 <b>대기방에서 고르면 매치 내내 고정</b>이기 때문이다
 * (S-GD-GSP9RQ 2절). 그래서 전환 횟수를 세지 않는다.</p>
 */
@Entity
@Table(name = "match_participants")
@IdClass(MatchParticipantEntity.Key.class)
public class MatchParticipantEntity {

    /** 복합 PK — 필드명은 엔티티와 일치해야 한다. */
    public record Key(String matchId, String accountId, String source) implements Serializable {
    }

    @Id
    @Column(name = "match_id", length = 36)
    private String matchId;

    @Id
    @Column(name = "account_id", length = 32)
    private String accountId;

    @Id
    @Column(name = "source", length = 8)
    private String source;

    @Column(name = "weapon", nullable = false, length = 16)
    private String weapon;

    @Column(name = "final_rank", nullable = false)
    private int finalRank;

    @Column(name = "final_score", nullable = false)
    private int finalScore;

    @Column(name = "kills", nullable = false)
    private int kills;

    @Column(name = "deaths", nullable = false)
    private int deaths;

    @Column(name = "assists", nullable = false)
    private int assists;

    @Column(name = "damage_dealt", nullable = false)
    private long damageDealt;

    @Column(name = "damage_taken", nullable = false)
    private long damageTaken;

    @Column(name = "gold_earned", nullable = false)
    private long goldEarned;

    @Column(name = "gold_spent", nullable = false)
    private long goldSpent;

    @Column(name = "survival_seconds", nullable = false)
    private int survivalSeconds;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    /** JPA 전용 — 읽기 전용 엔티티다. 행 생성은 명시적 INSERT 로만 한다 (D-083). */
    protected MatchParticipantEntity() {
        // JPA 전용
    }

    public String accountId() {
        return accountId;
    }

    public String weapon() {
        return weapon;
    }

    public int finalRank() {
        return finalRank;
    }
}
