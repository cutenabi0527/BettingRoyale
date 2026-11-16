package com.bettingroyale.server.room;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 방 배정의 계약 오류 (docs/server/api.md 10절). */
public final class RoomExceptions {

    private RoomExceptions() {
    }

    /** 티켓이 없거나 내 것이 아니다. 남의 티켓 id 로 존재 여부를 알아내지 못하게 둘 다 404 다. */
    public static final class TicketNotFound extends ApiException {
        public TicketNotFound(String detail) {
            super(HttpStatus.NOT_FOUND, "TICKET_NOT_FOUND", "방 요청을 찾을 수 없습니다", detail);
        }
    }

    public static final class Invalid extends ApiException {
        public Invalid(String detail) {
            super(HttpStatus.BAD_REQUEST, "VALIDATION_ERROR", "요청이 올바르지 않습니다", detail);
        }
    }

    /** 게임 서버 키가 없거나 틀렸다. */
    public static final class ServerKeyInvalid extends ApiException {
        public ServerKeyInvalid() {
            super(HttpStatus.UNAUTHORIZED, "SERVER_KEY_INVALID", "게임 서버 인증에 실패했습니다", "X-Game-Server-Key 불일치");
        }
    }

    /** 이 환경에 게임 서버 키가 설정되지 않아 배정 기능이 꺼져 있다. */
    public static final class Disabled extends ApiException {
        public Disabled() {
            super(HttpStatus.SERVICE_UNAVAILABLE, "GAME_SERVER_DISABLED", "이 환경은 전용 게임 서버를 쓰지 않습니다",
                    "GAME_SERVER_KEY 미설정");
        }
    }
}
