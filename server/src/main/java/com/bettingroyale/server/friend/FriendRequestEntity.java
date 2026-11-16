package com.bettingroyale.server.friend;

import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/**
 * 대기 중인 친구 요청. 스키마 원본은 Flyway V8 — 여기서는 매핑만 한다.
 *
 * <p><b>수락·거절되면 행을 지운다.</b> 상태 컬럼을 두지 않는 이유는 "처리된 요청" 의 수명·재요청 규칙을
 * 아직 정하지 않았기 때문이다 (docs/server/api.md 3-16). 이력이 필요해지면 그때 보존 정책과 함께 정한다.
 */
@Entity
@Table(name = "friend_requests")
public class FriendRequestEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @Column(name = "request_id")
    private Long requestId;

    @Column(name = "requester_account_id", nullable = false, length = 32)
    private String requesterAccountId;

    @Column(name = "addressee_account_id", nullable = false, length = 32)
    private String addresseeAccountId;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    protected FriendRequestEntity() {
    }

    public FriendRequestEntity(String requesterAccountId, String addresseeAccountId, Instant createdAt) {
        this.requesterAccountId = requesterAccountId;
        this.addresseeAccountId = addresseeAccountId;
        this.createdAt = createdAt;
    }

    public Long requestId() {
        return requestId;
    }

    public String requesterAccountId() {
        return requesterAccountId;
    }

    public String addresseeAccountId() {
        return addresseeAccountId;
    }

    public Instant createdAt() {
        return createdAt;
    }
}
