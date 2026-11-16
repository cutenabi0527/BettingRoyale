package com.bettingroyale.server.telemetry;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.regex.Pattern;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.auth.AuthService;
import com.bettingroyale.server.common.BadRequestException;

import jakarta.validation.Valid;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;

/**
 * 대전 지표 수집 — 계약: docs/server/api.md 9절 · 스펙: S-GD-GSP9RQ.
 *
 * <p><b>이 엔드포인트의 실패는 매치 결과 제출(3-3)을 막지 않는다.</b> 별개 경로이며, 응답 202 는
 * "받았다" 까지만 뜻한다. 클라이언트는 실패를 로컬 버퍼에 두고 다음 실행에서 재전송한다(7-3).</p>
 *
 * <p>⚠️ <b>JWT 는 누가 보냈는지만 확인한다.</b> 내용의 진위는 보증하지 않는다 — 게임 룰 권위가
 * Host Mode 라 데이터가 플레이어 PC 에서 온다(D-030, O-002 미결). 밸런스 통계로는 감수하되
 * 랭킹·보상·제재에 쓰면 안 된다.</p>
 */
@RestController
@RequestMapping("/api/v1/telemetry/matches")
public class TelemetryController {

    public record ParticipantPayload(
            @NotBlank String accountId,
            @NotBlank String weapon,
            @NotNull @Min(1) Integer finalRank,
            @NotNull Integer finalScore,
            @NotNull @Min(0) Integer kills,
            @NotNull @Min(0) Integer deaths,
            @NotNull @Min(0) Integer assists,
            @NotNull @Min(0) Long damageDealt,
            @NotNull @Min(0) Long damageTaken,
            @NotNull @Min(0) Long goldEarned,
            @NotNull @Min(0) Long goldSpent,
            @NotNull @Min(0) Integer survivalSeconds) {
    }

    public record BlessingOfferPayload(
            @NotBlank String accountId,
            @NotNull @Min(1) Integer roundNo,
            @NotBlank String blessingId,
            @NotNull Boolean picked,
            @NotNull @Min(0) Integer rerollCount,
            @NotNull @Min(0) Long rerollGoldSpent,
            /** 클라만 아는 값이라 host 배치에는 없다. */
            Integer decisionMillis) {
    }

    public record MatchPayload(
            @NotBlank String mapId,
            @NotNull @Min(1) Integer playerCount,
            @NotNull @Min(0) Integer roundCount,
            @NotBlank String endedReason,
            @NotBlank String buildVersion,
            @NotBlank String dataVersion,
            @NotNull Instant startedAt,
            /** 방이 중간에 터지면 채워지지 않는다. */
            Instant endedAt) {
    }

    public record TelemetryRequest(
            @NotBlank String source,
            @NotNull @Min(1) Integer schemaVersion,
            @NotNull @Valid MatchPayload match,
            @Valid List<ParticipantPayload> participants,
            @Valid List<BlessingOfferPayload> blessingOffers) {
    }

    public record TelemetryResponse(String matchId, Map<String, Integer> accepted,
            Map<String, Integer> ignoredDuplicates) {
    }

    private static final Logger LOGGER = LoggerFactory.getLogger(TelemetryController.class);

    /** 3-3 과 같은 검증이다 — 짧은 임의 문자열이 오면 PK 가 매치 간에 겹친다. */
    private static final Pattern MATCH_ID_PATTERN =
            Pattern.compile("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

    private final AuthService auth;
    private final TelemetryService telemetry;

    public TelemetryController(AuthService auth, TelemetryService telemetry) {
        this.auth = auth;
        this.telemetry = telemetry;
    }

    @PostMapping("/{matchId}")
    @ResponseStatus(HttpStatus.ACCEPTED)
    public TelemetryResponse ingest(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @PathVariable String matchId,
            @Valid @RequestBody TelemetryRequest request) {
        // 본문 해석 전에 인증부터 끝낸다 — 인증되지 않은 요청에 검증 비용을 쓰지 않는다.
        AccountEntity account = auth.authenticate(authorization);
        requireUuidMatchId(matchId);

        List<ParticipantPayload> participantPayloads =
                request.participants() == null ? List.of() : request.participants();
        List<BlessingOfferPayload> offerPayloads =
                request.blessingOffers() == null ? List.of() : request.blessingOffers();

        List<TelemetryService.ParticipantInput> participantInputs = new ArrayList<>(participantPayloads.size());
        for (ParticipantPayload payload : participantPayloads) {
            participantInputs.add(new TelemetryService.ParticipantInput(
                    payload.accountId(), payload.weapon(), payload.finalRank(), payload.finalScore(),
                    payload.kills(), payload.deaths(), payload.assists(),
                    payload.damageDealt(), payload.damageTaken(),
                    payload.goldEarned(), payload.goldSpent(), payload.survivalSeconds()));
        }

        List<TelemetryService.BlessingOfferInput> offerInputs = new ArrayList<>(offerPayloads.size());
        for (BlessingOfferPayload payload : offerPayloads) {
            offerInputs.add(new TelemetryService.BlessingOfferInput(
                    payload.accountId(), payload.roundNo(), payload.blessingId(), payload.picked(),
                    payload.rerollCount(), payload.rerollGoldSpent(), payload.decisionMillis()));
        }

        MatchPayload match = request.match();
        TelemetryService.IngestResult result = telemetry.ingest(
                matchId, account.accountId(), request.source(), request.schemaVersion(),
                new TelemetryService.MatchInput(
                        match.mapId(), match.playerCount(), match.roundCount(), match.endedReason(),
                        match.buildVersion(), match.dataVersion(), match.startedAt(), match.endedAt()),
                participantInputs, offerInputs);

        // matchId grep 한 번으로 그 매치의 전 보고를 추적한다 (3-3 의 로그 규약과 같은 이유).
        LOGGER.info("telemetry ingested: matchId={}, source={}, reportedBy={}, "
                        + "accepted={}/{}/{}, duplicates={}/{}/{}",
                matchId, request.source(), account.accountId(),
                result.acceptedMatches(), result.acceptedParticipants(), result.acceptedOffers(),
                result.duplicateMatches(), result.duplicateParticipants(), result.duplicateOffers());

        return new TelemetryResponse(
                matchId,
                Map.of("match", result.acceptedMatches(),
                        "participants", result.acceptedParticipants(),
                        "blessingOffers", result.acceptedOffers()),
                Map.of("match", result.duplicateMatches(),
                        "participants", result.duplicateParticipants(),
                        "blessingOffers", result.duplicateOffers()));
    }

    private static void requireUuidMatchId(String matchId) {
        if (!MATCH_ID_PATTERN.matcher(matchId).matches()) {
            throw new BadRequestException("matchId 는 UUID 형식이어야 합니다", "matchId=" + matchId);
        }
    }
}
