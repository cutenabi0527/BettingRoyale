-- 외형 의상의 기본 지급 축소와 메달 가격 (S-GD-5R953J).
--
-- V6 은 카탈로그 85개를 전량 기본 지급했다(S-GD-9BFMZ6, assumed). 기획이 그 값을 뒤집어
-- "슬롯당 한 벌씩 5종만 주고 나머지는 메달로 산다"로 확정했으므로 여기서 갈라진다.
--
-- ⚠️ 이미 전량을 받은 계정에서 회수하지 않는다. S-GD-5R953J 가 "기존 계정은 그대로 남긴다 —
--    기존과 신규의 소유 범위가 영구히 다른 것은 의도된 차이"로 확정했다.
--    그래서 이 파일에는 account_wardrobe_items 를 지우는 문장이 없다. 일부러 없다.

-- 가격. 0 은 "기본 지급이라 살 필요가 없다"는 뜻이다.
ALTER TABLE wardrobe_items
    ADD COLUMN price_medals INT NOT NULL DEFAULT 0;

-- 기본 지급을 5종으로 줄인다. 슬롯당 한 벌이며 머리는 없다(대머리가 기본).
UPDATE wardrobe_items SET granted_by_default = 0;
UPDATE wardrobe_items SET granted_by_default = 1
 WHERE item_id IN (
    'Body_11',                -- 피부  · 클라이언트 기본
    'Male_Emotion_Usual_01',  -- 얼굴  · CommonCharacterAppearanceCatalog.DefaultFaceName
    'Outwear_19',             -- 상의  ⚠️ 에이전트가 고름
    'Pants_02',               -- 하의  ⚠️ 에이전트가 고름
    'Shoe_Sneakers_05'        -- 신발  ⚠️ 에이전트가 고름
 );

-- 가격표 (S-GD-5R953J). 기준은 8인 매치 평균 32.5 메달이다 —
-- 1위 100 · 2위 60 · 3위 40 · 4위 20 · 5위 이하 10 (application.yml, ⚠️ 임시값 S-BE-ZK5DKZ).
--
-- 등급을 3D 모델의 퀄리티가 아니라 슬롯 종류로 나눈 이유: 퀄리티 판정에는 렌더링이 필요한데
-- 그 판단은 기획·아트의 몫이다. 슬롯은 객관적이라 값이 흔들리지 않는다.

-- 이스케이프 문자는 백슬래시가 아니라 ! 다. MySQL 은 기본 sql_mode 에서 문자열 리터럴의 백슬래시를
-- 이스케이프로 처리하므로 ESCAPE 뒤의 백슬래시 한 글자가 닫는 따옴표를 먹어 파싱이 깨진다.
-- 그렇다고 두 글자로 쓰면 테스트 DB(H2, MODE=MySQL)가 "이스케이프는 한 글자" 라며 거부한다 —
-- 양쪽에서 같은 뜻을 갖는 문자가 필요하다. 아이템 ID 에 ! 는 없다.
--
-- 일반 50 (약 2매치) — 얼굴 40종이 여기 있어 단가를 낮게 잡았다. 100 이면 얼굴만 123매치다.
UPDATE wardrobe_items SET price_medals = 50
 WHERE granted_by_default = 0
   AND (category = 'FACE'
        OR item_id LIKE 'Glasses!_%' ESCAPE '!'
        OR item_id LIKE 'Mask!_%'    ESCAPE '!'
        OR item_id LIKE 'Shorts!_%'  ESCAPE '!');

-- 표준 200 (약 6매치)
UPDATE wardrobe_items SET price_medals = 200
 WHERE granted_by_default = 0
   AND (item_id LIKE 'Gloves!_%'    ESCAPE '!'
        OR item_id LIKE 'Hat!_%'       ESCAPE '!'
        OR item_id LIKE 'Hairstyle!_%' ESCAPE '!'
        OR item_id LIKE 'Outwear!_%'   ESCAPE '!'
        OR item_id LIKE 'Pants!_%'     ESCAPE '!'
        OR item_id LIKE 'Shoe!_%'      ESCAPE '!');

-- 희귀 400 (약 12매치) — 피부는 캐릭터 전체 인상을 바꾼다.
UPDATE wardrobe_items SET price_medals = 400
 WHERE granted_by_default = 0 AND category = 'BODY';

-- 코스튬 600 (약 18매치) — 여러 슬롯을 한 번에 채운다.
UPDATE wardrobe_items SET price_medals = 600
 WHERE granted_by_default = 0 AND category = 'COSTUME';
