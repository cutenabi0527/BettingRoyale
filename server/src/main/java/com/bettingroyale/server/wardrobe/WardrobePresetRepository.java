package com.bettingroyale.server.wardrobe;

import java.util.List;

import org.springframework.data.jpa.repository.JpaRepository;

/** 계정별 무기 유형 프리셋 머리 (V6). 행 생성은 {@link WardrobeStore} 가 persist 로만 한다. */
public interface WardrobePresetRepository
        extends JpaRepository<WardrobePresetEntity, WardrobePresetEntity.Key> {

    List<WardrobePresetEntity> findByAccountId(String accountId);
}
