package com.bettingroyale.server.account;

import java.util.UUID;

/** accountId 발급 규칙 — identify(구)와 signup(신)이 같은 형식을 쓴다. */
public final class AccountIds {

    private AccountIds() {
    }

    /** 총 20자. 계약 상한은 32자 ASCII (api.md 3-2) — Fusion 고정 크기 슬롯에 안전하게 들어가도록 짧게 발급한다. */
    public static String newAccountId() {
        return "acc_" + UUID.randomUUID().toString().replace("-", "").substring(0, 16);
    }
}
