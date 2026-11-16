package com.bettingroyale.server.wardrobe;

import java.time.Instant;

import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.bettingroyale.server.account.AccountEntity;
import com.bettingroyale.server.account.AccountRepository;
import com.bettingroyale.server.common.BadRequestException;

/**
 * 외형 의상을 메달로 산다 (S-GD-5R953J).
 *
 * <p><b>차감과 지급은 한 트랜잭션이다.</b> 둘이 갈라지면 메달만 빠지고 옷은 안 들어오거나
 * 그 반대가 된다. 메달은 지금까지 쌓이기만 했으므로 이것이 첫 소모처다.
 */
@Service
public class WardrobePurchaseService {

    private final AccountRepository accounts;
    private final WardrobeItemRepository catalog;
    private final AccountWardrobeItemRepository owned;

    public WardrobePurchaseService(AccountRepository accounts, WardrobeItemRepository catalog,
            AccountWardrobeItemRepository owned) {
        this.accounts = accounts;
        this.catalog = catalog;
        this.owned = owned;
    }

    /**
     * 의상 한 벌을 사고 남은 잔액을 돌려준다.
     *
     * <p><b>순서가 중요하다</b> — 지급을 먼저 시도하고 성공했을 때만 차감한다. 반대로 하면
     * 이미 소유한 옷을 다시 샀을 때 메달만 빠진다. 지급이 0행이면 이미 가진 것이므로
     * 아무것도 빼지 않고 현재 잔액을 그대로 돌려준다(멱등).
     *
     * @throws BadRequestException          카탈로그에 없는 ID, 또는 팔지 않는 기본 지급품
     * @throws InsufficientMedalsException  잔액 부족
     */
    @Transactional
    public long purchase(AccountEntity account, String itemId) {
        String requested = itemId == null ? "" : itemId.trim();
        WardrobeItemEntity item = catalog.findById(requested)
                .orElseThrow(() -> new BadRequestException(
                        "알 수 없는 외형 의상입니다", "itemId=" + requested));

        int price = item.priceMedals();
        if (price <= 0) {
            // 기본 지급품이라 살 대상이 아니다. 조용히 성공시키면 "샀는데 잔액이 그대로"가 된다.
            throw new BadRequestException("살 수 없는 외형 의상입니다", "itemId=" + requested + ", price=" + price);
        }

        int granted = owned.insertPurchase(account.accountId(), requested, Instant.now());
        if (granted == 0) {
            return currentBalance(account.accountId());
        }

        if (accounts.deductIfAffordable(account.accountId(), price) == 0) {
            // 차감이 실패하면 방금 지급한 것도 없던 일이 되어야 한다. 예외가 트랜잭션을 되돌린다.
            throw new InsufficientMedalsException(account.accountId(), requested, price);
        }

        return currentBalance(account.accountId());
    }

    private long currentBalance(String accountId) {
        return accounts.findById(accountId).map(AccountEntity::medalBalance).orElse(0L);
    }
}
