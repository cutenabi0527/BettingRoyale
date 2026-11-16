package com.bettingroyale.server.wardrobe;

import java.io.Serializable;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;

/**
 * 프리셋의 슬롯 한 칸 (V6).
 *
 * <p>슬롯이 기본값이면 행이 없다. Costume 은 점유하는 슬롯 수만큼 <b>같은 {@code itemId} 가 여러 행</b>으로
 * 들어간다 — 점유 규칙을 서버가 해석하지 않고 클라이언트가 보낸 그대로 보관하기 때문이다(D-BE-Z2HMR6).
 */
@Entity
@Table(name = "account_wardrobe_preset_items")
@IdClass(WardrobePresetItemEntity.Key.class)
public class WardrobePresetItemEntity {

    /** 복합 PK — 필드명은 엔티티와 일치해야 한다. */
    public record Key(String accountId, String weaponType, String slot) implements Serializable {
    }

    @Id
    @Column(name = "account_id", length = 32)
    private String accountId;

    @Id
    @Column(name = "weapon_type", length = 16)
    private String weaponType;

    /** {@link WardrobeSlot#contractName()} — 소문자 계약 이름 그대로다. */
    @Id
    @Column(name = "slot", length = WardrobeSlot.MAX_NAME_LENGTH)
    private String slot;

    @Column(name = "item_id", nullable = false, length = 48)
    private String itemId;

    protected WardrobePresetItemEntity() {
        // JPA 전용
    }

    /** 신규 행 — {@code save()} 가 아니라 {@code EntityManager.persist} 로만 만든다(D-BE-SG4AD6). */
    WardrobePresetItemEntity(String accountId, String weaponType, WardrobeSlot slot, String itemId) {
        this.accountId = accountId;
        this.weaponType = weaponType;
        this.slot = slot.contractName();
        this.itemId = itemId;
    }

    public String slot() {
        return slot;
    }

    public String weaponType() {
        return weaponType;
    }

    public String itemId() {
        return itemId;
    }
}
