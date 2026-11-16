package com.bettingroyale.server.common;

import org.springframework.http.HttpStatus;

public class MatchNotFoundException extends ApiException {

    public MatchNotFoundException(String matchId) {
        super(HttpStatus.NOT_FOUND, "MATCH_NOT_FOUND", "매치 결과가 존재하지 않습니다", "matchId=" + matchId);
    }
}
