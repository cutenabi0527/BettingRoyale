package com.bettingroyale.server.account;

import java.io.Serializable;
import java.time.Instant;
import java.util.Objects;

import jakarta.persistence.Column;
import jakarta.persistence.Embeddable;
import jakarta.persistence.EmbeddedId;
import jakarta.persistence.Entity;
import jakarta.persistence.Table;

/**
 * 계정이 소유한 프로필 초상화 한 줄 (S-UX-RQ5N9E, V5).
 *
 * <p>계정 열에 쉼표 문자열로 담지 않는 이유는 지급·회수를 행 단위로 다루기 위해서다.
 */
@Entity
@Table(name = "account_portraits")
public class AccountPortraitEntity {

    @Embeddable
    public static class Key implements Serializable {

        @Column(name = "account_id", length = 32, nullable = false)
        private String accountId;

        @Column(name = "portrait_id", length = 16, nullable = false)
        private String portraitId;

        protected Key() {
            // JPA 전용
        }

        public Key(String accountId, String portraitId) {
            this.accountId = accountId;
            this.portraitId = portraitId;
        }

        public String accountId() {
            return accountId;
        }

        public String portraitId() {
            return portraitId;
        }

        @Override
        public boolean equals(Object other) {
            if (this == other) {
                return true;
            }
            if (!(other instanceof Key key)) {
                return false;
            }
            return Objects.equals(accountId, key.accountId) && Objects.equals(portraitId, key.portraitId);
        }

        @Override
        public int hashCode() {
            return Objects.hash(accountId, portraitId);
        }
    }

    @EmbeddedId
    private Key id;

    @Column(name = "granted_at", nullable = false)
    private Instant grantedAt;

    /**
     * JPA 전용 — 이 엔티티는 <b>읽기 전용</b>이다.
     *
     * <p>지급은 {@link AccountPortraitRepository#insertGrant} 의 명시적 INSERT 로만 한다.
     * PK 를 직접 지정하는 엔티티에 {@code save()} 를 쓰면 merge 가 되어 행이 있을 때 UPDATE 로
     * 조용히 성공한다(D-BE-SG4AD6 / 레거시 D-083). 그래서 공개 생성자를 두지 않는다 —
     * {@code save(new ...)} 경로 자체를 막는 것이 그 결정의 절반이다.
     */
    protected AccountPortraitEntity() {
        // JPA 전용
    }

    public String accountId() {
        return id.accountId();
    }

    public String portraitId() {
        return id.portraitId();
    }

    public Instant grantedAt() {
        return grantedAt;
    }
}
