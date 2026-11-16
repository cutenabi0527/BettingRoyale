package com.bettingroyale.server.account;

import java.util.Optional;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface AccountRepository extends JpaRepository<AccountEntity, String> {

    Optional<AccountEntity> findByLoginId(String loginId);

    Optional<AccountEntity> findByNickname(String nickname);

    boolean existsByLoginId(String loginId);

    boolean existsByNickname(String nickname);

    /**
     * 잔액 원자 증가 — 지급 트랜잭션 안에서만 호출한다 (MedalLedgerStore).
     * read-modify-write 대신 DB 가 더하게 해서 동시 매치 지급이 서로를 덮어쓰지 않는다.
     */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query("update AccountEntity a set a.medalBalance = a.medalBalance + :amount where a.accountId = :accountId")
    int addToBalance(@Param("accountId") String accountId, @Param("amount") long amount);

    /**
     * 잔액이 충분할 때만 차감한다 — 외형 구매의 지불 단계 (S-GD-5R953J).
     *
     * <p>{@code >= :amount} 조건을 <b>UPDATE 문 안에</b> 둔 것이 핵심이다. 잔액을 먼저 읽고 자바에서
     * 비교한 뒤 빼면, 두 구매가 같은 틈에 끼어들었을 때 둘 다 통과해 <b>잔액이 음수가 된다</b>.
     * 지급이 {@link #addToBalance} 로 DB 에게 더하게 시키는 것과 같은 이유다.
     *
     * @return 1 이면 차감 성공, 0 이면 잔액 부족(또는 계정 없음)
     */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query("update AccountEntity a set a.medalBalance = a.medalBalance - :amount "
            + "where a.accountId = :accountId and a.medalBalance >= :amount")
    int deductIfAffordable(@Param("accountId") String accountId, @Param("amount") long amount);
}
