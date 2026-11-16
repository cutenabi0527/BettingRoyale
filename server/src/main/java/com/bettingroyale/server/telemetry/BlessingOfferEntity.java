package com.bettingroyale.server.telemetry;

import java.io.Serializable;
import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;

/**
 * 제시된 축복 <b>1건 = 1행</b>. PK = (matchId, accountId, roundNo, blessingId, source).
 * 스키마 원본은 Flyway V9.
 *
 * <p><b>왜 목록이 아니라 행인가.</b> 제시 목록을 CSV·JSON 으로 담으면 픽률 집계에
 * {@code FIND_IN_SET} 이나 JSON 함수가 필요한데 둘 다 MySQL/H2 호환이 위태롭다.
 * 행으로 펴면 픽률이 순수 SQL 집계가 된다 — 분모는 {@code COUNT(*)}, 분자는
 * {@code SUM(CASE WHEN picked = 1 ...)} 다 (S-GD-GSP9RQ 4절).</p>
 */
@Entity
@Table(name = "blessing_offers")
@IdClass(BlessingOfferEntity.Key.class)
public class BlessingOfferEntity {

    /** 복합 PK — 필드명은 엔티티와 일치해야 한다. */
    public record Key(String matchId, String accountId, Integer roundNo, String blessingId, String source)
            implements Serializable {
    }

    @Id
    @Column(name = "match_id", length = 36)
    private String matchId;

    @Id
    @Column(name = "account_id", length = 32)
    private String accountId;

    /** 1..3. 등급은 뽑는 것이 아니라 라운드로 정해진다 (S-GD-HE87JY). */
    @Id
    @Column(name = "round_no")
    private Integer roundNo;

    @Id
    @Column(name = "blessing_id", length = 64)
    private String blessingId;

    @Id
    @Column(name = "source", length = 8)
    private String source;

    /**
     * 0 | 1. 컬럼은 {@code TINYINT} 다 — BOOLEAN 은 H2/MySQL 표기가 갈려 쓰지 않았다(V9).
     *
     * <p>⚠️ 자바 타입이 {@code byte} 인 이유는 Hibernate 스키마 검증이 타입을 정확히 맞추라고
     * 요구하기 때문이다. {@code int} 로 두면 <i>"found [tinyint], but expecting [integer]"</i> 로
     * 컨텍스트 자체가 뜨지 않는다(2026-09-21 실측). {@code boolean} 도 안 된다 — 방언마다
     * BOOLEAN/BIT 로 갈린다.</p>
     */
    @Column(name = "picked", nullable = false)
    private byte picked;

    @Column(name = "reroll_count", nullable = false)
    private int rerollCount;

    @Column(name = "reroll_gold_spent", nullable = false)
    private long rerollGoldSpent;

    /** 클라만 알 수 있어 host 보고에는 없다 (S-GD-GSP9RQ 7-2). */
    @Column(name = "decision_millis")
    private Integer decisionMillis;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    /** JPA 전용 — 읽기 전용 엔티티다. 행 생성은 명시적 INSERT 로만 한다 (D-083). */
    protected BlessingOfferEntity() {
        // JPA 전용
    }

    public String blessingId() {
        return blessingId;
    }

    public boolean isPicked() {
        return picked != 0;
    }
}
