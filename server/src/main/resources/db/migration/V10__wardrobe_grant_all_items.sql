-- 외형 의상을 전량 기본 지급으로 되돌린다 (S-GD-ZVME43).
--
-- V7 이 S-GD-5R953J 에 따라 기본 지급을 5종으로 줄이고 나머지를 메달 구매로 돌렸다.
-- 사용자가 그 결정을 뒤집어 "모든 외형 스킨을 모든 플레이어에게 전부 연다"로 확정했으므로
-- 여기서 되돌린다.
--
-- ⚠️ V7 을 수정하지 않는다. 이미 적용된 마이그레이션을 고치면 checksum 이 깨지고, 그 파일에 적힌
--    당시 결정의 기록도 사라진다. 되돌림은 언제나 새 파일로 한다.
--
-- ⚠️ 가격표(price_medals)는 지우지 않는다. 기본 지급으로 살 필요가 없어질 뿐이고, 등급·가격은
--    기획이 실제로 검토한 값이라 남겨 둔다. 지급 범위를 다시 줄이면 그대로 쓸 수 있다.

-- 1) 신규 계정: 카탈로그 전량을 기본 지급 대상으로 되돌린다.
--    목록의 정본은 이 컬럼 하나다(AccountWardrobeItemRepository.insertDefaultGrants 가 이것만 읽는다).
--    WHERE 를 붙인 이유는 값이 아니라 안전이다 — MySQL 클라이언트가 safe-update 모드면 WHERE 없는
--    UPDATE 를 거부한다. 결과는 조건이 있든 없든 같다.
UPDATE wardrobe_items SET granted_by_default = 1 WHERE granted_by_default = 0;

-- 2) 기존 계정: 지급은 계정 생성 때 한 번뿐이라 위 변경만으로는 아무도 못 받는다.
--    이미 있는 계정 전부에 빠진 항목을 채운다.
--
--    ⚠️ save() 가 아니라 명시적 INSERT 다 (D-BE-SG4AD6 / 레거시 D-083) — PK 를 우리가 지정하는
--       엔티티에서 save() 는 merge 라 행이 있으면 UPDATE 로 조용히 성공한다.
--    NOT EXISTS 로 이미 소유한 항목은 건너뛴다. 재실행해도 같은 결과다.
INSERT INTO account_wardrobe_items (account_id, item_id, granted_at)
SELECT a.account_id, w.item_id, NOW()
  FROM accounts a
 CROSS JOIN wardrobe_items w
 WHERE NOT EXISTS (
        SELECT 1 FROM account_wardrobe_items o
         WHERE o.account_id = a.account_id
           AND o.item_id = w.item_id);
