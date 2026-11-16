package com.bettingroyale.server.account;

import java.time.Instant;
import java.util.concurrent.ThreadLocalRandom;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/**
 * 계정 영속 신원·메달 잔액·외형 프리셋. 스키마 원본은 Flyway V1~V3 — 여기서는 매핑만 한다.
 * 잔액 증가는 지급과 같은 트랜잭션의 원자 UPDATE({@link AccountRepository#addToBalance})로만 한다.
 */
@Entity
@Table(name = "accounts")
public class AccountEntity {

    @Id
    @Column(name = "account_id", length = 32)
    private String accountId;

    /**
     * 이메일 형식, 소문자 정규화 저장 (S-BE-VGE4WD / S-BE-JNTJTE).
     * identify(deprecated) 로 만든 계정은 NULL — 로그인할 수 없다 (V2).
     */
    @Column(name = "login_id", length = 254)
    private String loginId;

    /** BCrypt 해시만 — 평문은 어디에도 저장·출력하지 않는다 (api.md 3-6). identify 계정은 NULL. */
    @Column(name = "password_hash", length = 72)
    private String passwordHash;

    @Column(nullable = false, length = 16)
    private String nickname;

    /** 동시 로그인 차단(S-BE-VGE4WD): 로그인 성공마다 +1. 토큰의 ver 클레임과 다르면 SESSION_REPLACED. */
    @Column(name = "session_version", nullable = false)
    private long sessionVersion;

    /** 현재 메달 잔액 (V2). 지급 트랜잭션 밖에서 직접 수정하지 않는다. */
    @Column(name = "medal_balance", nullable = false)
    private long medalBalance;

    /**
     * 계정 생성 때 서버가 한 번 정한 외형 프리셋 seed. 방 입장·Host 여부는 이 값을 바꾸지 않는다.
     * 실제 Body/Face/의상 선택은 클라이언트 카탈로그가 이 값을 결정적으로 해석한다(S-GD-X9T8WC).
     */
    @Column(name = "appearance_preset_seed", nullable = false)
    private int appearancePresetSeed;

    /**
     * 로비·대기방에 보이는 프로필 초상화 (S-UX-RQ5N9E, V5).
     * {@link #appearancePresetSeed} 와 다른 값이다 — 그쪽은 3D 캐릭터 외형이고 사람이 바꾸지 않는다.
     */
    @Column(name = "profile_portrait_id", nullable = false, length = ProfilePortraits.MAX_ID_LENGTH)
    private String profilePortraitId;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    protected AccountEntity() {
        // JPA 전용
    }

    public AccountEntity(String accountId, String loginId, String passwordHash, String nickname) {
        this.accountId = accountId;
        this.loginId = loginId;
        this.passwordHash = passwordHash;
        this.nickname = nickname;
        this.sessionVersion = 0;
        this.medalBalance = 0;
        this.appearancePresetSeed = ThreadLocalRandom.current().nextInt(1, Integer.MAX_VALUE);
        // 기본 초상화는 신규 계정 전원이 같다 — 무작위로 주지 않는다 (S-UX-RQ5N9E).
        this.profilePortraitId = ProfilePortraits.DEFAULT;
        this.createdAt = Instant.now();
    }

    public String accountId() {
        return accountId;
    }

    public String loginId() {
        return loginId;
    }

    public String passwordHash() {
        return passwordHash;
    }

    public String nickname() {
        return nickname;
    }

    public long sessionVersion() {
        return sessionVersion;
    }

    /** 로그인 성공 시 호출 — 이전에 발급된 모든 토큰을 무효화한다. 새 세션 버전을 돌려준다. */
    public long bumpSessionVersion() {
        sessionVersion += 1;
        return sessionVersion;
    }

    public long medalBalance() {
        return medalBalance;
    }

    public int appearancePresetSeed() {
        return appearancePresetSeed;
    }

    public String profilePortraitId() {
        return profilePortraitId;
    }

    /**
     * 프로필 초상화를 바꾼다. <b>알려진 ID 인지·소유했는지 검사는 호출자가 마친 뒤</b> 부른다 —
     * 엔티티는 검증 지점이 아니다.
     */
    public void changeProfilePortrait(String portraitId) {
        this.profilePortraitId = portraitId;
    }
}
