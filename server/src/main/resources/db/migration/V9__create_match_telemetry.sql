-- 대전 지표 수집 (S15P21A606-284). 계약: docs/server/api.md 9절 · 스펙: S-GD-GSP9RQ.
--
-- ⚠️ match_results / medal_grants 를 건드리지 않는다. 텔레메트리는 그것들과 생명주기가 다르다 —
--    방이 중간에 터지면 매치 결과는 없고 라운드 데이터만 남는다. FK 를 걸면 그 경우를 저장할 수 없다
--    (S-GD-GSP9RQ 7-1: 텔레메트리를 결과 제출과 분리한다).
--
-- ⚠️ MySQL 8 과 H2(MODE=MySQL, 테스트) 양쪽에서 도는 문법만 쓴다.
--    V7 이 ESCAPE '\' 로 이 함정에 빠져 preview 배포가 막혔다 (S15P21A606-221).
--    그래서 여기서는 ON DUPLICATE KEY UPDATE · JSON 함수 · FIND_IN_SET 을 쓰지 않는다.
--
-- ⚠️ source 가 PK 에 들어간다. S-GD-GSP9RQ 7-2 는 Host 보고를 우선하되 Host 가 이탈하면
--    클라 보고로 메우라고 한다. source 를 PK 에 두면 host 행과 client 행이 공존하고,
--    읽을 때 host 를 고르면 된다 — UPSERT 없이 INSERT 만으로 성립한다(D-083 과 같은 방향).

-- 매치 1건. 같은 매치를 host 와 client 가 각각 보고할 수 있다.
CREATE TABLE match_telemetry (
    match_id       VARCHAR(36)  NOT NULL, -- UUID v4. match_results 와 같은 값이지만 FK 는 걸지 않는다
    source         VARCHAR(8)   NOT NULL, -- 'host' | 'client'
    reported_by    VARCHAR(32)  NOT NULL, -- 보낸 계정. 진위는 보증하지 않는다 (D-030 · O-002)
    schema_version INT          NOT NULL, -- 항목이 바뀌어도 옛 데이터를 해석하기 위한 값 (7-3)
    map_id         VARCHAR(64)  NOT NULL,
    player_count   INT          NOT NULL, -- 매치 시작 계정 수 N. 점수표가 N 만큼 잘린다 (S-GD-P7SC3Q)
    round_count    INT          NOT NULL,
    ended_reason   VARCHAR(32)  NOT NULL, -- 'completed' | 'abandoned' | 'timeout' 등
    build_version  VARCHAR(64)  NOT NULL, -- Photon AppVersion 과 같은 값 (S-ONL-RBCJKD)
    data_version   VARCHAR(64)  NOT NULL, -- gamedata.bin 버전
    started_at     TIMESTAMP    NOT NULL,
    ended_at       TIMESTAMP    NULL,     -- 방이 터지면 채워지지 않는다
    created_at     TIMESTAMP    NOT NULL,
    PRIMARY KEY (match_id, source)
);

-- 밸런스 비교는 "패치 전 N판 vs 후 N판" 이라 버전이 조회의 축이다.
CREATE INDEX idx_match_telemetry_versions ON match_telemetry (build_version, data_version);
CREATE INDEX idx_match_telemetry_created ON match_telemetry (created_at);

-- 참가자 1명 = 1행. 무기는 대기방에서 고르면 매치 내내 고정이라 매치당 1개 값이다.
CREATE TABLE match_participants (
    match_id         VARCHAR(36) NOT NULL,
    account_id       VARCHAR(32) NOT NULL,
    source           VARCHAR(8)  NOT NULL,
    weapon           VARCHAR(16) NOT NULL, -- 'Sword' | 'Bow'. 둔기는 예약만이라 오지 않는다 (S-GD-T8H7W7)
    final_rank       INT         NOT NULL,
    final_score      INT         NOT NULL,
    kills            INT         NOT NULL,
    deaths           INT         NOT NULL,
    assists          INT         NOT NULL,
    damage_dealt     BIGINT      NOT NULL,
    damage_taken     BIGINT      NOT NULL,
    gold_earned      BIGINT      NOT NULL,
    gold_spent       BIGINT      NOT NULL,
    survival_seconds INT         NOT NULL,
    created_at       TIMESTAMP   NOT NULL,
    PRIMARY KEY (match_id, account_id, source)
);

-- 무기별 승률이 이 인덱스로 나온다.
CREATE INDEX idx_match_participants_weapon ON match_participants (weapon, final_rank);

-- 제시된 축복 1건 = 1행. ⚠️ 목록을 CSV·JSON 으로 담지 않는다 —
-- 픽률 집계에 FIND_IN_SET 이나 JSON 함수가 필요해지고 둘 다 MySQL/H2 호환이 위태롭다.
-- 행으로 펴면 픽률이 순수 SQL 집계가 된다:
--   SELECT blessing_id, COUNT(*) AS offered, SUM(CASE WHEN picked = 1 THEN 1 ELSE 0 END) AS picked
--   FROM blessing_offers WHERE source = 'host' GROUP BY blessing_id;
CREATE TABLE blessing_offers (
    match_id          VARCHAR(36) NOT NULL,
    account_id        VARCHAR(32) NOT NULL,
    round_no          INT         NOT NULL, -- 1..3. 등급은 라운드로 정해진다 (S-GD-HE87JY)
    blessing_id       VARCHAR(64) NOT NULL,
    source            VARCHAR(8)  NOT NULL,
    picked            TINYINT     NOT NULL, -- 0 | 1. BOOLEAN 은 H2/MySQL 표기가 갈려 쓰지 않는다
    reroll_count      INT         NOT NULL, -- 그 라운드에서 이 제시가 나오기까지의 재추첨 횟수
    reroll_gold_spent BIGINT      NOT NULL,
    decision_millis   INT         NULL,     -- 선택까지 걸린 시간. 클라만 알 수 있어 host 보고엔 없다
    created_at        TIMESTAMP   NOT NULL,
    PRIMARY KEY (match_id, account_id, round_no, blessing_id, source)
);

-- 축복별 픽률·승률 집계의 진입 인덱스.
CREATE INDEX idx_blessing_offers_blessing ON blessing_offers (blessing_id, picked);
