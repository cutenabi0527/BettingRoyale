package com.bettingroyale.server.wardrobe;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Optional;

import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * 옷장 정책 값. 수치·목록은 코드가 아니라 application.yml 에만 둔다(server/AGENTS.md 규칙 2).
 *
 * <p>특히 {@code weaponTypes} 를 코드에 박지 않는 이유는 <b>무기 종류가 아직 흔들리기 때문</b>이다 —
 * 현재 확정 범위는 Sword / Bow 2종이고 Mace 는 구현 없는 예약 지점이다(S-GD-8DM55S).
 * 무기가 늘면 설정만 고친다. 스키마도 코드도 바뀌지 않는다(D-BE-Z2HMR6).
 */
@ConfigurationProperties(prefix = "wardrobe")
public class WardrobeProperties {

    private List<String> weaponTypes = new ArrayList<>();
    private int maxItemIdLength = 48;

    /**
     * 설정에 적힌 정본 표기를 돌려준다. 대소문자는 가리지 않고 찾되 저장·응답은 정본 표기로 통일한다 —
     * 같은 계정이 {@code sword} 와 {@code Sword} 로 서로 다른 프리셋을 갖는 일이 없어야 한다.
     */
    public Optional<String> canonicalWeaponType(String weaponType) {
        if (weaponType == null) {
            return Optional.empty();
        }
        String normalized = weaponType.trim().toLowerCase(Locale.ROOT);
        return weaponTypes.stream()
                .filter(known -> known.toLowerCase(Locale.ROOT).equals(normalized))
                .findFirst();
    }

    public List<String> getWeaponTypes() {
        return weaponTypes;
    }

    public void setWeaponTypes(List<String> weaponTypes) {
        this.weaponTypes = weaponTypes == null ? new ArrayList<>() : weaponTypes;
    }

    public int getMaxItemIdLength() {
        return maxItemIdLength;
    }

    public void setMaxItemIdLength(int maxItemIdLength) {
        this.maxItemIdLength = maxItemIdLength;
    }
}
