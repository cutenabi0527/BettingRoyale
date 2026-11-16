package com.bettingroyale.server.auth;

import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

import org.springframework.web.bind.annotation.PatchMapping;

import com.bettingroyale.server.account.AccountController.AccountResponse;
import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.account.ProfilePortraitService;
import com.bettingroyale.server.account.ProfilePortraits;

import jakarta.validation.Valid;
import jakarta.validation.constraints.Email;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/** 계약: docs/server/api.md 3-6 (signup), 3-7 (login), 3-8 (me). */
@RestController
@RequestMapping("/api/v1")
public class AuthController {

    /**
     * 비밀번호 확인 필드는 클라이언트에 없고 서버로도 오지 않는다 (S-BE-VGE4WD).
     * password 8~72자 — 상한 72는 BCrypt 입력 한계, 나머지 ⚠️추정 (S-BE-JNTJTE).
     */
    public record SignupRequest(
            @NotBlank @Email @Size(max = 254) String loginId,
            @NotBlank @Size(min = 8, max = 72) String password,
            @NotBlank @Size(max = 64) String nickname) {
    }

    public record LoginRequest(
            @NotBlank @Size(max = 254) String loginId,
            @NotBlank @Size(max = 72) String password) {
    }

    public record LoginResponse(String accessToken, String tokenType, long expiresInSeconds,
            AccountResponse account) {
    }

    /** 프로필 초상화 변경 요청 (api.md 3-9, S-UX-RQ5N9E). */
    public record ProfilePortraitRequest(
            @NotBlank @Size(max = ProfilePortraits.MAX_ID_LENGTH) String profilePortraitId) {
    }

    private final AuthService auth;
    private final ProfilePortraitService portraits;

    public AuthController(AuthService auth, ProfilePortraitService portraits) {
        this.auth = auth;
        this.portraits = portraits;
    }

    @PostMapping("/auth/signup")
    @ResponseStatus(HttpStatus.CREATED)
    public AccountResponse signup(@Valid @RequestBody SignupRequest request) {
        AccountEntity entity = auth.signup(request.loginId(), request.password(), request.nickname());
        // 신규 계정의 잔액은 항상 0 — 토큰은 발급하지 않는다 (api.md 3-6).
        return toResponse(entity);
    }

    @PostMapping("/auth/login")
    public LoginResponse login(@Valid @RequestBody LoginRequest request) {
        AuthService.LoginResult result = auth.login(request.loginId(), request.password());
        return new LoginResponse(result.accessToken(), "Bearer", result.expiresInSeconds(),
                toResponse(result.account()));
    }

    /** 현재 유일한 JWT 필수 엔드포인트 (api.md 3-8). 기존 3-3~3-5 강제는 축1·축5 전환과 함께 별도 이슈. */
    @GetMapping("/accounts/me")
    public AccountResponse me(@RequestHeader(value = "Authorization", required = false) String authorization) {
        return toResponse(auth.authenticate(authorization));
    }

    /**
     * 프로필 초상화를 바꾼다. 소유하지 않은 초상화는 403 으로 막는다 (api.md 3-9).
     *
     * <p>⚠️ <b>검증을 클라이언트에 맡기지 않는다</b> — 화면이 목록을 어떻게 그리든 서버가
     * 카탈로그와 소유를 다시 본다.
     */
    @PatchMapping("/accounts/me")
    public AccountResponse changeProfilePortrait(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @Valid @RequestBody ProfilePortraitRequest request) {
        AccountEntity account = auth.authenticate(authorization);
        return toResponse(portraits.changePortrait(account, request.profilePortraitId()));
    }

    private AccountResponse toResponse(AccountEntity entity) {
        // 잔액은 V2(-35)부터 accounts 테이블이 원본이다 — 엔티티에서 바로 읽는다.
        return new AccountResponse(
                entity.accountId(),
                entity.nickname(),
                entity.medalBalance(),
                entity.appearancePresetSeed(),
                entity.profilePortraitId(),
                portraits.ownedPortraitIds(entity.accountId()));
    }
}
