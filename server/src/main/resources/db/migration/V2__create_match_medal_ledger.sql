-- 매치 결과·메달 원장·잔액 영속화 (S1 S15P21A606-35). 계약: docs/server/api.md 3-3~3-5.
-- in-memory MedalLedgerStore 의 보장을 DB 로 옮긴다:
--   멱등키 (match_id, account_id) = medal_grants PK,
--   최초 제출의 제출자·결과 집합 고정 = match_results 1행 (PK 가 동시 제출을 직렬화).
-- MySQL 8 과 H2(MODE=MySQL, 테스트) 양쪽에서 동작하는 문법만 사용한다.

-- 계정 잔액을 영속화한다. 지급과 같은 트랜잭션에서 갱신된다.
ALTER TABLE accounts ADD COLUMN medal_balance BIGINT NOT NULL DEFAULT 0;

-- identify(deprecated, api.md 3-2) 로 만든 계정은 로그인 정보가 없다.
-- 잔액이 DB 로 오면서 지급 대상 계정은 전부 accounts 에 있어야 하므로 NULL 을 허용한다.
ALTER TABLE accounts MODIFY login_id VARCHAR(254) NULL;
ALTER TABLE accounts MODIFY password_hash VARCHAR(72) NULL;

-- 매치당 1행. 최초 성공 제출이 제출자와 결과 집합을 고정한다 (api.md 3-3).
CREATE TABLE match_results (
    match_id             VARCHAR(36) NOT NULL, -- UUID v4 (서버가 형식 검증)
    submitter_account_id VARCHAR(32) NOT NULL,
    created_at           TIMESTAMP   NOT NULL,
    PRIMARY KEY (match_id)
);

-- 지급 원장. PK 가 곧 멱등키 (matchId, accountId) — 같은 키 중복 지급을 구조로 막는다.
CREATE TABLE medal_grants (
    match_id       VARCHAR(36) NOT NULL,
    account_id     VARCHAR(32) NOT NULL,
    player_rank    INT         NOT NULL, -- RANK 는 MySQL 8 예약어라 이름을 피한다
    medals_awarded BIGINT      NOT NULL,
    balance_after  BIGINT      NOT NULL, -- 지급 시점 스냅샷. 응답의 newBalance 는 항상 현재 잔액이다 (api.md 3-3)
    created_at     TIMESTAMP   NOT NULL,
    PRIMARY KEY (match_id, account_id)
);
