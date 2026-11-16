package com.bettingroyale.server;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyInt;
import static org.mockito.ArgumentMatchers.anyLong;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.reset;

import java.time.Instant;
import java.util.List;
import java.util.UUID;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.test.context.bean.override.mockito.MockitoSpyBean;

import com.bettingroyale.server.account.AccountStore;
import com.bettingroyale.server.auth.AuthService;
import com.bettingroyale.server.match.MatchResultRepository;
import com.bettingroyale.server.match.MedalGrantRepository;
import com.bettingroyale.server.match.MedalLedgerStore;

/**
 * -36 하드닝: 트랜잭션 도중의 진짜 DB 무결성 예외가 "동시 제출 경합 패배"로 위장되지 않는지.
 * 좁히기 전에는 원장 INSERT 의 DataIntegrityViolationException 이 경합으로 오인돼
 * 확정 기록 없는 재비교(IllegalStateException)로 왜곡됐다 — 지금은 원본 예외가 그대로 전파되고(500),
 * 트랜잭션 전체가 롤백되어 재시도가 처음부터 성공해야 한다.
 */
@SpringBootTest
class MedalLedgerDbFaultTest {

    @Autowired
    private AccountStore accounts;

    @Autowired
    private AuthService auth;

    @Autowired
    private MedalLedgerStore ledger;

    @Autowired
    private MatchResultRepository matchResults;

    @MockitoSpyBean
    private MedalGrantRepository grants;

    @Test
    void 원장_INSERT_의_DB_예외는_충돌로_왜곡되지_않고_전파되며_전체_롤백_후_재시도가_성공한다() {
        var winner = auth.signup(uniqueEmail(), "password-123", uniqueNickname());
        var runnerUp = auth.signup(uniqueEmail(), "password-123", uniqueNickname());
        String matchId = UUID.randomUUID().toString();
        List<MedalLedgerStore.GrantRequest> requests = List.of(
                new MedalLedgerStore.GrantRequest(winner.accountId(), 1, 100),
                new MedalLedgerStore.GrantRequest(runnerUp.accountId(), 2, 60));

        // 원장 기록은 명시적 INSERT 다 — save()(merge)는 행이 있으면 UPDATE 라 방어선이 안 된다(D-083).
        doThrow(new DataIntegrityViolationException("인위적 무결성 위반 — 일시적 DB 결함 재현"))
                .when(grants).insertGrant(anyString(), anyString(), anyInt(), anyLong(), anyLong(),
                        any(Instant.class));

        DataIntegrityViolationException propagated = assertThrows(
                DataIntegrityViolationException.class,
                () -> ledger.grantBatchOnce(matchId, winner.accountId(), requests),
                "원장 INSERT 의 DB 예외는 경합 패배로 오인되지 않고 원본 그대로 전파되어야 한다");
        assertTrue(propagated.getMessage().contains("인위적 무결성 위반"));

        assertEquals(0, accounts.require(winner.accountId()).medalBalance(),
                "실패한 제출은 어떤 계정의 잔액도 바꾸지 않아야 한다");
        assertEquals(0, accounts.require(runnerUp.accountId()).medalBalance());
        assertTrue(matchResults.findById(matchId).isEmpty(),
                "실패한 제출은 매치 서명도 남기지 않아야 한다 — 남으면 재시도가 영원히 409 다");

        reset(grants); // DB 결함이 사라진 뒤의 재시도
        List<MedalLedgerStore.GrantOutcome> retried =
                ledger.grantBatchOnce(matchId, winner.accountId(), requests);
        assertFalse(retried.get(0).duplicate(), "롤백이 완전했다면 재시도는 duplicate 가 아닌 최초 제출이다");
        assertEquals(100, accounts.require(winner.accountId()).medalBalance());
        assertEquals(60, accounts.require(runnerUp.accountId()).medalBalance());
    }

    private static String uniqueNickname() {
        return "dbf-" + UUID.randomUUID().toString().substring(0, 8);
    }

    private static String uniqueEmail() {
        return "dbfault-" + UUID.randomUUID().toString().substring(0, 8) + "@test.local";
    }
}
