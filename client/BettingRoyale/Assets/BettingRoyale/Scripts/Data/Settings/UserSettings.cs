using System;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 계정 설정 문서(로컬 캐시와 클라우드 전송이 공유하는 와이어 엔벌로프).
    /// JsonUtility 로 직렬화되므로 public 필드/배열만 사용하고 Dictionary 를 쓰지 않는다.
    /// </summary>
    [Serializable]
    public sealed class UserSettings
    {
        /// <summary>직렬화 스키마 버전. 저장 리비전은 metadata.settingsVersion에 둔다.</summary>
        public int version;

        public SettingsSection gameplay;
        public SettingsSection hud;
        public SettingsSection keyboardMouse;
        public SettingsSection controller;
        public SettingsSection audio;
        public SettingsSection graphics;
        public SettingsSection accessibility;
        public SettingsMetadata metadata;

        public UserSettings()
        {
            version = SettingsSchema.Version;
            gameplay = new SettingsSection();
            hud = new SettingsSection();
            keyboardMouse = new SettingsSection();
            controller = new SettingsSection();
            audio = new SettingsSection();
            graphics = new SettingsSection();
            accessibility = new SettingsSection();
            metadata = new SettingsMetadata();
        }

        /// <summary>정의된 모든 기본값과 기본 바인딩을 채운 새 문서.</summary>
        public static UserSettings CreateDefault()
        {
            var settings = new UserSettings();
            foreach (var category in SettingsSchema.Categories())
            {
                settings.ResetCategory(category);
            }

            return settings;
        }

        public SettingsSection Section(SettingsCategory category)
        {
            switch (category)
            {
                case SettingsCategory.Gameplay: return gameplay ?? (gameplay = new SettingsSection());
                case SettingsCategory.Hud: return hud ?? (hud = new SettingsSection());
                case SettingsCategory.KeyboardMouse: return keyboardMouse ?? (keyboardMouse = new SettingsSection());
                case SettingsCategory.Controller: return controller ?? (controller = new SettingsSection());
                case SettingsCategory.Audio: return audio ?? (audio = new SettingsSection());
                case SettingsCategory.Graphics: return graphics ?? (graphics = new SettingsSection());
                case SettingsCategory.Accessibility: return accessibility ?? (accessibility = new SettingsSection());
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        public UserSettings Clone()
        {
            var clone = new UserSettings
            {
                version = version,
                gameplay = Section(SettingsCategory.Gameplay).Clone(),
                hud = Section(SettingsCategory.Hud).Clone(),
                keyboardMouse = Section(SettingsCategory.KeyboardMouse).Clone(),
                controller = Section(SettingsCategory.Controller).Clone(),
                audio = Section(SettingsCategory.Audio).Clone(),
                graphics = Section(SettingsCategory.Graphics).Clone(),
                accessibility = Section(SettingsCategory.Accessibility).Clone(),
                metadata = (metadata ?? new SettingsMetadata()).Clone()
            };
            return clone;
        }

        /// <summary>JsonUtility 역직렬화 직후의 null 안전화.</summary>
        public void Normalize()
        {
            foreach (var category in SettingsSchema.Categories())
            {
                Section(category).Normalize();
            }

            if (metadata == null)
            {
                metadata = new SettingsMetadata();
            }

            metadata.Normalize();
            if (version < 0)
            {
                version = SettingsSchema.Version;
            }
        }

        /// <summary>
        /// 값 조회. 정의된 ID 는 저장값이 없으면 기본값을 돌려준다.
        /// 정의가 없는 ID 도 저장되어 있으면 그대로 읽어 보존성을 유지한다.
        /// </summary>
        public string Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            var canonical = SettingsDefinitions.CanonicalId(id);
            var definition = SettingsDefinitions.Find(canonical);
            if (definition != null)
            {
                if (definition.Kind == SettingKind.Binding)
                {
                    return Binding(definition.Category, SettingsDefinitions.ActionIdOf(canonical));
                }

                var stored = Section(definition.Category).FindValue(canonical);
                return stored != null ? stored.value : definition.DefaultValue;
            }

            foreach (var category in SettingsSchema.Categories())
            {
                var unknown = Section(category).FindValue(canonical);
                if (unknown != null)
                {
                    return unknown.value;
                }
            }

            return null;
        }

        public float Float(string id)
        {
            var raw = Get(id);
            if (SettingsSchema.TryParseFloat(raw, out var parsed))
            {
                return parsed;
            }

            var definition = SettingsDefinitions.Find(SettingsDefinitions.CanonicalId(id));
            if (definition != null && SettingsSchema.TryParseFloat(definition.DefaultValue, out var fallback))
            {
                return fallback;
            }

            return 0f;
        }

        public bool Bool(string id)
        {
            var raw = Get(id);
            if (SettingsSchema.TryParseBool(raw, out var parsed))
            {
                return parsed;
            }

            var definition = SettingsDefinitions.Find(SettingsDefinitions.CanonicalId(id));
            if (definition != null && SettingsSchema.TryParseBool(definition.DefaultValue, out var fallback))
            {
                return fallback;
            }

            return false;
        }

        /// <summary>
        /// 값 기록. 별칭 ID 는 원본 ID 슬롯 한 곳에만 저장한다.
        /// 정의가 없는 ID 는 이미 저장된 항목만 갱신하고 새로 만들지 않는다.
        /// </summary>
        public void Set(string id, string value)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var canonical = SettingsDefinitions.CanonicalId(id);
            var definition = SettingsDefinitions.Find(canonical);
            if (definition == null)
            {
                foreach (var category in SettingsSchema.Categories())
                {
                    var section = Section(category);
                    if (section.FindValue(canonical) != null)
                    {
                        section.SetValue(canonical, value);
                        return;
                    }
                }

                return;
            }

            if (definition.Kind == SettingKind.Binding)
            {
                SetBinding(definition.Category, SettingsDefinitions.ActionIdOf(canonical), value);
                return;
            }

            Section(definition.Category).SetValue(canonical, value ?? string.Empty);
        }

        public string Binding(SettingsCategory category, string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                return string.Empty;
            }

            var stored = Section(category).FindBinding(actionId);
            if (stored != null)
            {
                return stored.path ?? string.Empty;
            }

            return SettingsDefinitions.DefaultBinding(category, actionId);
        }

        /// <summary>
        /// 바인딩 지정. 같은 분류에서 같은 경로를 쓰던 다른 액션은 해제된다
        /// (S-UX-8FGK29: 충돌 시 기존 바인딩 제거). 빈 경로는 해제를 뜻한다.
        /// </summary>
        public void SetBinding(SettingsCategory category, string actionId, string path)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                return;
            }

            var normalizedPath = path ?? string.Empty;
            var section = Section(category);
            if (normalizedPath.Length > 0)
            {
                var conflict = FindBindingConflict(category, actionId, normalizedPath);
                if (!string.IsNullOrEmpty(conflict))
                {
                    section.SetBinding(conflict, string.Empty);
                }
            }

            section.SetBinding(actionId, normalizedPath);
        }

        /// <summary>
        /// 같은 분류(같은 장치) 안에서 path 를 이미 사용하는 다른 액션 ID. 없으면 빈 문자열.
        /// 게임플레이/UI 액션을 구분하지 않고 전체 재지정 가능 액션 집합에서 금지한다.
        /// </summary>
        public string FindBindingConflict(SettingsCategory category, string actionId, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var actions = SettingsDefinitions.BindingActions(category);
            for (var i = 0; i < actions.Count; i++)
            {
                var candidate = actions[i];
                if (string.Equals(candidate, actionId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(Binding(category, candidate), path, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            var section = Section(category);
            var stored = section.bindings ?? new SettingsBinding[0];
            for (var i = 0; i < stored.Length; i++)
            {
                var entry = stored[i];
                if (entry == null || string.IsNullOrEmpty(entry.actionId))
                {
                    continue;
                }

                if (string.Equals(entry.actionId, actionId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(entry.path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.actionId;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 현재 탭만 기본값으로. 정의되지 않은(다른 버전에서 온) 항목은 건드리지 않는다.
        /// 접근성 탭은 별칭으로 노출하는 원본 ID(hud.scale)도 함께 되돌린다.
        /// </summary>
        public void ResetCategory(SettingsCategory category)
        {
            var definitions = SettingsDefinitions.ForCategory(category);
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition.Kind == SettingKind.Binding)
                {
                    Section(category).SetBinding(
                        SettingsDefinitions.ActionIdOf(definition.Id),
                        definition.DefaultValue);
                    continue;
                }

                var canonical = SettingsDefinitions.CanonicalId(definition.Id);
                var target = SettingsDefinitions.Find(canonical);
                var owner = target != null ? target.Category : category;
                Section(owner).SetValue(canonical, definition.DefaultValue);
            }
        }

        public void ResetAll()
        {
            foreach (var category in SettingsSchema.Categories())
            {
                ResetCategory(category);
            }
        }

        /// <summary>메타데이터/리비전을 제외한 설정 내용이 같은지 비교한다.</summary>
        public static bool ContentEquals(UserSettings left, UserSettings right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            foreach (var category in SettingsSchema.Categories())
            {
                if (!SectionContentEquals(category, left.Section(category), right.Section(category)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SectionContentEquals(SettingsCategory category, SettingsSection left, SettingsSection right)
        {
            if (!ValueSetEquals(left, right) || !ValueSetEquals(right, left))
            {
                return false;
            }

            return BindingSetEquals(category, left, right) && BindingSetEquals(category, right, left);
        }

        private static bool ValueSetEquals(SettingsSection source, SettingsSection other)
        {
            var values = source.values ?? new SettingValue[0];
            for (var i = 0; i < values.Length; i++)
            {
                var entry = values[i];
                if (entry == null)
                {
                    continue;
                }

                var counterpart = other.FindValue(entry.id);
                var counterpartValue = counterpart != null ? counterpart.value : null;
                if (counterpartValue == null)
                {
                    var definition = SettingsDefinitions.Find(entry.id);
                    counterpartValue = definition != null ? definition.DefaultValue : null;
                }

                if (!string.Equals(entry.value ?? string.Empty, counterpartValue ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool BindingSetEquals(SettingsCategory category, SettingsSection source, SettingsSection other)
        {
            var bindings = source.bindings ?? new SettingsBinding[0];
            for (var i = 0; i < bindings.Length; i++)
            {
                var entry = bindings[i];
                if (entry == null)
                {
                    continue;
                }

                var counterpart = other.FindBinding(entry.actionId);
                var counterpartPath = counterpart != null
                    ? counterpart.path
                    : SettingsDefinitions.DefaultBinding(category, entry.actionId);
                if (!string.Equals(entry.path ?? string.Empty, counterpartPath ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
