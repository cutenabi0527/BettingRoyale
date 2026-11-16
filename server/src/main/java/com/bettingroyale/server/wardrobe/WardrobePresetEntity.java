package com.bettingroyale.server.wardrobe;

import java.io.Serializable;
import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;

/**
 * 한 계정·한 무기 유형의 외형 프리셋 머리 (V6, S-GD-5NRMCY).
 *
 * <p><b>행이 있다는 것 자체가 "한 번이라도 저장했다"는 뜻이다.</b> 그래서 {@code revision} 은 1 부터
 * 시작한다 — 행이 없으면 조회가 revision 0 을 만들어 주고, 0 인 동안 클라이언트는 계속
 * {@code appearancePresetSeed} 로 외형을 만든다(api.md 8-2). 이 구분이 없으면 "모든 슬롯을 기본으로
 * 저장한 사용자"의 선택이 매번 seed 외형으로 되돌아간다.
 *
 * <p>{@code revision} 은 서버가 발급하며 요청에서 받지 않는다. 조건부 쓰기는 요구하지 않는다 —
 * 동시 로그인이 금지라 같은 계정의 동시 편집자가 계약상 없다(D-BE-5DNW6H).
 */
@Entity
@Table(name = "account_wardrobe_presets")
@IdClass(WardrobePresetEntity.Key.class)
public class WardrobePresetEntity {

    /** 복합 PK — 필드명은 엔티티와 일치해야 한다. */
    public record Key(String accountId, String weaponType) implements Serializable {
    }

    @Id
    @Column(name = "account_id", length = 32)
    private String accountId;

    /** 설정(wardrobe.weapon-types)의 정본 표기로만 저장한다 — 현재 Sword / Bow. */
    @Id
    @Column(name = "weapon_type", length = 16)
    private String weaponType;

    @Column(name = "revision", nullable = false)
    private long revision;

    /** DB 정밀도(DATETIME(3))와 맞추려고 밀리초로 절삭해 저장한다 — 저장값과 응답값이 같아야 한다. */
    @Column(name = "updated_at", nullable = false)
    private Instant updatedAt;

    protected WardrobePresetEntity() {
        // JPA 전용
    }

    /**
     * 신규 프리셋. <b>{@code save()} 가 아니라 {@code EntityManager.persist} 로만</b> 기록한다 —
     * PK 를 직접 지정하는 엔티티에서 {@code save()} 는 merge 라 행이 있으면 UPDATE 로 성공한다
     * (D-BE-SG4AD6 / 레거시 D-083). {@link WardrobeStore} 가 계정 행 잠금 아래에서 호출한다.
     */
    WardrobePresetEntity(String accountId, String weaponType, Instant updatedAt) {
        this.accountId = accountId;
        this.weaponType = weaponType;
        this.revision = 1L;
        this.updatedAt = updatedAt;
    }

    /** 내용이 실제로 바뀐 저장에만 부른다. 같은 내용 재저장은 revision 을 올리지 않는다(api.md 8-4). */
    void markChanged(Instant updatedAt) {
        this.revision += 1L;
        this.updatedAt = updatedAt;
    }

    public String accountId() {
        return accountId;
    }

    public String weaponType() {
        return weaponType;
    }

    public long revision() {
        return revision;
    }

    public Instant updatedAt() {
        return updatedAt;
    }
}
