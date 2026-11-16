package com.bettingroyale.server.telemetry;

import java.time.Instant;
import java.util.List;
import java.util.Set;

import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.bettingroyale.server.common.BadRequestException;

/**
 * 대전 지표 배치를 받아 적는다. 계약: docs/server/api.md 9절 · 스펙: S-GD-GSP9RQ.
 *
 * <p><b>이 서비스는 매치 결과 제출(3-3)과 아무 관계가 없다.</b> 같은 트랜잭션에 들어가지 않고,
 * 여기서 무슨 일이 나도 메달 지급은 영향을 받지 않는다 — 분석 경로가 죽었다고 아무도 메달을 못
 * 받는 상황을 만들지 않기 위해서다(S-GD-GSP9RQ 7-1).</p>
 */
@Service
public class TelemetryService {

    /** 보고 주체. 값은 "누가 그것을 아는가" 로 갈린다 (S-GD-GSP9RQ 7-2). */
    private static final Set<String> SOURCES = Set.of("host", "client");

    /** 둔기는 예약된 확장 지점이라 구현이 없다 (S-GD-T8H7W7 · WeaponType.cs). */
    private static final Set<String> WEAPONS = Set.of("Sword", "Bow");

    public record ParticipantInput(
            String accountId, String weapon, int finalRank, int finalScore,
            int kills, int deaths, int assists,
            long damageDealt, long damageTaken, long goldEarned, long goldSpent, int survivalSeconds) {
    }

    public record BlessingOfferInput(
            String accountId, int roundNo, String blessingId, boolean picked,
            int rerollCount, long rerollGoldSpent, Integer decisionMillis) {
    }

    public record MatchInput(
            String mapId, int playerCount, int roundCount, String endedReason,
            String buildVersion, String dataVersion, Instant startedAt, Instant endedAt) {
    }

    /** 받은 수와 중복이라 건너뛴 수. 중복은 오류가 아니다 (재전송이 안전해야 한다, 7-3). */
    public record IngestResult(
            int acceptedMatches, int acceptedParticipants, int acceptedOffers,
            int duplicateMatches, int duplicateParticipants, int duplicateOffers) {
    }

    private final MatchTelemetryRepository matches;
    private final MatchParticipantRepository participants;
    private final BlessingOfferRepository offers;

    public TelemetryService(MatchTelemetryRepository matches,
            MatchParticipantRepository participants,
            BlessingOfferRepository offers) {
        this.matches = matches;
        this.participants = participants;
        this.offers = offers;
    }

    /**
     * 한 배치를 적는다. <b>이미 있는 PK 는 건너뛴다</b> — 같은 배치를 몇 번 보내도 행이 늘지 않는다.
     *
     * <p>⚠️ 존재 확인과 INSERT 사이의 경합은 배치를 통째로 실패시킨다. 그때는 클라이언트가 재전송하고
     * 두 번째에는 전부 중복으로 판정돼 성공한다. <b>텔레메트리라서 감수하는 선택이다</b> — 결과
     * 제출과 달리 유실돼도 게임·보상에 영향이 없다. 행마다 트랜잭션을 나누는 비용이 이 손실보다 크다.</p>
     */
    @Transactional
    public IngestResult ingest(String matchId, String reportedBy, String source, int schemaVersion,
            MatchInput match, List<ParticipantInput> participantInputs, List<BlessingOfferInput> offerInputs) {
        requireKnownSource(source);
        Instant now = Instant.now();

        int acceptedMatches = 0;
        int duplicateMatches = 0;
        if (matches.existsById(new MatchTelemetryEntity.Key(matchId, source))) {
            duplicateMatches++;
        } else {
            matches.insertMatch(matchId, source, reportedBy, schemaVersion,
                    match.mapId(), match.playerCount(), match.roundCount(), match.endedReason(),
                    match.buildVersion(), match.dataVersion(), match.startedAt(), match.endedAt(), now);
            acceptedMatches++;
        }

        int acceptedParticipants = 0;
        int duplicateParticipants = 0;
        for (ParticipantInput input : participantInputs) {
            requireKnownWeapon(input.weapon());
            if (participants.existsById(
                    new MatchParticipantEntity.Key(matchId, input.accountId(), source))) {
                duplicateParticipants++;
                continue;
            }
            participants.insertParticipant(matchId, input.accountId(), source, input.weapon(),
                    input.finalRank(), input.finalScore(), input.kills(), input.deaths(), input.assists(),
                    input.damageDealt(), input.damageTaken(), input.goldEarned(), input.goldSpent(),
                    input.survivalSeconds(), now);
            acceptedParticipants++;
        }

        int acceptedOffers = 0;
        int duplicateOffers = 0;
        for (BlessingOfferInput input : offerInputs) {
            if (offers.existsById(new BlessingOfferEntity.Key(
                    matchId, input.accountId(), input.roundNo(), input.blessingId(), source))) {
                duplicateOffers++;
                continue;
            }
            offers.insertOffer(matchId, input.accountId(), input.roundNo(), input.blessingId(), source,
                    input.picked() ? 1 : 0, input.rerollCount(), input.rerollGoldSpent(),
                    input.decisionMillis(), now);
            acceptedOffers++;
        }

        return new IngestResult(acceptedMatches, acceptedParticipants, acceptedOffers,
                duplicateMatches, duplicateParticipants, duplicateOffers);
    }

    private static void requireKnownSource(String source) {
        if (!SOURCES.contains(source)) {
            throw new BadRequestException("source 는 host 또는 client 여야 합니다", "source=" + source);
        }
    }

    private static void requireKnownWeapon(String weapon) {
        if (!WEAPONS.contains(weapon)) {
            throw new BadRequestException("weapon 은 Sword 또는 Bow 여야 합니다", "weapon=" + weapon);
        }
    }
}
