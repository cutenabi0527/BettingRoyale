-- 계정 생성 시 발급한 외형 프리셋을 영속화한다 (S-GD-X9T8WC).
-- 기존 계정은 호환값 1을 갖고, 신규 계정은 AccountEntity가 양의 난수 seed를 명시해 저장한다.
-- 방 입장·Host 여부는 이 값을 다시 뽑거나 변경하지 않는다.
ALTER TABLE accounts ADD COLUMN appearance_preset_seed INT NOT NULL DEFAULT 1;
