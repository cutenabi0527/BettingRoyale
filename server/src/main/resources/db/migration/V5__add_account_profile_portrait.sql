-- 프로필 초상화 (S-UX-RQ5N9E).
--
-- 프로필은 로비·대기방에 보이는 초상화 이미지 한 장이며, 계정 생성 시 서버가 정한다.
-- 캐릭터 3D 외형(appearance_preset_seed)과는 다른 값이라 열을 따로 둔다.
--
-- 소유는 별도 표로 둔다. 계정 열에 쉼표 문자열로 담으면 나중에 초상화를 지급·회수할 때
-- 행 단위로 다루지 못한다.

-- 기존 계정도 신규 계정과 같은 기본값을 갖는다 (S-UX-RQ5N9E: 무작위가 아니라 항상 같은 것).
ALTER TABLE accounts
    ADD COLUMN profile_portrait_id VARCHAR(16) NOT NULL DEFAULT 'hat_musketeer';

CREATE TABLE account_portraits (
    account_id  VARCHAR(32) NOT NULL,
    portrait_id VARCHAR(16) NOT NULL,
    granted_at  DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, portrait_id),
    CONSTRAINT fk_account_portraits_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON DELETE CASCADE
);

-- 현재 5장은 전원 기본 제공이므로 기존 계정에 일괄 부여한다.
INSERT INTO account_portraits (account_id, portrait_id, granted_at)
SELECT a.account_id, p.portrait_id, NOW(6)
FROM accounts a
CROSS JOIN (
    SELECT 'hood_cat'      AS portrait_id
    UNION ALL SELECT 'hood_bear'
    UNION ALL SELECT 'hood_ranger'
    UNION ALL SELECT 'hat_musketeer'
    UNION ALL SELECT 'hood_rabbit'
) p;
