package com.bettingroyale.server.wardrobe;

import java.time.Instant;
import java.util.List;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

/** 계정별 외형 아이템 소유 (V6). */
public interface AccountWardrobeItemRepository
        extends JpaRepository<AccountWardrobeItemEntity, AccountWardrobeItemEntity.Key> {

    /** ID 오름차순 — 표시 순서는 클라이언트 카탈로그가 정한다(api.md 8-3). 서버가 두 번째 정본을 갖지 않는다. */
    @Query("select o.itemId from AccountWardrobeItemEntity o where o.accountId = :accountId order by o.itemId asc")
    List<String> findOwnedItemIds(@Param("accountId") String accountId);

    /**
     * 기본 지급분을 <b>한 문장의 명시적 INSERT</b> 로 넣는다 (S-GD-ZVME43: 현재 카탈로그 전량).
     *
     * <p>{@code save()} 를 쓰지 않는 이유는 D-BE-SG4AD6(레거시 D-083) 그대로다 — PK 를 우리가 지정하는
     * 엔티티에서 {@code save()} 는 merge 라 행이 있으면 UPDATE 로 성공한다.
     *
     * <p>지급 목록의 정본은 {@code wardrobe_items.granted_by_default} 한 곳이다. 서비스 코드가 목록을
     * 따로 들고 있으면 마이그레이션이 넣은 것과 가입이 주는 것이 갈라진다.
     */
    @Modifying
    @Query(value = "INSERT INTO account_wardrobe_items (account_id, item_id, granted_at) "
            + "SELECT :accountId, w.item_id, :grantedAt FROM wardrobe_items w "
            + "WHERE w.granted_by_default = 1", nativeQuery = true)
    int insertDefaultGrants(@Param("accountId") String accountId, @Param("grantedAt") Instant grantedAt);

    /**
     * 산 의상 한 벌을 <b>명시적 INSERT</b> 로 지급한다 (D-BE-SG4AD6 / 레거시 D-083).
     *
     * <p>{@code save()} 를 쓰지 않는 이유는 그 결정 그대로다 — PK 를 우리가 지정하는 엔티티에서
     * {@code save()} 는 merge 라 행이 있으면 UPDATE 로 조용히 성공한다.
     *
     * <p>{@code NOT EXISTS} 는 재호출 멱등성을 위한 것이다. 이미 소유 중이면 0 을 돌려주고,
     * 호출자는 <b>그때 메달을 빼지 않는다</b> — 같은 옷을 두 번 사게 두지 않는다.
     *
     * @return 실제로 넣은 행 수 (이미 소유 중이면 0)
     */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query(value = "INSERT INTO account_wardrobe_items (account_id, item_id, granted_at) "
            + "SELECT :accountId, :itemId, :grantedAt FROM accounts a "
            + "WHERE a.account_id = :accountId "
            + "AND NOT EXISTS (SELECT 1 FROM account_wardrobe_items o "
            + "WHERE o.account_id = :accountId AND o.item_id = :itemId)", nativeQuery = true)
    int insertPurchase(@Param("accountId") String accountId,
            @Param("itemId") String itemId,
            @Param("grantedAt") Instant grantedAt);
}
