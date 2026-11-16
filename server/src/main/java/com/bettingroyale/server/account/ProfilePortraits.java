package com.bettingroyale.server.account;

import java.util.List;
import java.util.Set;

/**
 * 프로필 초상화 카탈로그 (S-UX-RQ5N9E).
 *
 * <p>ID 는 배열 순번이 아니라 그림을 알아볼 수 있는 이름이다 — 순번으로 저장하면 목록에 하나를
 * 끼워 넣는 순간 저장된 모든 계정의 프로필이 다른 그림으로 바뀐다.
 *
 * <p>⚠️ 이 목록은 클라이언트의 초상화 카탈로그와 <b>같은 ID 집합</b>이어야 한다. 서버가 소유를
 * 검증하므로 서버도 무엇이 존재하는지 알아야 한다. 한쪽만 늘리면 새 초상화를 고를 수 없거나
 * (서버가 400) 화면에 빈 칸이 뜬다.
 */
public final class ProfilePortraits {

    /** 현재 지원하는 초상화. 순서는 화면 정렬용일 뿐 저장값이 아니다. */
    public static final List<String> ALL = List.of(
            "hood_cat",
            "hood_bear",
            "hood_ranger",
            "hat_musketeer",
            "hood_rabbit");

    /**
     * 신규 계정의 기본 초상화. 모든 신규 계정이 같은 값을 받는다 — 무작위로 주지 않는다.
     * 값을 바꾸면 V5 마이그레이션의 기본값과 어긋나므로 새 마이그레이션을 함께 낸다.
     */
    public static final String DEFAULT = "hat_musketeer";

    /** 현재는 전부 기본 제공이다. 유료·해금 초상화가 생기면 이 목록이 갈라진다. */
    public static final List<String> GRANTED_ON_SIGNUP = ALL;

    /** DB 열 길이(VARCHAR(16))와 같다. 넘는 값은 저장 전에 막는다. */
    public static final int MAX_ID_LENGTH = 16;

    private static final Set<String> KNOWN = Set.copyOf(ALL);

    private ProfilePortraits() {
    }

    public static boolean isKnown(String portraitId) {
        return portraitId != null && KNOWN.contains(portraitId);
    }
}
