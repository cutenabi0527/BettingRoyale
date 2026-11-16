package com.bettingroyale.server.common;

import org.springframework.http.HttpStatus;

public class ResultConflictException extends ApiException {

    public ResultConflictException(String matchId, String accountId, int existingRank, int submittedRank) {
        super(HttpStatus.CONFLICT, "RESULT_CONFLICT",
                "같은 매치에 다른 순위가 이미 제출되었습니다. 최초 제출이 유효합니다",
                "matchId=" + matchId + ", accountId=" + accountId
                        + ", existingRank=" + existingRank + ", submittedRank=" + submittedRank);
    }

    public ResultConflictException(String matchId, String reason) {
        super(HttpStatus.CONFLICT, "RESULT_CONFLICT",
                "같은 매치에 다른 결과가 이미 제출되었습니다. 최초 제출이 유효합니다",
                "matchId=" + matchId + ", reason=" + reason);
    }
}
