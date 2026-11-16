package com.bettingroyale.server.match;

import java.util.ArrayList;
import java.util.List;
import java.util.regex.Pattern;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.bettingroyale.server.common.MatchNotFoundException;
import com.bettingroyale.server.common.BadRequestException;

import jakarta.validation.Valid;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.NotNull;

/** 계약: docs/server/api.md 3-3. Host 가 매치 종료 시 전체 결과를 제출한다. 재전송에 안전하다. */
@RestController
@RequestMapping("/api/v1/matches")
public class MatchResultController {

    public record PlayerResult(@NotBlank String accountId, @NotNull @Min(1) Integer rank) {
    }

    public record MatchResultRequest(
            @NotBlank String submitterAccountId,
            @NotEmpty @Valid List<PlayerResult> results) {
    }

    public record MedalReceipt(String accountId, String nickname, int rank, long medalsAwarded, long newBalance,
            boolean duplicate) {
    }

    public record MatchResultResponse(String matchId, List<MedalReceipt> receipts) {
    }

    private static final Logger LOGGER = LoggerFactory.getLogger(MatchResultController.class);

    /** matchId 는 Host 가 생성하는 UUID v4 다 — 짧은 임의 문자열이 오면 멱등키가 매치 간에 겹친다 (api.md 3-3). */
    private static final Pattern MATCH_ID_PATTERN =
            Pattern.compile("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

    private final MatchResultService matchResults;

    public MatchResultController(MatchResultService matchResults) {
        this.matchResults = matchResults;
    }

    @PostMapping("/{matchId}/results")
    public MatchResultResponse submit(@PathVariable String matchId, @Valid @RequestBody MatchResultRequest request) {
        requireUuidMatchId(matchId);
        List<MatchResultService.SubmittedPlayerResult> submittedResults = new ArrayList<>(request.results().size());
        for (int i = 0; i < request.results().size(); i++) {
            PlayerResult result = request.results().get(i);
            submittedResults.add(new MatchResultService.SubmittedPlayerResult(result.accountId(), result.rank()));
        }

        List<MatchResultService.ReceiptView> handled = matchResults.submit(
                request.submitterAccountId(),
                matchId,
                submittedResults);

        List<MedalReceipt> receipts = new ArrayList<>(handled.size());
        for (MatchResultService.ReceiptView receipt : handled) {
            receipts.add(new MedalReceipt(
                    receipt.accountId(),
                    receipt.nickname(),
                    receipt.rank(),
                    receipt.medalsAwarded(),
                    receipt.newBalance(),
                    receipt.duplicate()));
        }
        // 오류 WARN(GlobalExceptionHandler)과 함께 matchId grep 한 번으로 매치의 전 요청을 추적한다 (-36).
        // duplicate 는 전체가 함께 정해진다(부분 중복 없음, api.md 3-3) — 첫 영수증이 배치 전체를 대표한다.
        if (!handled.isEmpty()) {
            LOGGER.info("match result {}: matchId={}, submitter={}, players={}",
                    handled.get(0).duplicate() ? "duplicate" : "settled",
                    matchId, request.submitterAccountId(), handled.size());
        }
        return new MatchResultResponse(matchId, receipts);
    }

    /**
     * 저장된 확정 결과 조회 — 409 를 받은 클라이언트의 복구 경로 (docs/server/api.md 3-5).
     * 조회는 신규 지급이 아니므로 영수증의 duplicate 는 항상 true 다.
     */
    @GetMapping("/{matchId}/results")
    public MatchResultResponse getResults(@PathVariable String matchId) {
        requireUuidMatchId(matchId);
        List<MatchResultService.ReceiptView> stored = matchResults.getStoredReceipts(matchId);
        if (stored.isEmpty()) {
            throw new MatchNotFoundException(matchId);
        }
        List<MedalReceipt> receipts = new ArrayList<>(stored.size());
        for (MatchResultService.ReceiptView receipt : stored) {
            receipts.add(new MedalReceipt(
                    receipt.accountId(),
                    receipt.nickname(),
                    receipt.rank(),
                    receipt.medalsAwarded(),
                    receipt.newBalance(),
                    receipt.duplicate()));
        }
        return new MatchResultResponse(matchId, receipts);
    }

    private static void requireUuidMatchId(String matchId) {
        if (!MATCH_ID_PATTERN.matcher(matchId).matches()) {
            throw new BadRequestException("matchId 는 UUID 형식이어야 합니다", "matchId=" + matchId);
        }
    }
}
