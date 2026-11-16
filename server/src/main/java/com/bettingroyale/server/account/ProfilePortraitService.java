package com.bettingroyale.server.account;

import java.time.Instant;
import java.util.List;

import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.bettingroyale.server.common.BadRequestException;

/**
 * 프로필 초상화 소유 지급과 변경 (S-UX-RQ5N9E).
 *
 * <p>변경은 <b>알려진 ID 인가 → 소유했는가</b> 두 관문을 모두 통과해야 한다. 소유 검증을 서버가
 * 하므로 클라이언트가 목록을 어떻게 그리든 남의 초상화를 쓸 수 없다.
 */
@Service
public class ProfilePortraitService {

    private final AccountRepository accounts;
    private final AccountPortraitRepository portraits;

    public ProfilePortraitService(AccountRepository accounts, AccountPortraitRepository portraits) {
        this.accounts = accounts;
        this.portraits = portraits;
    }

    /** 이 계정이 소유한 초상화. 카탈로그 순서로 정렬해 화면이 매번 같은 차례로 그린다. */
    public List<String> ownedPortraitIds(String accountId) {
        List<String> owned = portraits.findPortraitIds(accountId);
        return ProfilePortraits.ALL.stream().filter(owned::contains).toList();
    }

    /**
     * 신규 계정에 기본 제공 초상화를 지급한다. 가입 트랜잭션 안에서 부른다.
     *
     * <p><b>멱등하다</b> — 이미 지급된 계정에 다시 불려도 행이 늘지 않고 실패하지도 않는다.
     * 지급 자체는 {@code save()} 가 아니라 리포지토리의 명시적 INSERT 다(D-BE-SG4AD6 / 레거시 D-083):
     * PK 를 우리가 지정하는 엔티티에서 {@code save()} 는 merge 라 행이 있으면 UPDATE 로 성공해
     * PK 를 방어선으로 삼은 보장이 통째로 무너진다.
     *
     * <p>목록을 한 문장으로 넣지 않고 장마다 한 문장을 쓰는 이유는, 초상화 카탈로그의 정본이
     * DB 표가 아니라 {@link ProfilePortraits}(클라이언트 카탈로그와 같은 ID 집합) 이기 때문이다.
     * 옷장은 {@code wardrobe_items} 라는 카탈로그 표가 있어 {@code INSERT ... SELECT} 한 문장으로 넣는다.
     */
    @Transactional
    public void grantSignupPortraits(String accountId) {
        Instant grantedAt = Instant.now();
        for (String portraitId : ProfilePortraits.GRANTED_ON_SIGNUP) {
            portraits.insertGrant(accountId, portraitId, grantedAt);
        }
    }

    /**
     * 프로필 초상화를 바꾸고 갱신된 계정을 돌려준다.
     *
     * @throws BadRequestException       카탈로그에 없는 ID
     * @throws PortraitNotOwnedException 소유하지 않은 초상화
     */
    @Transactional
    public AccountEntity changePortrait(AccountEntity account, String portraitId) {
        String requested = portraitId == null ? "" : portraitId.trim();
        if (!ProfilePortraits.isKnown(requested)) {
            throw new BadRequestException("알 수 없는 프로필 초상화입니다", "portraitId=" + requested);
        }
        if (!portraits.existsById(new AccountPortraitEntity.Key(account.accountId(), requested))) {
            throw new PortraitNotOwnedException(account.accountId(), requested);
        }

        // 같은 값이면 저장을 건너뛴다 — 화면이 저장을 여러 번 눌러도 행이 흔들리지 않는다.
        if (requested.equals(account.profilePortraitId())) {
            return account;
        }

        account.changeProfilePortrait(requested);
        return accounts.saveAndFlush(account);
    }
}
