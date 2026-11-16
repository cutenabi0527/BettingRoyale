package com.bettingroyale.server.account;

import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotBlank;

/** 계약: docs/server/api.md 3-2 (identify), 3-4 (balance). */
@RestController
@RequestMapping("/api/v1/accounts")
public class AccountController {

    public record IdentifyRequest(@NotBlank String nickname) {
    }

    /**
     * 프로필 초상화와 소유 목록을 함께 싣는다 (S-UX-RQ5N9E) — 화면이 고를 수 있는 것을
     * 알려면 소유 목록이 필요하고, 조회를 두 번 하게 만들 이유가 없다.
     */
    public record AccountResponse(
            String accountId,
            String nickname,
            long medalBalance,
            int appearancePresetSeed,
            String profilePortraitId,
            java.util.List<String> ownedPortraitIds) {
    }

    public record BalanceResponse(String accountId, long medalBalance) {
    }

    private final AccountStore accounts;
    private final ProfilePortraitService portraits;

    public AccountController(AccountStore accounts, ProfilePortraitService portraits) {
        this.accounts = accounts;
        this.portraits = portraits;
    }

    @PostMapping("/identify")
    public AccountResponse identify(@Valid @RequestBody IdentifyRequest request) {
        Account account = accounts.identify(request.nickname().trim());
        return new AccountResponse(
                account.accountId(),
                account.nickname(),
                account.medalBalance(),
                account.appearancePresetSeed(),
                account.profilePortraitId(),
                portraits.ownedPortraitIds(account.accountId()));
    }

    @GetMapping("/{accountId}/balance")
    public BalanceResponse balance(@PathVariable String accountId) {
        Account account = accounts.require(accountId);
        return new BalanceResponse(account.accountId(), account.medalBalance());
    }
}
