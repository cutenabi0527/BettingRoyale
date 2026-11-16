package com.bettingroyale.server.friend;

import java.io.Serializable;
import java.time.Instant;
import java.util.Objects;

import jakarta.persistence.Column;
import jakarta.persistence.Embeddable;
import jakarta.persistence.EmbeddedId;
import jakarta.persistence.Entity;
import jakarta.persistence.Table;

/**
 * 성립된 친구 관계. 스키마 원본은 Flyway V8.
 *
 * <p><b>대칭 관계를 한 행으로만 저장한다.</b> {@code low < high} 를 {@link #of} 가 보장하고 PK 가 중복을 막는다.
 * 양방향 두 행으로 저장하면 한쪽만 지워진 상태가 만들어질 수 있다.
 */
@Entity
@Table(name = "friendships")
public class FriendshipEntity {

    @EmbeddedId
    private FriendshipId id;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    protected FriendshipEntity() {
    }

    /** 두 계정 id 의 사전순으로 정렬해 항상 같은 행을 가리키게 한다. */
    public static FriendshipEntity of(String accountA, String accountB, Instant createdAt) {
        FriendshipEntity entity = new FriendshipEntity();
        entity.id = FriendshipId.of(accountA, accountB);
        entity.createdAt = createdAt;
        return entity;
    }

    public FriendshipId id() {
        return id;
    }

    public Instant createdAt() {
        return createdAt;
    }

    /** 이 관계에서 {@code me} 가 아닌 쪽. */
    public String otherThan(String me) {
        return id.accountIdLow().equals(me) ? id.accountIdHigh() : id.accountIdLow();
    }

    @Embeddable
    public static class FriendshipId implements Serializable {

        @Column(name = "account_id_low", nullable = false, length = 32)
        private String accountIdLow;

        @Column(name = "account_id_high", nullable = false, length = 32)
        private String accountIdHigh;

        protected FriendshipId() {
        }

        public static FriendshipId of(String accountA, String accountB) {
            FriendshipId id = new FriendshipId();
            if (accountA.compareTo(accountB) <= 0) {
                id.accountIdLow = accountA;
                id.accountIdHigh = accountB;
            } else {
                id.accountIdLow = accountB;
                id.accountIdHigh = accountA;
            }
            return id;
        }

        public String accountIdLow() {
            return accountIdLow;
        }

        public String accountIdHigh() {
            return accountIdHigh;
        }

        @Override
        public boolean equals(Object other) {
            if (this == other) {
                return true;
            }
            if (!(other instanceof FriendshipId that)) {
                return false;
            }
            return Objects.equals(accountIdLow, that.accountIdLow)
                    && Objects.equals(accountIdHigh, that.accountIdHigh);
        }

        @Override
        public int hashCode() {
            return Objects.hash(accountIdLow, accountIdHigh);
        }
    }
}
