-- 계정 외형("옷장") — 무기 유형별 프리셋과 의상 소유 (docs/server/api.md 8절).
--
-- 근거 스펙: S-GD-5NRMCY(무기 유형별 프리셋을 계정이 소유), S-UX-F3CJEP(소유한 의상만 고른다),
--            S-GD-9BFMZ6(기본 지급 범위 — ⚠️ 에이전트 임의, assumed).
-- 근거 결정: D-BE-Z2HMR6(문자열 아이템 ID·슬롯 맵), D-BE-5DNW6H(계정 행 잠금·서버 발급 revision).
--
-- ⚠️ accounts.appearance_preset_seed(V3)는 건드리지 않는다. 의미도 값도 그대로 두고 이 계약이 옆에 선다 —
--    api.md 3-2 가 "이 응답 필드를 조용히 재해석하지 않는다"를 이미 적어 뒀다(S-GD-X9T8WC).
--
-- 타입은 MySQL 8 과 H2(테스트, MODE=MySQL) 양쪽에서 같은 의미를 갖는 것만 쓴다.

-- 카탈로그. 서버가 "존재하는 아이템"을 아는 유일한 근거이며, 클라이언트 공용 카탈로그
-- (S-CLI-RQB4QD 의 Body 5 · Face 40 · 의상 파츠 40)와 같은 ID 집합이어야 한다.
-- ⚠️ 아이템↔슬롯 호환(특히 Costume 의 다중 슬롯 점유)은 여기 넣지 않는다. 그 규칙의 정본은
--    클라이언트 카탈로그 하나뿐이다 — 두 곳에 두면 반드시 어긋난다(D-BE-Z2HMR6).
--    category 는 진단·운영용 분류일 뿐이며 서버가 이것으로 저장을 거절하지 않는다.
CREATE TABLE wardrobe_items (
    item_id            VARCHAR(48) NOT NULL, -- 공급사 prefab 이름 그대로 (예: Body_11, Hat_24)
    category           VARCHAR(16) NOT NULL, -- BODY / FACE / OUTFIT / COSTUME — 분류일 뿐 검증 기준이 아니다
    granted_by_default TINYINT     NOT NULL, -- 1 이면 계정 생성 시 자동 지급 (S-GD-9BFMZ6)
    PRIMARY KEY (item_id)
);

-- 소유. 계정 열에 쉼표 문자열로 담지 않는 이유는 지급·회수를 행 단위로 다루기 위해서다
-- (account_portraits 와 같은 이유, S-UX-RQ5N9E).
CREATE TABLE account_wardrobe_items (
    account_id VARCHAR(32) NOT NULL,
    item_id    VARCHAR(48) NOT NULL,
    granted_at DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, item_id),
    CONSTRAINT fk_account_wardrobe_items_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id) ON DELETE CASCADE,
    CONSTRAINT fk_account_wardrobe_items_item
        FOREIGN KEY (item_id) REFERENCES wardrobe_items (item_id)
);

-- 프리셋 머리. 행이 있다는 것 자체가 "이 무기 유형을 한 번이라도 저장했다"는 뜻이고,
-- 그래서 revision 은 1 부터 시작한다. 행이 없으면 조회 응답이 revision 0 을 만들어 준다 —
-- 그 동안 클라이언트는 계속 appearance_preset_seed 로 외형을 만든다(api.md 8-2).
CREATE TABLE account_wardrobe_presets (
    account_id  VARCHAR(32) NOT NULL,
    weapon_type VARCHAR(16) NOT NULL, -- 유효 목록은 설정(wardrobe.weapon-types) — 현재 Sword / Bow
    revision    BIGINT      NOT NULL,
    updated_at  DATETIME(3) NOT NULL,
    PRIMARY KEY (account_id, weapon_type),
    CONSTRAINT fk_account_wardrobe_presets_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id) ON DELETE CASCADE
);

-- 프리셋 슬롯. 슬롯이 없으면 기본값이라 행도 없다. Costume 은 점유하는 슬롯 수만큼 같은 item_id 가 들어간다.
CREATE TABLE account_wardrobe_preset_items (
    account_id  VARCHAR(32) NOT NULL,
    weapon_type VARCHAR(16) NOT NULL,
    slot        VARCHAR(16) NOT NULL, -- body/face/glasses/gloves/hairstyle/hat/mask/outwear/pants/shorts/shoe
    item_id     VARCHAR(48) NOT NULL,
    PRIMARY KEY (account_id, weapon_type, slot),
    CONSTRAINT fk_wardrobe_preset_items_preset
        FOREIGN KEY (account_id, weapon_type)
        REFERENCES account_wardrobe_presets (account_id, weapon_type) ON DELETE CASCADE,
    CONSTRAINT fk_wardrobe_preset_items_item
        FOREIGN KEY (item_id) REFERENCES wardrobe_items (item_id)
);

-- 현재 카탈로그 85개 (Body 5 · Face 40 · 일반 의상 36 · Costume 4).
-- ⚠️ 전량 granted_by_default = 1 — 기본 지급 범위는 에이전트가 임의로 정했다(S-GD-9BFMZ6, assumed).
--    기획이 해금·구매를 정하면 그때 갈라진다. 지금은 잠긴 의상이 하나도 없다.
INSERT INTO wardrobe_items (item_id, category, granted_by_default) VALUES
    -- Body(+같은 피부색 Ears 쌍) 5개 — 클라이언트 기본은 Body_11 이다.
    ('Body_09', 'BODY', 1),
    ('Body_10', 'BODY', 1),
    ('Body_11', 'BODY', 1),
    ('Body_12', 'BODY', 1),
    ('Body_13', 'BODY', 1),
    -- Face 40개 — 클라이언트 기본은 Male_Emotion_Usual_01 이다.
    ('Male_Emotion_Usual_01', 'FACE', 1),
    ('Female_Emotion_Anger_01', 'FACE', 1),
    ('Female_Emotion_Angry_01', 'FACE', 1),
    ('Female_Emotion_Angry_02', 'FACE', 1),
    ('Female_Emotion_Confuse_01', 'FACE', 1),
    ('Female_Emotion_Cry_01', 'FACE', 1),
    ('Female_Emotion_Evil_01', 'FACE', 1),
    ('Female_Emotion_Evil_02', 'FACE', 1),
    ('Female_Emotion_Happy_01', 'FACE', 1),
    ('Female_Emotion_Happy_02', 'FACE', 1),
    ('Female_Emotion_Love_01', 'FACE', 1),
    ('Female_Emotion_Neutral_01', 'FACE', 1),
    ('Female_Emotion_Neutral_02', 'FACE', 1),
    ('Female_Emotion_Sad_01', 'FACE', 1),
    ('Female_Emotion_Sad_02', 'FACE', 1),
    ('Female_Emotion_Shout_01', 'FACE', 1),
    ('Female_Emotion_Surpriced_01', 'FACE', 1),
    ('Female_Emotion_Surpriced_02', 'FACE', 1),
    ('Female_Emotion_Tongue_01', 'FACE', 1),
    ('Female_Emotion_Usual_01', 'FACE', 1),
    ('Female_Emotion_Usual_02', 'FACE', 1),
    ('Male_Emotion_Anger_01', 'FACE', 1),
    ('Male_Emotion_Angry_01', 'FACE', 1),
    ('Male_Emotion_Angry_02', 'FACE', 1),
    ('Male_Emotion_Confuse_01', 'FACE', 1),
    ('Male_Emotion_Cry_01', 'FACE', 1),
    ('Male_Emotion_Evil_01', 'FACE', 1),
    ('Male_Emotion_Evil_02', 'FACE', 1),
    ('Male_Emotion_Happy_01', 'FACE', 1),
    ('Male_Emotion_Happy_02', 'FACE', 1),
    ('Male_Emotion_Love_01', 'FACE', 1),
    ('Male_Emotion_Neutral_01', 'FACE', 1),
    ('Male_Emotion_Neutral_02', 'FACE', 1),
    ('Male_Emotion_Sad_01', 'FACE', 1),
    ('Male_Emotion_Sad_02', 'FACE', 1),
    ('Male_Emotion_Shout_01', 'FACE', 1),
    ('Male_Emotion_Surpriced_01', 'FACE', 1),
    ('Male_Emotion_Surpriced_02', 'FACE', 1),
    ('Male_Emotion_Tongue_01', 'FACE', 1),
    ('Male_Emotion_Usual_02', 'FACE', 1),
    -- 일반 의상 36개. 슬롯 호환은 서버가 검사하지 않는다(8-1).
    ('Glasses_05', 'OUTFIT', 1),
    ('Glasses_06', 'OUTFIT', 1),
    ('Glasses_14', 'OUTFIT', 1),
    ('Gloves_03', 'OUTFIT', 1),
    ('Gloves_07', 'OUTFIT', 1),
    ('Gloves_09', 'OUTFIT', 1),
    ('Gloves_10', 'OUTFIT', 1),
    ('Gloves_11', 'OUTFIT', 1),
    ('Gloves_12', 'OUTFIT', 1),
    ('Hairstyle_Female_06', 'OUTFIT', 1),
    ('Hairstyle_Female_Single_008', 'OUTFIT', 1),
    ('Hairstyle_Male_12', 'OUTFIT', 1),
    ('Hairstyle_Male_13', 'OUTFIT', 1),
    ('Hairstyle_Male_Single', 'OUTFIT', 1),
    ('Hat_03', 'OUTFIT', 1),
    ('Hat_24', 'OUTFIT', 1),
    ('Hat_25', 'OUTFIT', 1),
    ('Hat_44', 'OUTFIT', 1),
    ('Hat_48', 'OUTFIT', 1),
    ('Mask_02', 'OUTFIT', 1),
    ('Outwear_19', 'OUTFIT', 1),
    ('Outwear_28', 'OUTFIT', 1),
    ('Outwear_30', 'OUTFIT', 1),
    ('Outwear_36', 'OUTFIT', 1),
    ('Outwear_42', 'OUTFIT', 1),
    ('Pants_02', 'OUTFIT', 1),
    ('Pants_06', 'OUTFIT', 1),
    ('Pants_07', 'OUTFIT', 1),
    ('Pants_11', 'OUTFIT', 1),
    ('Shoe_Slippers_05', 'OUTFIT', 1),
    ('Shoe_Slippers_07', 'OUTFIT', 1),
    ('Shoe_Sneakers_05', 'OUTFIT', 1),
    ('Shoe_Sneakers_10', 'OUTFIT', 1),
    ('Shoe_Sneakers_14', 'OUTFIT', 1),
    ('Shorts_01', 'OUTFIT', 1),
    ('Shorts_03', 'OUTFIT', 1),
    -- 여러 슬롯을 점유하는 Costume 4개. 점유 규칙의 정본은 클라이언트 공용 카탈로그다.
    ('Costume_1_01', 'COSTUME', 1),
    ('Costume_1_02', 'COSTUME', 1),
    ('Costume_1_03', 'COSTUME', 1),
    ('Costume_5_02', 'COSTUME', 1);
-- 기존 계정에도 기본 지급분을 일괄 부여한다. 이 작업 때문에 이미 있는 계정이 고를 수 있는 것이
-- 줄어들면 안 된다 (S-GD-5NRMCY 마이그레이션 요구).
INSERT INTO account_wardrobe_items (account_id, item_id, granted_at)
SELECT a.account_id, w.item_id, NOW(6)
FROM accounts a
CROSS JOIN wardrobe_items w
WHERE w.granted_by_default = 1;
