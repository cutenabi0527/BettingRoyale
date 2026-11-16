-- 계정 영속 스키마 (S1 S15P21A606-34). 계약: docs/server/api.md 3-6~3-8.
-- 인증 신원(아이디·비밀번호 해시·닉네임·세션 버전)만 둔다.
-- 메달 잔액·지급 원장의 영속화는 -35 범위라 여기 넣지 않는다 (그때 V2 로 추가).
-- MySQL 8 과 H2(테스트) 양쪽에서 동작하는 공통 DDL 만 사용한다.
CREATE TABLE accounts (
    account_id      VARCHAR(32)  NOT NULL, -- 서버 발급 불투명 키, 상한 32자 ASCII (api.md 3-2)
    login_id        VARCHAR(254) NOT NULL, -- 이메일 형식, 소문자 정규화 저장 (S-BE-VGE4WD / S-BE-JNTJTE)
    password_hash   VARCHAR(72)  NOT NULL, -- BCrypt 해시만 — 평문 저장 금지 (api.md 3-6)
    nickname        VARCHAR(16)  NOT NULL, -- 클라 NetworkString<_16> 실측과 동일 상한
    session_version BIGINT       NOT NULL DEFAULT 0, -- 동시 로그인 차단: 로그인마다 +1, 토큰 클레임과 비교
    created_at      TIMESTAMP    NOT NULL,
    PRIMARY KEY (account_id),
    CONSTRAINT uk_accounts_login_id UNIQUE (login_id),
    CONSTRAINT uk_accounts_nickname UNIQUE (nickname)
);
