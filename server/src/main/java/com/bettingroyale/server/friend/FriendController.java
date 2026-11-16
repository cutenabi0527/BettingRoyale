package com.bettingroyale.server.friend;

import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.auth.AuthService;

/**
 * 친구 요청·목록 (docs/server/api.md 3-14~3-18, S15P21A606-202).
 *
 * <p>🔒 모든 엔드포인트가 Bearer JWT 필수다. <b>내 계정은 토큰에서만</b> 정한다 — 경로에도 본문에도
 * 내 계정 id 를 받지 않으므로 남인 척할 경로가 없다. 401 규약은 3-8 과 같다.
 *
 * <p>⚠️ <b>실시간 알림은 아직 없다.</b> D-BE-8TN47B 가 WebSocket 으로 정했으나 연결 인증이
 * O-012(JWT 보관·갱신)에 묶여 있다. 그때까지 클라이언트는 {@code GET /friends/requests} 를 폴링한다
 * (api.md 3-19).
 */
@RestController
@RequestMapping("/api/v1/friends")
public class FriendController {

    private final AuthService auth;
    private final FriendService friends;

    public FriendController(AuthService auth, FriendService friends) {
        this.auth = auth;
        this.friends = friends;
    }

    /** 성공 201(PENDING). 반대 방향 요청이 있었으면 200(ACCEPTED) 이다 — 그 자리에서 친구가 됐다는 뜻이다. */
    @PostMapping("/requests")
    public ResponseEntity<FriendViews.SendRequestResult> send(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @RequestBody(required = false) FriendViews.SendRequestBody body) {
        AccountEntity me = auth.authenticate(authorization);
        if (body == null) {
            throw new FriendExceptions.Invalid("본문이 필요합니다");
        }
        FriendViews.SendRequestResult result = friends.send(me.accountId(), body.targetNickname());
        HttpStatus status = FriendViews.SendRequestResult.ACCEPTED.equals(result.state())
                ? HttpStatus.OK
                : HttpStatus.CREATED;
        return ResponseEntity.status(status).body(result);
    }

    @GetMapping("/requests")
    public FriendViews.RequestList requests(
            @RequestHeader(value = "Authorization", required = false) String authorization) {
        return friends.list(auth.authenticate(authorization).accountId());
    }

    @PostMapping("/requests/{requestId}/accept")
    public FriendViews.Profile accept(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @PathVariable long requestId) {
        return friends.accept(auth.authenticate(authorization).accountId(), requestId);
    }

    @PostMapping("/requests/{requestId}/decline")
    public ResponseEntity<Void> decline(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @PathVariable long requestId) {
        friends.decline(auth.authenticate(authorization).accountId(), requestId);
        return ResponseEntity.noContent().build();
    }

    @GetMapping
    public FriendViews.FriendList friends(
            @RequestHeader(value = "Authorization", required = false) String authorization) {
        return friends.friends(auth.authenticate(authorization).accountId());
    }

    @DeleteMapping("/{accountId}")
    public ResponseEntity<Void> remove(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @PathVariable String accountId) {
        friends.remove(auth.authenticate(authorization).accountId(), accountId);
        return ResponseEntity.noContent().build();
    }
}
