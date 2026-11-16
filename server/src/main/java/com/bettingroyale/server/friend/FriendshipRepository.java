package com.bettingroyale.server.friend;

import java.util.List;
import java.util.Optional;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface FriendshipRepository
        extends JpaRepository<FriendshipEntity, FriendshipEntity.FriendshipId> {

    /**
     * 내가 속한 모든 관계. 대칭 관계를 한 행으로 저장하므로(V8) 두 컬럼 어느 쪽에서든 찾아야 한다.
     * 정렬은 서비스가 닉네임 기준으로 다시 한다 — 여기서는 id 를 모으는 것이 목적이다.
     */
    @Query("select f from FriendshipEntity f "
            + "where f.id.accountIdLow = :accountId or f.id.accountIdHigh = :accountId")
    List<FriendshipEntity> findAllInvolving(@Param("accountId") String accountId);

    default Optional<FriendshipEntity> findBetween(String accountA, String accountB) {
        return findById(FriendshipEntity.FriendshipId.of(accountA, accountB));
    }

    default boolean existsBetween(String accountA, String accountB) {
        return existsById(FriendshipEntity.FriendshipId.of(accountA, accountB));
    }
}
