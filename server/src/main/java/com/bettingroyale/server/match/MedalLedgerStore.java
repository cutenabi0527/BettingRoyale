package com.bettingroyale.server.match;

import java.time.Instant;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.stereotype.Service;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

import com.bettingroyale.server.account.AccountRepository;
import com.bettingroyale.server.common.ResultConflictException;

/**
 * 메달 지급 원장 — MySQL 영속 (V2, -35). 멱등키 = (matchId, accountId) — 계약(docs/server/api.md 1절).
 *
 * <p>in-memory 시절의 보장을 DB 로 옮겼다 (선행 커밋 3ff8b950 의 방침):
 * <ul>
 *   <li>매치 단위 원자성 — 최초 제출의 서명(제출자)·원장·잔액 갱신이 <b>한 트랜잭션</b>이다.
 *       충돌이 있으면 어떤 계정의 잔액도 바뀌지 않는다.</li>
 *   <li>동시 최초 제출 직렬화 — striped lock 대신 match_results 의 PK 가 맡는다.
 *       진 쪽은 INSERT 충돌로 트랜잭션 전체가 롤백되고, 확정된 결과와 다시 비교된다.</li>
 *   <li>중복 지급 방지 — medal_grants 의 PK(matchId, accountId)가 최종 방어선이다.</li>
 * </ul></p>
 */
@Service
public class MedalLedgerStore {

    /**
     * 동시 최초 제출 경합 패배 — {@code match_results} 서명 INSERT 의 PK 충돌에서만 만들어진다.
     * 그 외의 {@link DataIntegrityViolationException}(원장 INSERT 실패 등)은 진짜 DB 결함이므로
     * 이 예외로 변환하지 않고 그대로 전파해 500 으로 드러낸다.
     */
    private static final class FirstSubmissionLostException extends RuntimeException {
        private FirstSubmissionLostException(DataIntegrityViolationException cause) {
            super("동시 최초 제출 경합에서 밀렸습니다", cause);
        }
    }

    public record Grant(int rank, long medalsAwarded, long balanceAfter) {
    }

    public record GrantOutcome(Grant grant, boolean duplicate) {
    }

    public record GrantRequest(String accountId, int rank, long amount) {
    }

    public record AccountGrant(String accountId, Grant grant) {
    }

    private final MatchResultRepository matchResults;
    private final MedalGrantRepository grants;
    private final AccountRepository accounts;
    private final TransactionTemplate transaction;

    public MedalLedgerStore(MatchResultRepository matchResults, MedalGrantRepository grants,
            AccountRepository accounts, PlatformTransactionManager transactionManager) {
        this.matchResults = matchResults;
        this.grants = grants;
        this.accounts = accounts;
        this.transaction = new TransactionTemplate(transactionManager);
    }

    public Grant find(String matchId, String accountId) {
        return grants.findById(new MedalGrantEntity.Key(matchId, accountId))
                .map(MedalLedgerStore::toGrant)
                .orElse(null);
    }

    /** 한 매치의 지급 기록 전체를 rank 오름차순으로. 기록이 없으면 빈 목록. */
    public List<AccountGrant> findByMatch(String matchId) {
        return grants.findByMatchIdOrderByPlayerRankAsc(matchId).stream()
                .map(entity -> new AccountGrant(entity.accountId(), toGrant(entity)))
                .toList();
    }

    /**
     * 한 매치의 전체 결과를 지급한다. 최초 제출이면 서명·원장·잔액을 한 트랜잭션으로 확정하고,
     * 확정된 매치면 제출자·전체 (accountId, rank) 집합이 같을 때만 duplicate 영수증을 돌려준다.
     * 다르면 409 — 최초 제출이 이긴다 (api.md 3-3).
     */
    public List<GrantOutcome> grantBatchOnce(
            String matchId, String submitterAccountId, List<GrantRequest> requests) {
        if (matchId == null || submitterAccountId == null || submitterAccountId.isBlank() || requests == null) {
            throw new IllegalArgumentException("matchId, submitterAccountId와 requests는 필수입니다");
        }
        Map<String, Integer> submittedRanks = new HashMap<>();
        for (GrantRequest request : requests) {
            requireValidRequest(request);
            Integer previousRank = submittedRanks.putIfAbsent(request.accountId(), request.rank());
            if (previousRank != null) {
                if (previousRank != request.rank()) {
                    throw new ResultConflictException(matchId, request.accountId(), previousRank, request.rank());
                }
                throw new IllegalArgumentException("같은 배치에 같은 accountId를 두 번 넣을 수 없습니다");
            }
        }
        if (requests.isEmpty()) {
            return List.of();
        }

        if (matchResults.findById(matchId).isEmpty()) {
            try {
                return transaction.execute(status -> settleFirstSubmission(matchId, submitterAccountId, requests));
            } catch (FirstSubmissionLostException race) {
                // 동시 최초 제출에서 졌다 — 아무 잔액도 바뀌지 않았고, 아래에서 확정 결과와 비교한다.
                // 그 외 무결성 위반은 여기서 잡지 않는다 — DB 결함을 409/duplicate 로 위장하면 안 된다.
            }
        }
        return resolveAgainstStored(matchId, submitterAccountId, submittedRanks, requests);
    }

    /** 최초 제출 확정 — 서명 INSERT 가 가장 먼저라, 동시 제출은 여기서 즉시 충돌한다. */
    private List<GrantOutcome> settleFirstSubmission(
            String matchId, String submitterAccountId, List<GrantRequest> requests) {
        try {
            // ⚠️ save() 가 아니라 명시적 INSERT 여야 한다 — save() 는 merge 라 행이 있으면 UPDATE 로
            //    조용히 성공하고, 그러면 진 제출이 승자의 서명을 덮어쓰며 잔액을 이중 지급한다(D-083).
            //    이 INSERT 의 무결성 위반은 PK(match_id) 충돌뿐이다 — 경합 패배로 확정할 수 있다.
            matchResults.insertSignature(matchId, submitterAccountId, Instant.now());
        } catch (DataIntegrityViolationException race) {
            throw new FirstSubmissionLostException(race);
        }

        List<GrantOutcome> outcomes = new ArrayList<>(requests.size());
        for (GrantRequest request : requests) {
            if (accounts.addToBalance(request.accountId(), request.amount()) != 1) {
                // 서비스가 계정 존재를 사전 검사하므로 도달하면 결함이다 — 트랜잭션 전체가 롤백된다.
                throw new IllegalStateException("지급 대상 계정을 찾을 수 없습니다: " + request.accountId());
            }
            long newBalance = accounts.findById(request.accountId())
                    .orElseThrow(() -> new IllegalStateException("잔액 갱신 직후 계정 조회 실패: " + request.accountId()))
                    .medalBalance();
            // 여기도 명시적 INSERT — 멱등키 PK 를 중복 지급의 최종 방어선으로 실제로 작동시킨다(D-083).
            grants.insertGrant(matchId, request.accountId(), request.rank(), request.amount(),
                    newBalance, Instant.now());
            outcomes.add(new GrantOutcome(new Grant(request.rank(), request.amount(), newBalance), false));
        }
        return List.copyOf(outcomes);
    }

    /** 이미 확정된 매치와 비교 — 같은 결과면 duplicate, 다르면 최초 제출이 이긴다(409). */
    private List<GrantOutcome> resolveAgainstStored(
            String matchId, String submitterAccountId, Map<String, Integer> submittedRanks,
            List<GrantRequest> requests) {
        Optional<MatchResultEntity> stored = matchResults.findById(matchId);
        if (stored.isEmpty()) {
            throw new IllegalStateException("확정 기록이 없는 매치의 재비교: " + matchId);
        }
        if (!stored.get().submitterAccountId().equals(submitterAccountId)) {
            throw new ResultConflictException(matchId, "submitterAccountId mismatch");
        }

        Map<String, MedalGrantEntity> storedGrants = new HashMap<>();
        for (MedalGrantEntity grant : grants.findByMatchIdOrderByPlayerRankAsc(matchId)) {
            storedGrants.put(grant.accountId(), grant);
        }
        for (Map.Entry<String, Integer> submitted : submittedRanks.entrySet()) {
            MedalGrantEntity existing = storedGrants.get(submitted.getKey());
            if (existing != null && existing.playerRank() != submitted.getValue()) {
                throw new ResultConflictException(
                        matchId, submitted.getKey(), existing.playerRank(), submitted.getValue());
            }
        }
        if (storedGrants.size() != submittedRanks.size() || !storedGrants.keySet().equals(submittedRanks.keySet())) {
            throw new ResultConflictException(matchId, "participant or rank set mismatch");
        }

        List<GrantOutcome> outcomes = new ArrayList<>(requests.size());
        for (GrantRequest request : requests) {
            outcomes.add(new GrantOutcome(toGrant(storedGrants.get(request.accountId())), true));
        }
        return List.copyOf(outcomes);
    }

    private static Grant toGrant(MedalGrantEntity entity) {
        return new Grant(entity.playerRank(), entity.medalsAwarded(), entity.balanceAfter());
    }

    private static void requireValidRequest(GrantRequest request) {
        if (request == null
                || request.accountId() == null
                || request.accountId().isBlank()
                || request.rank() < 1
                || request.amount() < 0) {
            throw new IllegalArgumentException("지급 요청의 계정, 순위, 금액이 올바르지 않습니다");
        }
    }
}
