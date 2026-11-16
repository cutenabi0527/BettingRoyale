package com.bettingroyale.server.wardrobe;

import org.springframework.http.HttpStatus;

import com.bettingroyale.server.common.ApiException;

/** 메달이 모자라 외형 의상을 살 수 없다 (S-GD-5R953J). */
public class InsufficientMedalsException extends ApiException {

    public InsufficientMedalsException(String accountId, String itemId, int price) {
        super(HttpStatus.CONFLICT, "INSUFFICIENT_MEDALS",
                "메달이 부족합니다",
                "accountId=" + accountId + ", itemId=" + itemId + ", price=" + price);
    }
}
