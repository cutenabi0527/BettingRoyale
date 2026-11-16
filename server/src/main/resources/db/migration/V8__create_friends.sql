-- 친구 관계와 친구 요청 (docs/server/api.md 3-14~3-18, S15P21A606-202).
-- 사람이 보는 식별자는 accounts.nickname 이다 — login_id 는 이메일이라 노출하지 않는다.
-- MySQL 8 과 H2(테스트) 양쪽에서 같은 의미를 갖는 DDL 만 쓴다.

-- 친구 요청. 수락·거절되면 행을 지운다 — 대기 중인 것만 남긴다(3-14).
-- 이력이 필요해지면 그때 상태 컬럼과 보존 정책을 함께 정한다. 지금 남기면 "처리된 요청"의
-- 수명·재요청 규칙을 정하지 않은 채 데이터만 쌓인다.
CREATE TABLE friend_requests (
    request_id            BIGINT       NOT NULL AUTO_INCREMENT,
    requester_account_id  VARCHAR(32)  NOT NULL,
    addressee_account_id  VARCHAR(32)  NOT NULL,
    created_at            DATETIME(3)  NOT NULL,
    PRIMARY KEY (request_id),
    -- 같은 방향의 중복 요청을 DB 가 막는다. 반대 방향은 막지 않는다 —
    -- 서로 보낸 경우는 즉시 친구가 되는 정상 경로다(3-13).
    CONSTRAINT uk_friend_requests_pair UNIQUE (requester_account_id, addressee_account_id),
    CONSTRAINT fk_friend_requests_requester FOREIGN KEY (requester_account_id) REFERENCES accounts (account_id),
    CONSTRAINT fk_friend_requests_addressee FOREIGN KEY (addressee_account_id) REFERENCES accounts (account_id)
);

-- 받은 요청 목록 조회가 가장 잦다(폴링, 3-17).
CREATE INDEX ix_friend_requests_addressee ON friend_requests (addressee_account_id);

-- 친구 관계. 대칭이므로 한 쌍을 한 행으로만 저장한다.
-- account_id_low < account_id_high 를 애플리케이션이 보장하고 UNIQUE 가 중복을 막는다.
-- 두 행(양방향)으로 저장하면 한쪽만 지워지는 상태가 생길 수 있다.
CREATE TABLE friendships (
    account_id_low   VARCHAR(32) NOT NULL,
    account_id_high  VARCHAR(32) NOT NULL,
    created_at       DATETIME(3) NOT NULL,
    PRIMARY KEY (account_id_low, account_id_high),
    CONSTRAINT fk_friendships_low  FOREIGN KEY (account_id_low)  REFERENCES accounts (account_id),
    CONSTRAINT fk_friendships_high FOREIGN KEY (account_id_high) REFERENCES accounts (account_id)
);

-- 내 친구 목록은 두 컬럼 어느 쪽에서든 나를 찾아야 한다.
CREATE INDEX ix_friendships_high ON friendships (account_id_high);
