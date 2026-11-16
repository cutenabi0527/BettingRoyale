-- 계정별 클라우드 설정 정본 (docs/server/api.md 7절, S-UX-8FGK29).
-- 계정 1개당 정확히 1행이며 PK 가 곧 계정이다 — 설정은 계정에 종속된 값이라 별도 대리키를 두지 않는다.
-- document 는 검증을 통과한 JSON 원문이다. 서버는 내용을 해석하지 않고, 알 수 없는 category/option 필드도
-- 그대로 보존한다. updated_at/device_id 는 문서 안에도 있지만 LWW 비교를 JSON 파싱 없이 하려고 컬럼으로도 둔다.
-- 타입은 MySQL 8 과 H2(테스트) 양쪽에서 같은 의미를 갖는 것만 쓴다 (LONGTEXT/DATETIME(3)).
CREATE TABLE account_settings (
    account_id       VARCHAR(32)  NOT NULL,
    envelope_version INT          NOT NULL,
    settings_version BIGINT       NOT NULL,
    updated_at       DATETIME(3)  NOT NULL,
    device_id        VARCHAR(128) NOT NULL,
    document         LONGTEXT     NOT NULL,
    stored_at        DATETIME(3)  NOT NULL,
    PRIMARY KEY (account_id),
    CONSTRAINT fk_account_settings_account FOREIGN KEY (account_id) REFERENCES accounts (account_id)
);
