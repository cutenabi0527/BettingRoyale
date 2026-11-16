package com.bettingroyale.server.wardrobe;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/**
 * 외형 카탈로그 1건 (V6). 서버가 "존재하는 아이템"을 아는 유일한 근거다.
 *
 * <p>ID 는 공급사 prefab 이름 그대로이며 클라이언트 공용 카탈로그(S-CLI-RQB4QD)와 <b>같은 ID 집합</b>이어야
 * 한다. 한쪽만 늘리면 새 아이템을 저장할 수 없거나(400) 화면에 빈 칸이 뜬다.
 *
 * <p>{@code granted_by_default} 열은 <b>일부러 매핑하지 않는다</b> — 기본 지급은 행을 한 줄씩 읽어 판단하는
 * 값이 아니라 {@link AccountWardrobeItemRepository#insertDefaultGrants} 의 한 문장이 소비하는 조건이다.
 * 매핑하지 않으면 boolean ↔ TINYINT 의 방언 차이가 {@code ddl-auto: validate} 를 건드릴 일도 없다.
 */
@Entity
@Table(name = "wardrobe_items")
public class WardrobeItemEntity {

    @Id
    @Column(name = "item_id", length = 48)
    private String itemId;

    /** BODY / FACE / OUTFIT / COSTUME — 진단·운영용 분류다. 서버는 이것으로 저장을 거절하지 않는다. */
    @Column(name = "category", nullable = false, length = 16)
    private String category;

    protected WardrobeItemEntity() {
        // JPA 전용 — 카탈로그 행은 Flyway migration 만 만든다.
    }

    public String itemId() {
        return itemId;
    }

    /** 메달 가격 (V7). 0 은 기본 지급이라 살 필요가 없다는 뜻이다 (S-GD-5R953J). */
    @Column(name = "price_medals", nullable = false)
    private int priceMedals;

    public String category() {
        return category;
    }

    public int priceMedals() {
        return priceMedals;
    }
}
