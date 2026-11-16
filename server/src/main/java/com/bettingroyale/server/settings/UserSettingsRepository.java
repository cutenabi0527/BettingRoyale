package com.bettingroyale.server.settings;

import org.springframework.data.jpa.repository.JpaRepository;

/**
 * 설정 정본 저장소. 쓰기 경로는 계정 행 잠금이 필요해 {@link UserSettingsStore} 가 EntityManager 로 직접 다루고,
 * 이 인터페이스는 조회 전용으로 쓴다.
 */
public interface UserSettingsRepository extends JpaRepository<UserSettingsEntity, String> {
}
