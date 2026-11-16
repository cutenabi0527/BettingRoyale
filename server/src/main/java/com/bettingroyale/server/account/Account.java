package com.bettingroyale.server.account;

/**
 * 계정의 읽기 스냅샷. 원본은 MySQL(accounts) — 잔액을 포함한 모든 값이 조회 시점의 것이다.
 * 잔액 변경은 지급 트랜잭션({@link AccountRepository#addToBalance})에서만 일어난다 (V2, -35).
 */
public record Account(String accountId, String nickname, long medalBalance, int appearancePresetSeed,
        String profilePortraitId) {
}
