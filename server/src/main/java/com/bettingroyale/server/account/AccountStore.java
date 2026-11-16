package com.bettingroyale.server.account;

import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.stereotype.Component;

import com.bettingroyale.server.common.AccountNotFoundException;

/**
 * 계정 조회 창구. 원본은 MySQL(accounts) — V2(-35)부터 잔액까지 영속이라 in-memory 상태를 두지 않는다.
 *
 * <p>⚠️ identify 는 deprecated (api.md 3-2) — S0 임시 규약(S-BE-ZK5DKZ)의 호환 유지용.
 * V2 부터는 identify 계정도 DB 에 저장된다(로그인 정보 없음) — 지급 대상 계정이 전부 accounts 에
 * 있어야 결과·잔액이 한 트랜잭션에 묶이기 때문이다. 축1·축5 전환 완료 후 제거한다.</p>
 */
@Component
public class AccountStore {

    private final AccountRepository repository;

    public AccountStore(AccountRepository repository) {
        this.repository = repository;
    }

    /** @deprecated -34 부터 signup/login(api.md 3-6·3-7)이 대체한다. 전환기 호환용으로만 유지. */
    @Deprecated
    public Account identify(String nickname) {
        return repository.findByNickname(nickname)
                .map(AccountStore::snapshot)
                .orElseGet(() -> createForIdentify(nickname));
    }

    /** 존재하지 않으면 ACCOUNT_NOT_FOUND(404). */
    public Account require(String accountId) {
        return repository.findById(accountId)
                .map(AccountStore::snapshot)
                .orElseThrow(() -> new AccountNotFoundException(accountId));
    }

    private Account createForIdentify(String nickname) {
        try {
            return snapshot(repository.saveAndFlush(
                    new AccountEntity(AccountIds.newAccountId(), null, null, nickname)));
        } catch (DataIntegrityViolationException race) {
            // 같은 닉네임의 동시 identify — UNIQUE 제약이 이겼으니 그 계정을 돌려준다.
            return repository.findByNickname(nickname)
                    .map(AccountStore::snapshot)
                    .orElseThrow(() -> race);
        }
    }

    private static Account snapshot(AccountEntity entity) {
        return new Account(
                entity.accountId(),
                entity.nickname(),
                entity.medalBalance(),
                entity.appearancePresetSeed(),
                entity.profilePortraitId());
    }
}
