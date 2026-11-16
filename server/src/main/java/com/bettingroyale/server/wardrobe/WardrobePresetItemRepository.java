package com.bettingroyale.server.wardrobe;

import java.util.List;

import org.springframework.data.jpa.repository.JpaRepository;

/** 프리셋 슬롯 행 (V6). */
public interface WardrobePresetItemRepository
        extends JpaRepository<WardrobePresetItemEntity, WardrobePresetItemEntity.Key> {

    List<WardrobePresetItemEntity> findByAccountId(String accountId);

    List<WardrobePresetItemEntity> findByAccountIdAndWeaponType(String accountId, String weaponType);
}
