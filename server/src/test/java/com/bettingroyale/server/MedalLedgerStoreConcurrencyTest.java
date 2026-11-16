package com.bettingroyale.server;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;

import com.bettingroyale.server.account.AccountStore;
import com.bettingroyale.server.auth.AuthService;
import com.bettingroyale.server.common.ResultConflictException;
import com.bettingroyale.server.match.MatchResultService;
import com.bettingroyale.server.match.MedalLedgerStore;

/**
 * 정산 원자성·동시성 보장 검증 (선행 커밋 3ff8b950 의 in-memory 테스트를 DB(V2)로 이식).
 * 같은 보장을 이제 match_results PK(동시 최초 제출 직렬화)와 단일 트랜잭션이 제공한다.
 */
@SpringBootTest
class MedalLedgerStoreConcurrencyTest {

    @Autowired
    private AccountStore accounts;

    @Autowired
    private MedalLedgerStore ledger;

    @Autowired
    private MatchResultService service;

    @Autowired
    private AuthService auth;

    @Test
    void duplicateAccountInSingleBatchIsRejectedBeforeAnyBalanceChange() {
        var account = accounts.identify(uniqueNickname());

        IllegalArgumentException exception = assertThrows(
                IllegalArgumentException.class,
                () -> ledger.grantBatchOnce(
                        UUID.randomUUID().toString(),
                        account.accountId(),
                        List.of(
                                new MedalLedgerStore.GrantRequest(account.accountId(), 1, 100),
                                new MedalLedgerStore.GrantRequest(account.accountId(), 1, 100))));

        assertTrue(exception.getMessage().contains("같은 accountId"));
        assertEquals(0, accounts.require(account.accountId()).medalBalance(),
                "중복 accountId는 잔액 반영 전에 차단되어야 한다");
    }

    @Test
    void conflictingConcurrentBatchesCommitExactlyOneCompleteResult() throws Exception {
        var first = accounts.identify(uniqueNickname());
        var second = accounts.identify(uniqueNickname());
        String matchId = UUID.randomUUID().toString();
        CountDownLatch ready = new CountDownLatch(2);
        CountDownLatch start = new CountDownLatch(1);

        List<MatchResultService.SubmittedPlayerResult> resultA = List.of(
                new MatchResultService.SubmittedPlayerResult(first.accountId(), 1),
                new MatchResultService.SubmittedPlayerResult(second.accountId(), 2));
        List<MatchResultService.SubmittedPlayerResult> resultB = List.of(
                new MatchResultService.SubmittedPlayerResult(first.accountId(), 2),
                new MatchResultService.SubmittedPlayerResult(second.accountId(), 1));

        try (ExecutorService executor = Executors.newFixedThreadPool(2)) {
            Future<Boolean> firstSubmission = executor.submit(() -> submitAfterBarrier(
                    service, matchId, first.accountId(), resultA, ready, start));
            Future<Boolean> secondSubmission = executor.submit(() -> submitAfterBarrier(
                    service, matchId, first.accountId(), resultB, ready, start));

            ready.await();
            start.countDown();

            assertTrue(firstSubmission.get() ^ secondSubmission.get(),
                    "충돌하는 두 전체 결과 중 하나만 확정되어야 한다");
        }

        MedalLedgerStore.Grant firstGrant = ledger.find(matchId, first.accountId());
        MedalLedgerStore.Grant secondGrant = ledger.find(matchId, second.accountId());
        assertTrue(
                (firstGrant.rank() == 1 && secondGrant.rank() == 2)
                        || (firstGrant.rank() == 2 && secondGrant.rank() == 1),
                "한 제출의 순위 조합 전체가 원자적으로 남아야 한다");
        // 테스트 설정의 정책값(1위 100 / 2위 60)에 의존하지 않도록 지급 합만 검사한다.
        assertEquals(firstGrant.medalsAwarded() + secondGrant.medalsAwarded(),
                accounts.require(first.accountId()).medalBalance()
                        + accounts.require(second.accountId()).medalBalance(),
                "지급 원장과 잔액 합이 일치해야 한다");
    }

    /** -36: 응답 유실 재시도가 동시에 겹치는 폭풍 — 전부 성공하되 최초 정산은 정확히 한 번이어야 한다. */
    @Test
    void 같은_결과_동시_다중_제출은_모두_성공하고_정산은_한_번만_반영된다() throws Exception {
        var first = auth.signup(uniqueEmail(), "password-123", uniqueNickname());
        var second = auth.signup(uniqueEmail(), "password-123", uniqueNickname());
        String matchId = UUID.randomUUID().toString();
        List<MatchResultService.SubmittedPlayerResult> results = List.of(
                new MatchResultService.SubmittedPlayerResult(first.accountId(), 1),
                new MatchResultService.SubmittedPlayerResult(second.accountId(), 2));

        int submissions = 8;
        CountDownLatch ready = new CountDownLatch(submissions);
        CountDownLatch start = new CountDownLatch(1);
        List<Future<List<MatchResultService.ReceiptView>>> futures = new ArrayList<>(submissions);
        try (ExecutorService executor = Executors.newFixedThreadPool(submissions)) {
            for (int i = 0; i < submissions; i++) {
                futures.add(executor.submit(() -> {
                    ready.countDown();
                    start.await();
                    return service.submit(first.accountId(), matchId, results);
                }));
            }
            ready.await();
            start.countDown();

            int settlements = 0;
            for (Future<List<MatchResultService.ReceiptView>> future : futures) {
                // 같은 결과의 재시도는 어떤 타이밍에 겹쳐도 예외 없이 영수증을 받아야 한다.
                List<MatchResultService.ReceiptView> receipts = future.get();
                if (!receipts.get(0).duplicate()) {
                    settlements++;
                }
            }
            assertEquals(1, settlements, "동시 재시도 폭풍에서도 최초 정산은 정확히 한 번이어야 한다");
        }

        assertEquals(ledger.find(matchId, first.accountId()).medalsAwarded(),
                accounts.require(first.accountId()).medalBalance(),
                "잔액은 원장에 기록된 지급량과 정확히 같아야 한다 — 중복 반영 금지");
        assertEquals(ledger.find(matchId, second.accountId()).medalsAwarded(),
                accounts.require(second.accountId()).medalBalance());
    }

    /** -36: 서로 다른 결과 N개가 동시에 들어와도 하나의 결과만 확정되고 잔액이 원장과 일치해야 한다. */
    @Test
    void 서로_다른_결과_다중_동시_제출은_하나의_결과만_확정하고_잔액이_원장과_일치한다() throws Exception {
        var first = auth.signup(uniqueEmail(), "password-123", uniqueNickname());
        var second = auth.signup(uniqueEmail(), "password-123", uniqueNickname());
        String matchId = UUID.randomUUID().toString();
        List<MatchResultService.SubmittedPlayerResult> resultA = List.of(
                new MatchResultService.SubmittedPlayerResult(first.accountId(), 1),
                new MatchResultService.SubmittedPlayerResult(second.accountId(), 2));
        List<MatchResultService.SubmittedPlayerResult> resultB = List.of(
                new MatchResultService.SubmittedPlayerResult(first.accountId(), 2),
                new MatchResultService.SubmittedPlayerResult(second.accountId(), 1));

        int submissions = 8;
        CountDownLatch ready = new CountDownLatch(submissions);
        CountDownLatch start = new CountDownLatch(1);
        List<Future<Boolean>> futures = new ArrayList<>(submissions);
        try (ExecutorService executor = Executors.newFixedThreadPool(submissions)) {
            for (int i = 0; i < submissions; i++) {
                List<MatchResultService.SubmittedPlayerResult> payload = (i % 2 == 0) ? resultA : resultB;
                futures.add(executor.submit(() -> submitAfterBarrier(
                        service, matchId, first.accountId(), payload, ready, start)));
            }
            ready.await();
            start.countDown();

            int successes = 0;
            for (Future<Boolean> future : futures) {
                // ResultConflictException 외의 예외는 future.get 에서 터져 테스트를 실패시킨다.
                if (future.get()) {
                    successes++;
                }
            }
            assertTrue(successes >= 1, "충돌 속에서도 최소 한 제출은 확정되어야 한다");
        }

        MedalLedgerStore.Grant firstGrant = ledger.find(matchId, first.accountId());
        MedalLedgerStore.Grant secondGrant = ledger.find(matchId, second.accountId());
        assertTrue(
                (firstGrant.rank() == 1 && secondGrant.rank() == 2)
                        || (firstGrant.rank() == 2 && secondGrant.rank() == 1),
                "확정된 결과는 두 제출 중 정확히 하나의 순위 조합이어야 한다");
        assertEquals(firstGrant.medalsAwarded(), accounts.require(first.accountId()).medalBalance(),
                "잔액은 확정 원장과 정확히 일치해야 한다 — 진 제출의 흔적이 없어야 한다");
        assertEquals(secondGrant.medalsAwarded(), accounts.require(second.accountId()).medalBalance());
    }

    private static boolean submitAfterBarrier(
            MatchResultService service,
            String matchId,
            String submitterAccountId,
            List<MatchResultService.SubmittedPlayerResult> requests,
            CountDownLatch ready,
            CountDownLatch start) throws InterruptedException {
        ready.countDown();
        start.await();
        try {
            service.submit(submitterAccountId, matchId, requests);
            return true;
        } catch (ResultConflictException expected) {
            return false;
        }
    }

    private static String uniqueNickname() {
        return ("cc-" + UUID.randomUUID()).substring(0, 15);
    }

    private static String uniqueEmail() {
        return "cc-" + UUID.randomUUID().toString().substring(0, 8) + "@test.local";
    }
}
