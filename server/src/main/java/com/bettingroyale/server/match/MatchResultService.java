package com.bettingroyale.server.match;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

import org.springframework.stereotype.Service;

import com.bettingroyale.server.account.Account;
import com.bettingroyale.server.account.AccountStore;
import com.bettingroyale.server.common.BadRequestException;
import com.bettingroyale.server.medal.MedalPolicy;

/**
 * 매치 결과 제출의 애플리케이션 경계.
 * 요청 형식·계정 존재 검사는 여기서, 멱등성/충돌 판정과 원장·잔액의 단일 트랜잭션은
 * {@link MedalLedgerStore}(MySQL)가 맡는다.
 */
@Service
public class MatchResultService {

    public record SubmittedPlayerResult(String accountId, int rank) {
    }

    public record ReceiptView(String accountId, String nickname, int rank, long medalsAwarded, long newBalance,
            boolean duplicate) {
    }

    private final AccountStore accounts;
    private final MedalLedgerStore ledger;
    private final MedalPolicy policy;

    public MatchResultService(AccountStore accounts, MedalLedgerStore ledger, MedalPolicy policy) {
        this.accounts = accounts;
        this.ledger = ledger;
        this.policy = policy;
    }

    public List<ReceiptView> submit(String submitterAccountId, String matchId, List<SubmittedPlayerResult> results) {
        accounts.require(submitterAccountId);

        Set<String> seenAccounts = new HashSet<>();
        Set<Integer> seenRanks = new HashSet<>();
        boolean submitterIncluded = false;
        List<MedalLedgerStore.GrantRequest> grantRequests = new ArrayList<>(results.size());
        for (SubmittedPlayerResult result : results) {
            if (!seenAccounts.add(result.accountId())) {
                throw new BadRequestException("results 안에 같은 accountId 가 두 번 들어 있습니다",
                        "accountId=" + result.accountId());
            }
            if (!seenRanks.add(result.rank())) {
                throw new BadRequestException("results 안에 같은 rank 가 두 번 들어 있습니다. 동점 정책 확정 전까지 rank 는 유일해야 합니다",
                        "rank=" + result.rank());
            }
            if (result.accountId().equals(submitterAccountId)) {
                submitterIncluded = true;
            }

            accounts.require(result.accountId());
            grantRequests.add(new MedalLedgerStore.GrantRequest(
                    result.accountId(),
                    result.rank(),
                    policy.rewardFor(result.rank())));
        }
        if (!submitterIncluded) {
            throw new BadRequestException(
                    "submitterAccountId는 results의 참가 계정이어야 합니다",
                    "submitterAccountId=" + submitterAccountId);
        }

        List<MedalLedgerStore.GrantOutcome> outcomes =
                ledger.grantBatchOnce(matchId, submitterAccountId, grantRequests);

        List<ReceiptView> receipts = new ArrayList<>(results.size());
        for (int i = 0; i < results.size(); i++) {
            SubmittedPlayerResult result = results.get(i);
            MedalLedgerStore.GrantOutcome outcome = outcomes.get(i);
            // newBalance 는 응답 시점의 현재 잔액이다 — 지급 시점 스냅샷을 주면 중복 재수신 시 UI 잔액이 과거로 후퇴한다 (api.md 3-3).
            Account account = accounts.require(result.accountId());
            receipts.add(new ReceiptView(
                    account.accountId(),
                    account.nickname(),
                    outcome.grant().rank(),
                    outcome.grant().medalsAwarded(),
                    account.medalBalance(),
                    outcome.duplicate()));
        }
        return List.copyOf(receipts);
    }

    public List<ReceiptView> getStoredReceipts(String matchId) {
        List<MedalLedgerStore.AccountGrant> stored = ledger.findByMatch(matchId);
        List<ReceiptView> receipts = new ArrayList<>(stored.size());
        for (MedalLedgerStore.AccountGrant entry : stored) {
            Account account = accounts.require(entry.accountId());
            receipts.add(new ReceiptView(
                    account.accountId(),
                    account.nickname(),
                    entry.grant().rank(),
                    entry.grant().medalsAwarded(),
                    account.medalBalance(),
                    true));
        }
        return List.copyOf(receipts);
    }
}
