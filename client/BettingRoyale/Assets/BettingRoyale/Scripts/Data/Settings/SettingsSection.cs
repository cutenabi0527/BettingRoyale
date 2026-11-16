using System;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 분류 하나의 저장 슬롯. 값 배열과 바인딩 배열을 함께 갖는다.
    /// 알 수 없는 ID/액션 항목도 그대로 보존한다(다른 버전 클라이언트 보호).
    /// </summary>
    [Serializable]
    public sealed class SettingsSection
    {
        public SettingValue[] values;
        public SettingsBinding[] bindings;

        public SettingsSection()
        {
            values = new SettingValue[0];
            bindings = new SettingsBinding[0];
        }

        public SettingsSection Clone()
        {
            var clone = new SettingsSection();
            var source = values ?? new SettingValue[0];
            clone.values = new SettingValue[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                clone.values[i] = source[i] != null ? source[i].Clone() : new SettingValue();
            }

            var sourceBindings = bindings ?? new SettingsBinding[0];
            clone.bindings = new SettingsBinding[sourceBindings.Length];
            for (var i = 0; i < sourceBindings.Length; i++)
            {
                clone.bindings[i] = sourceBindings[i] != null ? sourceBindings[i].Clone() : new SettingsBinding();
            }

            return clone;
        }

        /// <summary>JsonUtility 역직렬화 후 null 배열/원소를 안전한 기본값으로 채운다.</summary>
        public void Normalize()
        {
            if (values == null)
            {
                values = new SettingValue[0];
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == null)
                {
                    values[i] = new SettingValue();
                    continue;
                }

                values[i].id = values[i].id ?? string.Empty;
                values[i].value = values[i].value ?? string.Empty;
            }

            if (bindings == null)
            {
                bindings = new SettingsBinding[0];
            }

            for (var i = 0; i < bindings.Length; i++)
            {
                if (bindings[i] == null)
                {
                    bindings[i] = new SettingsBinding();
                    continue;
                }

                bindings[i].actionId = bindings[i].actionId ?? string.Empty;
                bindings[i].path = bindings[i].path ?? string.Empty;
            }
        }

        public SettingValue FindValue(string id)
        {
            if (values == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (var i = 0; i < values.Length; i++)
            {
                var entry = values[i];
                if (entry != null && string.Equals(entry.id, id, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        public SettingsBinding FindBinding(string actionId)
        {
            if (bindings == null || string.IsNullOrEmpty(actionId))
            {
                return null;
            }

            for (var i = 0; i < bindings.Length; i++)
            {
                var entry = bindings[i];
                if (entry != null && string.Equals(entry.actionId, actionId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        public void SetValue(string id, string value)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var existing = FindValue(id);
            if (existing != null)
            {
                existing.value = value ?? string.Empty;
                return;
            }

            if (values == null)
            {
                values = new SettingValue[0];
            }

            if (values.Length >= SettingsSchema.MaxEntriesPerSection)
            {
                return;
            }

            var grown = new SettingValue[values.Length + 1];
            Array.Copy(values, grown, values.Length);
            grown[values.Length] = new SettingValue(id, value);
            values = grown;
        }

        public void RemoveValue(string id)
        {
            var existing = FindValue(id);
            if (existing == null)
            {
                return;
            }

            var remaining = new SettingValue[values.Length - 1];
            var index = 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (ReferenceEquals(values[i], existing))
                {
                    continue;
                }

                remaining[index++] = values[i];
            }

            values = remaining;
        }

        public void SetBinding(string actionId, string path)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                return;
            }

            var existing = FindBinding(actionId);
            if (existing != null)
            {
                existing.path = path ?? string.Empty;
                return;
            }

            if (bindings == null)
            {
                bindings = new SettingsBinding[0];
            }

            if (bindings.Length >= SettingsSchema.MaxEntriesPerSection)
            {
                return;
            }

            var grown = new SettingsBinding[bindings.Length + 1];
            Array.Copy(bindings, grown, bindings.Length);
            grown[bindings.Length] = new SettingsBinding(actionId, path);
            bindings = grown;
        }
    }
}
