package com.bettingroyale.server.wardrobe;

import java.util.List;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;

/** 외형 카탈로그 조회 (V6). 행 생성·수정은 Flyway migration 만 한다. */
public interface WardrobeItemRepository extends JpaRepository<WardrobeItemEntity, String> {

    @Query("select i.itemId from WardrobeItemEntity i")
    List<String> findAllItemIds();
}
