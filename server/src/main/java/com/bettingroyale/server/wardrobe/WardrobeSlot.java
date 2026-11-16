package com.bettingroyale.server.wardrobe;

import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.function.Function;
import java.util.stream.Collectors;

/**
 * 외형 프리셋이 채울 수 있는 슬롯 (docs/server/api.md 8-1).
 *
 * <p>Body·Face 와 Cute Characters Mini 의 9개 물리 의상 슬롯을 합쳐 11개다. 선언 순서가 곧 응답 순서이므로
 * 화면이 매번 같은 차례로 그린다.
 *
 * <p>⚠️ <b>어떤 아이템이 어떤 슬롯에 들어갈 수 있는지는 여기서 정하지 않는다.</b> Costume 이 여러 슬롯을
 * 원자적으로 점유하는 규칙의 정본은 클라이언트 공용 카탈로그다(S-CLI-RQB4QD). 같은 규칙을 서버에도 적으면
 * 반드시 어긋난다 — 서버가 보는 것은 "아는 슬롯 이름인가" 뿐이다(D-BE-Z2HMR6).
 */
public enum WardrobeSlot {

    BODY("body"),
    FACE("face"),
    GLASSES("glasses"),
    GLOVES("gloves"),
    HAIRSTYLE("hairstyle"),
    HAT("hat"),
    MASK("mask"),
    OUTWEAR("outwear"),
    PANTS("pants"),
    SHORTS("shorts"),
    SHOE("shoe");

    /** DB 열 길이(VARCHAR(16))와 같다. 계약 이름이 이보다 길어지면 마이그레이션이 필요하다. */
    public static final int MAX_NAME_LENGTH = 16;

    private static final Map<String, WardrobeSlot> BY_CONTRACT_NAME = Arrays.stream(values())
            .collect(Collectors.toUnmodifiableMap(WardrobeSlot::contractName, Function.identity()));

    private static final List<String> CONTRACT_NAMES = Arrays.stream(values())
            .map(WardrobeSlot::contractName)
            .toList();

    private final String contractName;

    WardrobeSlot(String contractName) {
        this.contractName = contractName;
    }

    /** 계약·DB 에 나가는 이름. 소문자다 — 응답 필드명과 `slot` 열 값이 같은 문자열이어야 한다. */
    public String contractName() {
        return contractName;
    }

    /** 선언 순서 그대로의 계약 이름 목록. 조회 응답의 {@code slots} 가 이것이다. */
    public static List<String> contractNames() {
        return CONTRACT_NAMES;
    }

    /**
     * 계약 이름으로 슬롯을 찾는다. 대소문자는 가리지 않는다 — 클라이언트가 enum 이름을 그대로
     * 보내도({@code Hat}) 같은 슬롯으로 해석하고, 저장·응답은 언제나 정본 소문자로 정규화한다.
     */
    public static Optional<WardrobeSlot> find(String name) {
        if (name == null) {
            return Optional.empty();
        }
        return Optional.ofNullable(BY_CONTRACT_NAME.get(name.trim().toLowerCase(Locale.ROOT)));
    }
}
