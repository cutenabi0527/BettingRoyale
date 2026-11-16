package com.bettingroyale.server.friend;

import java.time.Instant;
import java.util.List;

/**
 * 친구 응답의 유선 형태 (docs/server/api.md 3-14~3-18).
 *
 * <p><b>어떤 형태에도 {@code loginId} 가 없다.</b> {@code accounts.login_id} 는 이메일이라
 * 친구에게 노출하면 이메일이 새어 나간다. 사람이 보는 식별자는 {@code nickname} 이다.
 * <b>외형도 없다</b> — 남의 외형은 대기방에서만 본다 (S-BE-KQ8928, api.md 3-11).
 */
public final class FriendViews {

    private FriendViews() {
    }

    /** 친구 한 명. 이 계약이 남에게 보여주는 전부다. */
    public record Profile(String accountId, String nickname) {
    }

    public record SendRequestBody(String targetNickname) {
    }

    /** 보낸 직후의 결과. 반대 방향 요청이 있었으면 state 가 ACCEPTED 다 (3-15). */
    public record SendRequestResult(Long requestId, String targetNickname, String state) {

        public static final String PENDING = "PENDING";
        public static final String ACCEPTED = "ACCEPTED";
    }

    public record IncomingRequest(long requestId, Profile from, Instant createdAt) {
    }

    public record OutgoingRequest(long requestId, Profile to, Instant createdAt) {
    }

    public record RequestList(List<IncomingRequest> incoming, List<OutgoingRequest> outgoing) {
    }

    public record FriendList(List<Profile> friends) {
    }
}
