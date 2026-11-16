package com.bettingroyale.server.auth;

import java.util.Locale;

import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.account.AccountIds;
import com.bettingroyale.server.account.AccountRepository;
import com.bettingroyale.server.account.ProfilePortraitService;
import com.bettingroyale.server.common.BadRequestException;
import com.bettingroyale.server.wardrobe.WardrobeService;

/**
 * 회원가입·로그인·토큰 인증 (api.md 3-6~3-8).
 * 비밀번호는 BCrypt 해시로만 저장하고, 평문은 로그를 포함해 어디에도 출력하지 않는다.
 */
@Service
public class AuthService {

    /** 앞뒤 공백 제거 후 1~16자 — 클라 NetworkString<_16> 실측과 동일 (api.md 3-6). */
    private static final int NICKNAME_MAX_LENGTH = 16;

    private final AccountRepository repository;
    private final JwtTokenService tokens;
    private final ProfilePortraitService portraits;
    private final WardrobeService wardrobe;
    private final PasswordEncoder passwordEncoder = new BCryptPasswordEncoder();

    public AuthService(AccountRepository repository, JwtTokenService tokens,
            ProfilePortraitService portraits, WardrobeService wardrobe) {
        this.portraits = portraits;
        this.wardrobe = wardrobe;
        this.repository = repository;
        this.tokens = tokens;
    }

    /** 가입 성공 시 계정 엔티티를 돌려준다. 토큰은 발급하지 않는다 — 가입 후 로그인 화면 복귀(S-UX-6EP7JE). */
    @Transactional
    public AccountEntity signup(String loginId, String password, String nickname) {
        String normalizedLoginId = normalizeLoginId(loginId);
        String trimmedNickname = nickname.trim();
        if (trimmedNickname.isEmpty() || trimmedNickname.length() > NICKNAME_MAX_LENGTH) {
            throw new BadRequestException("닉네임은 1~" + NICKNAME_MAX_LENGTH + "자여야 합니다",
                    "nickname.length=" + trimmedNickname.length());
        }
        if (repository.existsByLoginId(normalizedLoginId)) {
            throw new DuplicateLoginIdException(normalizedLoginId);
        }
        if (repository.existsByNickname(trimmedNickname)) {
            throw new DuplicateNicknameException(trimmedNickname);
        }
        AccountEntity entity = new AccountEntity(AccountIds.newAccountId(), normalizedLoginId,
                passwordEncoder.encode(password), trimmedNickname);
        try {
            AccountEntity saved = repository.saveAndFlush(entity);
            // 기본 제공 초상화를 같은 가입 흐름에서 지급한다 (S-UX-RQ5N9E).
            portraits.grantSignupPortraits(saved.accountId());
            // 기본 제공 외형 아이템도 같은 트랜잭션에서 지급한다 (api.md 8-5).
            // ⚠️ 지급 범위는 에이전트가 임의로 정했다 — S-GD-9BFMZ6 (assumed, 현재 카탈로그 전량).
            wardrobe.grantSignupItems(saved.accountId());
            return saved;
        } catch (DataIntegrityViolationException ex) {
            // 사전 검사와 저장 사이에 동시 가입이 끼어든 경우 — UNIQUE 제약이 최종 방어선이다.
            throw mapConstraintViolation(ex, normalizedLoginId, trimmedNickname);
        }
    }

    public record LoginResult(String accessToken, long expiresInSeconds, AccountEntity account) {
    }

    /**
     * 로그인 성공은 세션 버전을 올려 이전 토큰을 전부 무효화한다 —
     * 동시 로그인 금지, 나중 로그인이 이긴다 (S-BE-VGE4WD / S-BE-JNTJTE).
     */
    @Transactional
    public LoginResult login(String loginId, String password) {
        AccountEntity entity = repository.findByLoginId(normalizeLoginId(loginId))
                .orElseThrow(InvalidCredentialsException::new);
        if (!passwordEncoder.matches(password, entity.passwordHash())) {
            // 아이디 부재와 같은 응답 — 계정 존재 여부를 노출하지 않는다 (api.md 3-7).
            throw new InvalidCredentialsException();
        }
        long version = entity.bumpSessionVersion();
        repository.save(entity);
        return new LoginResult(tokens.issue(entity.accountId(), version), tokens.expiresInSeconds(), entity);
    }

    /** Authorization 헤더의 Bearer 토큰을 검증하고 계정을 돌려준다 (api.md 3-8). */
    @Transactional(readOnly = true)
    public AccountEntity authenticate(String authorizationHeader) {
        if (authorizationHeader == null || !authorizationHeader.startsWith("Bearer ")) {
            throw new TokenInvalidException("Authorization: Bearer <token> 헤더가 필요합니다");
        }
        JwtTokenService.ParsedToken parsed = tokens.parse(authorizationHeader.substring("Bearer ".length()));
        AccountEntity entity = repository.findById(parsed.accountId())
                .orElseThrow(() -> new TokenInvalidException("accountId=" + parsed.accountId()));
        if (parsed.sessionVersion() != entity.sessionVersion()) {
            throw new SessionReplacedException(entity.accountId());
        }
        return entity;
    }

    /** 소문자 정규화 — DEMO@EXAMPLE.COM 과 demo@example.com 은 같은 계정이다 (⚠️추정, S-BE-JNTJTE). */
    private static String normalizeLoginId(String loginId) {
        return loginId.trim().toLowerCase(Locale.ROOT);
    }

    private RuntimeException mapConstraintViolation(DataIntegrityViolationException ex,
            String loginId, String nickname) {
        String message = ex.getMostSpecificCause().getMessage();
        if (message != null && message.toLowerCase(Locale.ROOT).contains("uk_accounts_nickname")) {
            return new DuplicateNicknameException(nickname);
        }
        return new DuplicateLoginIdException(loginId);
    }
}
