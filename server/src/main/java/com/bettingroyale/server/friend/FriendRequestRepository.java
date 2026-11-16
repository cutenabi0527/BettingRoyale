package com.bettingroyale.server.friend;

import java.util.List;
import java.util.Optional;

import org.springframework.data.jpa.repository.JpaRepository;

public interface FriendRequestRepository extends JpaRepository<FriendRequestEntity, Long> {

    Optional<FriendRequestEntity> findByRequesterAccountIdAndAddresseeAccountId(
            String requesterAccountId, String addresseeAccountId);

    List<FriendRequestEntity> findByAddresseeAccountIdOrderByCreatedAtDesc(String addresseeAccountId);

    List<FriendRequestEntity> findByRequesterAccountIdOrderByCreatedAtDesc(String requesterAccountId);

    /** 친구가 되거나 계정이 사라질 때 양방향 대기 요청을 함께 정리한다. */
    void deleteByRequesterAccountIdAndAddresseeAccountId(String requesterAccountId, String addresseeAccountId);
}
