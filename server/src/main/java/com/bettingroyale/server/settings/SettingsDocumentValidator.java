package com.bettingroyale.server.settings;

import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.time.format.DateTimeFormatter;
import java.time.format.DateTimeFormatterBuilder;
import java.time.format.DateTimeParseException;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.regex.Pattern;

import org.springframework.stereotype.Component;

import com.bettingroyale.server.common.BadRequestException;

import tools.jackson.databind.ObjectMapper;

/**
 * PUT 본문 검증 (docs/server/api.md 7절).
 *
 * <p>서버는 설정의 <b>의미</b>를 해석하지 않는다 — 값 문자열의 타입·범위 판정은 클라이언트 카탈로그의 몫이다
 * (S-UX-8FGK29). 서버가 보장하는 것은 봉투 구조·크기·메타데이터의 유계성뿐이며, 알 수 없는 category/option
 * 필드는 그대로 보존한다.
 *
 * <p>본문·토큰·설정 내용은 예외 메시지에도 detail 에도 넣지 않는다 — 필드 이름과 길이/개수만 남긴다
 * (api.md 6절, 7절 "설정·토큰·요청 본문은 로그에 남기지 않는다").
 *
 * <p>Jackson 3(Boot 4.1, {@code tools.jackson.*}) 의 트리 API 는 2.x 대비 접근자 이름이 바뀌었으므로
 * 여기서는 {@code Map}/{@code List} 로만 읽고 쓴다 — 파싱 순서가 보존되는 LinkedHashMap 이라
 * 재직렬화해도 필드 순서와 미지 필드가 유지된다.
 */
@Component
class SettingsDocumentValidator {

    /** 봉투 상한 64KiB (S-CLI-JZKPDF 작업 가정). 파싱 전에 먼저 막는다. */
    static final int MAX_DOCUMENT_BYTES = 64 * 1024;

    /** 현재 지원하는 봉투 버전은 1 하나다 (api.md 7절). */
    static final int SUPPORTED_ENVELOPE_VERSION = 1;

    /** 서버 수신 시각보다 5분을 넘는 미래 UpdatedAt 은 거부한다 (S-CLI-JZKPDF). */
    static final Duration MAX_FUTURE_SKEW = Duration.ofMinutes(5);

    /** 명백히 비정상인 과거 시각 하한 — 클라이언트 시계 미설정(1970) 값을 정본으로 굳히지 않는다. */
    static final Instant MIN_UPDATED_AT = Instant.parse("2000-01-01T00:00:00Z");

    private static final DateTimeFormatter CANONICAL_TIMESTAMP = new DateTimeFormatterBuilder()
            .appendInstant(3).toFormatter();

    static final String VERSION_FIELD = "version";
    static final String METADATA_FIELD = "metadata";
    static final String SETTINGS_VERSION_FIELD = "settingsVersion";
    static final String UPDATED_AT_FIELD = "updatedAt";
    static final String DEVICE_ID_FIELD = "deviceId";
    static final String VALUES_FIELD = "values";
    static final String BINDINGS_FIELD = "bindings";

    /** api.md 7절이 예시로 든 category 들. 이 밖의 키도 객체이기만 하면 보존한다. */
    static final Set<String> KNOWN_CATEGORIES = Set.of(
            "gameplay", "hud", "keyboardMouse", "controller", "audio", "graphics", "accessibility");

    /** 불투명한 설치 식별자 — GUID/해시 등을 허용하되 제어문자·공백·구분자는 막는다. */
    private static final Pattern DEVICE_ID_PATTERN = Pattern.compile("[A-Za-z0-9][A-Za-z0-9._:+=-]{0,127}");

    /** 알 수 없는 category 이름도 받아들이되 식별자 모양은 요구한다 (보존 대상이지 자유 텍스트가 아니다). */
    private static final Pattern CATEGORY_NAME_PATTERN = Pattern.compile("[A-Za-z][A-Za-z0-9._-]{0,63}");

    /** option id / actionId 는 점 표기 식별자다 ("hud.scale", "attack"). */
    private static final Pattern OPTION_ID_PATTERN = Pattern.compile("[A-Za-z0-9][A-Za-z0-9._:\\-\\[\\]/]{0,127}");

    private static final long MAX_SETTINGS_VERSION = 1_000_000L;
    private static final int MAX_CATEGORIES = 64;
    private static final int MAX_ENTRIES_PER_LIST = 512;
    private static final int MAX_OPTION_VALUE_LENGTH = 1024;
    private static final int MAX_BINDING_PATH_LENGTH = 256;
    private static final int MAX_CATEGORY_NAME_LENGTH = 64;
    private static final int MAX_METADATA_FIELDS = 32;
    private static final int MAX_UPDATED_AT_LENGTH = 64;

    /** 미지 필드 보존을 허용하되 유계로 둔다 — 깊이/폭/문자열 길이 상한. */
    private static final int MAX_DEPTH = 12;
    private static final int MAX_OBJECT_FIELDS = 128;
    private static final int MAX_ARRAY_ELEMENTS = 1024;
    private static final int MAX_STRING_LENGTH = 4096;

    private final ObjectMapper mapper;

    SettingsDocumentValidator(ObjectMapper mapper) {
        this.mapper = mapper;
    }

    /**
     * 본문을 검증하고 정본 문서를 만든다. 실패는 전부 {@code 400 VALIDATION_ERROR}.
     *
     * @param rawBody   요청 본문 원문 (null·빈 문자열 허용 — 400 으로 거부한다)
     * @param serverNow 서버 수신 시각 (미래 시각 판정 기준)
     */
    ValidatedSettings validate(String rawBody, Instant serverNow) {
        if (rawBody == null || rawBody.isBlank()) {
            throw invalid("설정 본문이 필요합니다", "body.missing");
        }
        int bytes = rawBody.getBytes(StandardCharsets.UTF_8).length;
        if (bytes > MAX_DOCUMENT_BYTES) {
            throw invalid("설정 문서가 너무 큽니다", "body.bytes=" + bytes + " limit=" + MAX_DOCUMENT_BYTES);
        }

        Object parsed = parse(rawBody);
        if (!(parsed instanceof Map<?, ?>)) {
            throw invalid("설정 문서는 JSON 객체여야 합니다", "body.root.type");
        }
        Map<String, Object> root = asObject(parsed);
        checkShape(root, 1, "body");

        int envelopeVersion = requireEnvelopeVersion(root);
        Map<String, Object> metadata = requireMetadata(root);
        long settingsVersion = requireSettingsVersion(metadata);
        Instant updatedAt = requireUpdatedAt(metadata, serverNow);
        String deviceId = requireDeviceId(metadata);
        requireCategories(root);

        // 응답 문서도 DB 비교 시각과 같아야 클라이언트의 LWW 판정이 일치한다.
        metadata.put(UPDATED_AT_FIELD, CANONICAL_TIMESTAMP.format(updatedAt));
        return new ValidatedSettings(serialize(root), envelopeVersion, settingsVersion, updatedAt, deviceId);
    }

    private Object parse(String rawBody) {
        try {
            return mapper.readValue(rawBody, Object.class);
        } catch (Exception ex) {
            // Jackson 3 의 파싱 예외는 unchecked 다. Exception 으로 받아 두면 체크 예외 여부와 무관하게 컴파일된다.
            // 원문·파서 메시지는 본문 내용을 흘릴 수 있으므로 detail 에 넣지 않는다.
            throw invalid("설정 JSON 형식이 올바르지 않습니다", "body.json.unparseable");
        }
    }

    private String serialize(Map<String, Object> document) {
        try {
            return mapper.writeValueAsString(document);
        } catch (Exception ex) {
            // 방금 파싱한 값이라 실패할 이유가 없다 — 발생하면 서버 결함(500)이다.
            throw new IllegalStateException("설정 문서를 직렬화할 수 없습니다", ex);
        }
    }

    private static int requireEnvelopeVersion(Map<String, Object> root) {
        Object value = root.get(VERSION_FIELD);
        if (value == null) {
            throw invalid("설정 문서에 version 이 필요합니다", "version.missing");
        }
        long version = requireIntegral(value, VERSION_FIELD);
        if (version != SUPPORTED_ENVELOPE_VERSION) {
            throw invalid("지원하지 않는 설정 버전입니다", "version.unsupported=" + version);
        }
        return SUPPORTED_ENVELOPE_VERSION;
    }

    private static Map<String, Object> requireMetadata(Map<String, Object> root) {
        Object value = root.get(METADATA_FIELD);
        if (!(value instanceof Map<?, ?>)) {
            throw invalid("설정 metadata 가 필요합니다", value == null ? "metadata.missing" : "metadata.type");
        }
        Map<String, Object> metadata = asObject(value);
        if (metadata.size() > MAX_METADATA_FIELDS) {
            throw invalid("설정 metadata 항목이 너무 많습니다", "metadata.fields=" + metadata.size());
        }
        return metadata;
    }

    private static long requireSettingsVersion(Map<String, Object> metadata) {
        Object value = metadata.get(SETTINGS_VERSION_FIELD);
        if (value == null) {
            throw invalid("설정 metadata.settingsVersion 이 필요합니다", "metadata.settingsVersion.missing");
        }
        long settingsVersion = requireIntegral(value, "metadata." + SETTINGS_VERSION_FIELD);
        if (settingsVersion < 0 || settingsVersion > MAX_SETTINGS_VERSION) {
            throw invalid("설정 스키마 버전이 올바르지 않습니다", "metadata.settingsVersion.range=" + settingsVersion);
        }
        return settingsVersion;
    }

    private static Instant requireUpdatedAt(Map<String, Object> metadata, Instant serverNow) {
        String text = requireString(metadata.get(UPDATED_AT_FIELD), "metadata." + UPDATED_AT_FIELD,
                MAX_UPDATED_AT_LENGTH);
        if (!text.endsWith("Z")) {
            // UTC 표기만 받는다 — 오프셋 표기를 허용하면 같은 순간이 두 문자열이 되어 LWW 비교가 흔들린다.
            throw invalid("설정 갱신 시각은 UTC(Z) 여야 합니다", "metadata.updatedAt.notUtc");
        }
        Instant parsed;
        try {
            parsed = Instant.parse(text);
        } catch (DateTimeParseException ex) {
            throw invalid("설정 갱신 시각 형식이 올바르지 않습니다", "metadata.updatedAt.format");
        }
        // DB(DATETIME(3)) 와 비교 정밀도를 맞춘다 — 저장 후 다시 읽어도 같은 값이어야 재시도가 멱등이다.
        Instant truncated = parsed.truncatedTo(ChronoUnit.MILLIS);
        if (truncated.isBefore(MIN_UPDATED_AT)) {
            throw invalid("설정 갱신 시각이 올바르지 않습니다", "metadata.updatedAt.tooOld");
        }
        if (truncated.isAfter(serverNow.plus(MAX_FUTURE_SKEW))) {
            throw invalid("설정 갱신 시각이 서버 시각보다 너무 앞섭니다", "metadata.updatedAt.future");
        }
        return truncated;
    }

    private static String requireDeviceId(Map<String, Object> metadata) {
        String deviceId = requireString(metadata.get(DEVICE_ID_FIELD), "metadata." + DEVICE_ID_FIELD, 128);
        if (!DEVICE_ID_PATTERN.matcher(deviceId).matches()) {
            throw invalid("설정 기기 식별자가 올바르지 않습니다", "metadata.deviceId.format");
        }
        return deviceId;
    }

    /** version·metadata 를 뺀 나머지 최상위 키는 전부 category 다 — 알 수 없는 이름도 객체이면 보존한다. */
    private static void requireCategories(Map<String, Object> root) {
        int categories = 0;
        for (Map.Entry<String, Object> entry : root.entrySet()) {
            String name = entry.getKey();
            if (VERSION_FIELD.equals(name) || METADATA_FIELD.equals(name)) {
                continue;
            }
            categories++;
            if (categories > MAX_CATEGORIES) {
                throw invalid("설정 분류가 너무 많습니다", "categories=" + categories);
            }
            if (name.length() > MAX_CATEGORY_NAME_LENGTH
                    || (!KNOWN_CATEGORIES.contains(name) && !CATEGORY_NAME_PATTERN.matcher(name).matches())) {
                throw invalid("설정 분류 이름이 올바르지 않습니다", "category.name.length=" + name.length());
            }
            if (!(entry.getValue() instanceof Map<?, ?>)) {
                throw invalid("설정 분류는 객체여야 합니다", "category.type");
            }
            requireCategory(name, asObject(entry.getValue()));
        }
    }

    private static void requireCategory(String name, Map<String, Object> category) {
        Object values = category.get(VALUES_FIELD);
        if (values != null) {
            List<Object> entries = requireArray(values, name + "." + VALUES_FIELD);
            for (Object element : entries) {
                Map<String, Object> option = requireEntryObject(element, name + "." + VALUES_FIELD);
                requireOptionId(option.get("id"), name + ".values.id");
                requireOptionalString(option.get("value"), name + ".values.value", MAX_OPTION_VALUE_LENGTH);
                // id/value 밖의 필드는 클라이언트 확장이다 — 검사하지 않고 그대로 보존한다.
            }
        }
        Object bindings = category.get(BINDINGS_FIELD);
        if (bindings != null) {
            List<Object> entries = requireArray(bindings, name + "." + BINDINGS_FIELD);
            for (Object element : entries) {
                Map<String, Object> binding = requireEntryObject(element, name + "." + BINDINGS_FIELD);
                requireOptionId(binding.get("actionId"), name + ".bindings.actionId");
                requireOptionalString(binding.get("path"), name + ".bindings.path", MAX_BINDING_PATH_LENGTH);
            }
        }
    }

    private static List<Object> requireArray(Object value, String field) {
        if (!(value instanceof List<?> list)) {
            throw invalid("설정 항목 목록이 올바르지 않습니다", field + ".type");
        }
        if (list.size() > MAX_ENTRIES_PER_LIST) {
            throw invalid("설정 항목이 너무 많습니다", field + ".size=" + list.size());
        }
        @SuppressWarnings("unchecked")
        List<Object> typed = (List<Object>) list;
        return typed;
    }

    private static Map<String, Object> requireEntryObject(Object element, String field) {
        if (!(element instanceof Map<?, ?>)) {
            throw invalid("설정 항목은 객체여야 합니다", field + ".element.type");
        }
        return asObject(element);
    }

    private static void requireOptionId(Object value, String field) {
        String id = requireString(value, field, 128);
        if (!OPTION_ID_PATTERN.matcher(id).matches()) {
            throw invalid("설정 항목 식별자가 올바르지 않습니다", field + ".format");
        }
    }

    private static void requireOptionalString(Object value, String field, int maxLength) {
        if (!(value instanceof String text) || text.length() > maxLength)
            throw invalid("설정 값 형식이 올바르지 않습니다", field + ".typeOrLength");
    }

    private static String requireString(Object value, String field, int maxLength) {
        if (!(value instanceof String text)) {
            throw invalid("설정 " + field + " 값이 올바르지 않습니다", value == null ? field + ".missing" : field + ".type");
        }
        if (text.isEmpty() || text.length() > maxLength) {
            throw invalid("설정 " + field + " 값이 올바르지 않습니다", field + ".length=" + text.length());
        }
        return text;
    }

    private static long requireIntegral(Object value, String field) {
        if (value instanceof Integer intValue) {
            return intValue.longValue();
        }
        if (value instanceof Long longValue) {
            return longValue;
        }
        if (value instanceof Short shortValue) {
            return shortValue.longValue();
        }
        if (value instanceof BigInteger bigValue) {
            try {
                return bigValue.longValueExact();
            } catch (ArithmeticException ex) {
                throw invalid("설정 " + field + " 값이 범위를 벗어났습니다", field + ".range");
            }
        }
        throw invalid("설정 " + field + " 값은 정수여야 합니다", field + ".type");
    }

    /** 미지 필드를 보존하되 무한정 받지는 않는다 — 깊이·폭·문자열 길이를 유계로 둔다. */
    private static void checkShape(Object node, int depth, String path) {
        if (depth > MAX_DEPTH) {
            throw invalid("설정 문서 구조가 너무 깊습니다", path + ".depth=" + depth);
        }
        if (node instanceof Map<?, ?> map) {
            if (map.size() > MAX_OBJECT_FIELDS) {
                throw invalid("설정 객체 필드가 너무 많습니다", path + ".fields=" + map.size());
            }
            for (Map.Entry<?, ?> entry : map.entrySet()) {
                Object key = entry.getKey();
                if (!(key instanceof String name) || name.isEmpty() || name.length() > MAX_STRING_LENGTH) {
                    throw invalid("설정 필드 이름이 올바르지 않습니다", path + ".key");
                }
                checkShape(entry.getValue(), depth + 1, path + "." + "*");
            }
        } else if (node instanceof List<?> list) {
            if (list.size() > MAX_ARRAY_ELEMENTS) {
                throw invalid("설정 배열이 너무 깁니다", path + ".size=" + list.size());
            }
            for (Object element : list) {
                checkShape(element, depth + 1, path + "[]");
            }
        } else if (node instanceof String text && text.length() > MAX_STRING_LENGTH) {
            throw invalid("설정 문자열이 너무 깁니다", path + ".length=" + text.length());
        }
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> asObject(Object value) {
        // JSON 객체의 키는 항상 문자열이며 checkShape 가 이를 확인한다.
        return (Map<String, Object>) value;
    }

    private static BadRequestException invalid(String message, String detail) {
        return new BadRequestException(message, detail);
    }
}
