using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 값 검증 결과 요약. 저장/로드 경로에서 무엇이 교정됐는지 보고하기 위해 쓴다.
    /// </summary>
    public struct SettingsValidationReport
    {
        /// <summary>범위를 벗어나 잘린 슬라이더 값 수.</summary>
        public int Clamped;

        /// <summary>형식이 깨져 기본값으로 되돌린 값 수.</summary>
        public int Reverted;

        /// <summary>길이 제한 등으로 폐기한 항목 수.</summary>
        public int Dropped;

        /// <summary>정의에 없지만 그대로 보존한 항목 수.</summary>
        public int UnknownPreserved;

        public bool HasCorrections
        {
            get { return Clamped > 0 || Reverted > 0 || Dropped > 0; }
        }
    }

    /// <summary>
    /// 정의 표 기준의 순수 값 검증. 직렬화나 파일 접근에 의존하지 않으므로 단위 테스트가 쉽다.
    /// 알 수 없는 ID/액션은 파괴하지 않고 보존한다(다른 버전 클라이언트 보호).
    /// </summary>
    public static class SettingsValidation
    {
        /// <summary>
        /// 정의 기준으로 값을 교정한다. 형식이 깨졌으면 기본값을 돌려준다.
        /// 동적 선택지(해상도/주사율)는 하드웨어 목록을 여기서 알 수 없으므로 그대로 통과시킨다.
        /// </summary>
        public static string NormalizeValue(SettingDefinition definition, string raw, out bool corrected)
        {
            corrected = false;
            if (definition == null)
            {
                return raw;
            }

            var value = raw ?? string.Empty;
            switch (definition.Kind)
            {
                case SettingKind.Toggle:
                {
                    if (SettingsSchema.TryParseBool(value, out var parsed))
                    {
                        var normalized = SettingsSchema.FormatBool(parsed);
                        corrected = !string.Equals(normalized, value, StringComparison.Ordinal);
                        return normalized;
                    }

                    corrected = true;
                    return definition.DefaultValue;
                }

                case SettingKind.Slider:
                {
                    if (SettingsSchema.TryParseFloat(value, out var parsed))
                    {
                        if (float.IsNaN(parsed) || float.IsInfinity(parsed))
                        {
                            corrected = true;
                            return definition.DefaultValue;
                        }

                        var clamped = definition.Clamp(parsed);
                        var normalized = SettingsSchema.FormatFloat(clamped);
                        corrected = !string.Equals(normalized, value, StringComparison.Ordinal);
                        return normalized;
                    }

                    corrected = true;
                    return definition.DefaultValue;
                }

                case SettingKind.Choice:
                {
                    if (definition.HasDynamicChoices)
                    {
                        return value;
                    }

                    if (definition.HasChoice(value))
                    {
                        return value;
                    }

                    corrected = true;
                    return definition.DefaultValue;
                }

                case SettingKind.Binding:
                {
                    return NormalizeBindingPath(value);
                }

                default:
                    return value;
            }
        }

        public static string NormalizeValue(string id, string raw)
        {
            var definition = SettingsDefinitions.Find(SettingsDefinitions.CanonicalId(id));
            return NormalizeValue(definition, raw, out _);
        }

        public static string NormalizeBindingPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var trimmed = path.Trim();
            return trimmed.Length > SettingsSchema.MaxValueLength
                ? string.Empty
                : trimmed;
        }

        /// <summary>
        /// 문서 전체를 제자리에서 교정한다. 잘못된 값은 기본값으로 되돌리고,
        /// 범위를 벗어난 값은 자르며, 지나치게 긴 항목은 폐기한다.
        /// </summary>
        public static SettingsValidationReport Validate(UserSettings settings)
        {
            var report = new SettingsValidationReport();
            if (settings == null)
            {
                return report;
            }

            settings.Normalize();

            foreach (var category in SettingsSchema.Categories())
            {
                var section = settings.Section(category);
                ValidateValues(section, ref report);
                ValidateBindings(section, ref report);
            }

            return report;
        }

        private static void ValidateValues(SettingsSection section, ref SettingsValidationReport report)
        {
            var kept = new List<SettingValue>(section.values.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < section.values.Length; i++)
            {
                var entry = section.values[i];
                if (entry == null ||
                    string.IsNullOrEmpty(entry.id) ||
                    entry.id.Length > SettingsSchema.MaxIdLength ||
                    (entry.value != null && entry.value.Length > SettingsSchema.MaxValueLength) ||
                    !seen.Add(entry.id) ||
                    kept.Count >= SettingsSchema.MaxEntriesPerSection)
                {
                    report.Dropped++;
                    continue;
                }

                var definition = SettingsDefinitions.Find(entry.id);
                if (definition == null)
                {
                    report.UnknownPreserved++;
                    kept.Add(entry);
                    continue;
                }

                if (definition.Kind == SettingKind.Binding)
                {
                    // 바인딩은 bindings 배열에만 존재해야 한다. 값 슬롯으로 온 항목은 폐기한다.
                    report.Dropped++;
                    continue;
                }

                var normalized = NormalizeValue(definition, entry.value, out var corrected);
                if (corrected)
                {
                    if (definition.Kind == SettingKind.Slider &&
                        SettingsSchema.TryParseFloat(entry.value, out _))
                    {
                        report.Clamped++;
                    }
                    else
                    {
                        report.Reverted++;
                    }

                    entry.value = normalized;
                }

                kept.Add(entry);
            }

            if (kept.Count != section.values.Length)
            {
                section.values = kept.ToArray();
            }
        }

        private static void ValidateBindings(SettingsSection section, ref SettingsValidationReport report)
        {
            var kept = new List<SettingsBinding>(section.bindings.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < section.bindings.Length; i++)
            {
                var entry = section.bindings[i];
                if (entry == null ||
                    string.IsNullOrEmpty(entry.actionId) ||
                    entry.actionId.Length > SettingsSchema.MaxIdLength ||
                    !seen.Add(entry.actionId) ||
                    kept.Count >= SettingsSchema.MaxEntriesPerSection)
                {
                    report.Dropped++;
                    continue;
                }

                var normalized = NormalizeBindingPath(entry.path);
                if (!string.Equals(normalized, entry.path ?? string.Empty, StringComparison.Ordinal))
                {
                    report.Reverted++;
                    entry.path = normalized;
                }

                kept.Add(entry);
            }

            if (kept.Count != section.bindings.Length)
            {
                section.bindings = kept.ToArray();
            }
        }

        /// <summary>
        /// 한 분류 안에서 같은 경로를 공유하는 액션 쌍을 보고한다(자동 수정은 하지 않는다).
        /// 사용자가 명시적으로 재지정할 때만 기존 바인딩을 해제한다는 규칙을 지키기 위함이다.
        /// </summary>
        public static IReadOnlyList<string> FindDuplicateBindingActions(UserSettings settings, SettingsCategory category)
        {
            var duplicates = new List<string>();
            if (settings == null)
            {
                return duplicates;
            }

            var actions = SettingsDefinitions.BindingActions(category);
            for (var i = 0; i < actions.Count; i++)
            {
                var path = settings.Binding(category, actions[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                for (var j = 0; j < i; j++)
                {
                    if (string.Equals(settings.Binding(category, actions[j]), path, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicates.Add(actions[i]);
                        break;
                    }
                }
            }

            return duplicates;
        }
    }
}
