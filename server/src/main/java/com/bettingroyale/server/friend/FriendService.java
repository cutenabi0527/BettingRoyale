package com.bettingroyale.server.friend;

import java.time.Instant;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.account.AccountRepository;

/**
 * 친구 요청·관계 규칙 (docs/server/api.md 3-15~3-18).
 *
 * <p>상대는 <b>닉네임으로만</b> 지목한다. 프로필이 친구 전용이라(S-BE-75FF7H) 목록을 훑어 고를 수 없고,
 * {@code login_id} 는 이메일이라 검색 키로 쓰면 이메일 존재 여부를 확인하는 통로가 된다.
 */
@Service
public class FriendService {

    private final AccountRepository accounts;
    private final FriendRequestRepository requests;
    private final FriendshipRepository friendships;

    public FriendService(
            AccountRepository accounts,
            FriendRequestRepository requests,
            FriendshipRepository friendships) {
        this.accounts = accounts;
        this.requests = requests;
        this.friendships = friendships;
    }

    /**
     * 친구 요청을 보낸다.
     *
     * <p><b>반대 방향의 대기 요청이 있으면 즉시 친구가 된다.</b> 둘 다 수락 대기로 두면 서로 상대의 수락을
     * 기다리며 멈춘다 — 사용자에게는 "보냈는데 아무 일도 안 일어남" 으로 보인다.
     */
    @Transactional
    public FriendViews.SendRequestResult send(String meAccountId, String targetNickname) {
        if (targetNickname == null || targetNickname.isBlank()) {
            throw new FriendExceptions.Invalid("targetNickname 이 비어 있습니다");
        }
        AccountEntity target = accounts.findByNickname(targetNickname.trim())
                .orElseThrow(() -> new FriendExceptions.NotFound("nickname=" + targetNickname));
        String targetId = target.accountId();
        if (targetId.equals(meAccountId)) {
            throw new FriendExceptions.Invalid("자기 자신에게는 보낼 수 없습니다");
        }
        if (friendships.existsBetween(meAccountId, targetId)) {
            throw new FriendExceptions.Conflict("이미 친구입니다");
        }
        if (requests.findByRequesterAccountIdAndAddresseeAccountId(meAccountId, targetId).isPresent()) {
            throw new FriendExceptions.Conflict("이미 보낸 요청이 있습니다");
        }

        Optional<FriendRequestEntity> opposite =
                requests.findByRequesterAccountIdAndAddresseeAccountId(targetId, meAccountId);
        if (opposite.isPresent()) {
            requests.delete(opposite.get());
            friendships.save(FriendshipEntity.of(meAccountId, targetId, now()));
            return new FriendViews.SendRequestResult(
                    null, target.nickname(), FriendViews.SendRequestResult.ACCEPTED);
        }

        FriendRequestEntity saved =
                requests.save(new FriendRequestEntity(meAccountId, targetId, now()));
        return new FriendViews.SendRequestResult(
                saved.requestId(), target.nickname(), FriendViews.SendRequestResult.PENDING);
    }

    /** 대기 중인 받은·보낸 요청. 처리된 요청은 행이 없으므로 자연히 빠진다. */
    @Transactional(readOnly = true)
    public FriendViews.RequestList list(String meAccountId) {
        List<FriendRequestEntity> incoming =
                requests.findByAddresseeAccountIdOrderByCreatedAtDesc(meAccountId);
        List<FriendRequestEntity> outgoing =
                requests.findByRequesterAccountIdOrderByCreatedAtDesc(meAccountId);

        List<String> ids = new ArrayList<>();
        incoming.forEach(r -> ids.add(r.requesterAccountId()));
        outgoing.forEach(r -> ids.add(r.addresseeAccountId()));
        Map<String, FriendViews.Profile> profiles = profilesOf(ids);

        List<FriendViews.IncomingRequest> in = incoming.stream()
                .map(r -> new FriendViews.IncomingRequest(
                        r.requestId(), profiles.get(r.requesterAccountId()), r.createdAt()))
                .toList();
        List<FriendViews.OutgoingRequest> out = outgoing.stream()
                .map(r -> new FriendViews.OutgoingRequest(
                        r.requestId(), profiles.get(r.addresseeAccountId()), r.createdAt()))
                .toList();
        return new FriendViews.RequestList(in, out);
    }

    /**
     * 받은 요청을 수락한다.
     *
     * <p>보낸 사람이 호출하면 {@link FriendExceptions.NotFound} 다 — 403 을 쓰면 그 id 가 존재한다는 것을
     * 알려주게 된다 (api.md 3-17).
     */
    @Transactional
    public FriendViews.Profile accept(String meAccountId, long requestId) {
        FriendRequestEntity request = mineAsAddressee(meAccountId, requestId);
        String otherId = request.requesterAccountId();
        requests.delete(request);
        if (!friendships.existsBetween(meAccountId, otherId)) {
            friendships.save(FriendshipEntity.of(meAccountId, otherId, now()));
        }
        return profileOf(otherId);
    }

    @Transactional
    public void decline(String meAccountId, long requestId) {
        requests.delete(mineAsAddressee(meAccountId, requestId));
    }

    /** 친구 목록. 닉네임 오름차순 (api.md 3-18). */
    @Transactional(readOnly = true)
    public FriendViews.FriendList friends(String meAccountId) {
        List<String> otherIds = friendships.findAllInvolving(meAccountId).stream()
                .map(f -> f.otherThan(meAccountId))
                .toList();
        List<FriendViews.Profile> profiles =
                new ArrayList<>(profilesOf(otherIds).values());
        profiles.sort(Comparator.comparing(FriendViews.Profile::nickname));
        return new FriendViews.FriendList(profiles);
    }

    /** 친구 삭제. 관계가 한 행이라 양쪽에서 동시에 사라진다. */
    @Transactional
    public void remove(String meAccountId, String otherAccountId) {
        FriendshipEntity friendship = friendships.findBetween(meAccountId, otherAccountId)
                .orElseThrow(() -> new FriendExceptions.NotFound("accountId=" + otherAccountId));
        friendships.delete(friendship);
    }

    private FriendRequestEntity mineAsAddressee(String meAccountId, long requestId) {
        return requests.findById(requestId)
                .filter(r -> r.addresseeAccountId().equals(meAccountId))
                .orElseThrow(() -> new FriendExceptions.NotFound("requestId=" + requestId));
    }

    private FriendViews.Profile profileOf(String accountId) {
        return accounts.findById(accountId)
                .map(a -> new FriendViews.Profile(a.accountId(), a.nickname()))
                .orElseThrow(() -> new FriendExceptions.NotFound("accountId=" + accountId));
    }

    /** id 목록을 한 번에 조회한다 — 목록 크기만큼 쿼리를 날리지 않는다. */
    private Map<String, FriendViews.Profile> profilesOf(List<String> accountIds) {
        Map<String, FriendViews.Profile> result = new HashMap<>();
        if (accountIds.isEmpty()) {
            return result;
        }
        accounts.findAllById(accountIds.stream().distinct().toList())
                .forEach(a -> result.put(a.accountId(), new FriendViews.Profile(a.accountId(), a.nickname())));
        return result;
    }

    /** 저장소의 다른 서비스와 같은 방식으로 시간을 얻는다 (UserSettingsStore·MedalLedgerStore). */
    private Instant now() {
        return Instant.now();
    }
}
