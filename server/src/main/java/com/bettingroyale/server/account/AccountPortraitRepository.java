package com.bettingroyale.server.account;

import java.time.Instant;
import java.util.List;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

/** 계정별 프로필 초상화 소유 (S-UX-RQ5N9E, V5). */
public interface AccountPortraitRepository
        extends JpaRepository<AccountPortraitEntity, AccountPortraitEntity.Key> {

    @Query("select p.id.portraitId from AccountPortraitEntity p where p.id.accountId = :accountId")
    List<String> findPortraitIds(@Param("accountId") String accountId);

    boolean existsById(AccountPortraitEntity.Key id);

    /**
     * 초상화 한 장을 <b>명시적 INSERT</b> 로 지급한다 (D-BE-SG4AD6 / 레거시 D-083).
     *
     * <p>{@code save()} 를 쓰지 않는 이유는 그 결정 그대로다 — PK 를 우리가 지정하는 엔티티에서
     * {@code save()} 는 {@code merge} 가 되어 <b>행이 있으면 UPDATE 로 조용히 성공</b>한다.
     * 그러면 "PK 충돌이 동시 삽입을 직렬화한다"는 전제가 무너진다. 메달 이중 지급이 그렇게 났다.
     *
     * <p>{@code NOT EXISTS} 는 <b>재호출 멱등성</b>을 위한 것이지 동시성 방어선이 아니다.
     * 두 호출이 같은 틈에 끼어들면 PK 가 제약 위반으로 막는다 — 그것이 의도한 방어선이고,
     * {@code merge} 와 달리 조용히 UPDATE 로 넘어가지 않는다.
     *
     * <p>{@code FROM accounts} 인 이유는 두 가지다. (1) MySQL 은 {@code FROM} 없는 {@code WHERE} 를
     * 허용하지 않으므로 한 행짜리 원천이 필요하다. (2) 계정이 없으면 0행이라 FK 위반 대신
     * "아무것도 지급하지 않음"이 된다.
     *
     * @return 실제로 넣은 행 수 (이미 소유 중이면 0)
     */
    @Modifying
    @Query(value = "INSERT INTO account_portraits (account_id, portrait_id, granted_at) "
            + "SELECT :accountId, :portraitId, :grantedAt FROM accounts a "
            + "WHERE a.account_id = :accountId "
            + "AND NOT EXISTS (SELECT 1 FROM account_portraits p "
            + "WHERE p.account_id = :accountId AND p.portrait_id = :portraitId)", nativeQuery = true)
    int insertGrant(@Param("accountId") String accountId,
            @Param("portraitId") String portraitId,
            @Param("grantedAt") Instant grantedAt);
}
