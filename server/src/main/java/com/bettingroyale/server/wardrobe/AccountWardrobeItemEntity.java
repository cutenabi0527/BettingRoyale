package com.bettingroyale.server.wardrobe;

import java.io.Serializable;
import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;

/**
 * 계정이 소유한 외형 아이템 한 줄 (V6, S-UX-F3CJEP).
 *
 * <p>계정 열에 쉼표 문자열로 담지 않는 이유는 지급·회수를 행 단위로 다루기 위해서다
 * ({@code account_portraits} 와 같은 이유, S-UX-RQ5N9E).
 */
@Entity
@Table(name = "account_wardrobe_items")
@IdClass(AccountWardrobeItemEntity.Key.class)
public class AccountWardrobeItemEntity {

    /** 복합 PK — 필드명은 엔티티와 일치해야 한다. */
    public record Key(String accountId, String itemId) implements Serializable {
    }

    @Id
    @Column(name = "account_id", length = 32)
    private String accountId;

    @Id
    @Column(name = "item_id", length = 48)
    private String itemId;

    @Column(name = "granted_at", nullable = false)
    private Instant grantedAt;

    /**
     * JPA 전용 — 이 엔티티는 <b>읽기 전용</b>이다.
     *
     * <p>지급은 {@link AccountWardrobeItemRepository#insertDefaultGrants} 의 명시적 INSERT 로만 한다.
     * PK 를 직접 지정하는 엔티티에 {@code save()} 를 쓰면 merge 가 되어 행이 있을 때 UPDATE 로
     * 조용히 성공한다(D-BE-SG4AD6 / 레거시 D-083). 그래서 공개 생성자를 두지 않는다.
     */
    protected AccountWardrobeItemEntity() {
        // JPA 전용
    }

    public String accountId() {
        return accountId;
    }

    public String itemId() {
        return itemId;
    }

    public Instant grantedAt() {
        return grantedAt;
    }
}
