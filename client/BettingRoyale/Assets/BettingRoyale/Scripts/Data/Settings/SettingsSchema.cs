using System;
using System.Globalization;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 설정 문서의 스키마 상수와 문화권 독립 문자열 변환 규칙.
    /// 저장/전송되는 모든 값은 이 규칙으로만 직렬화한다(InvariantCulture 고정).
    /// </summary>
    public static class SettingsSchema
    {
        /// <summary>현재 클라이언트가 이해하는 스키마 버전(S-CLI-JZKPDF: 1).</summary>
        public const int Version = 1;

        /// <summary>로컬/클라우드 문서 1건의 최대 크기(S-CLI-JZKPDF: 64KiB).</summary>
        public const int MaxJsonBytes = 64 * 1024;

        /// <summary>섹션 하나가 보존하는 최대 값/바인딩 항목 수(알 수 없는 항목 포함).</summary>
        public const int MaxEntriesPerSection = 512;

        /// <summary>식별자 최대 길이. 초과 항목은 저장 시 폐기한다.</summary>
        public const int MaxIdLength = 128;

        /// <summary>값 문자열 최대 길이. 초과 항목은 저장 시 폐기한다.</summary>
        public const int MaxValueLength = 256;

        /// <summary>타임스탬프 형식: UTC ISO-8601 라운드트립.</summary>
        public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

        private const string FloatFormat = "0.#####";

        public static string CategoryKey(SettingsCategory category)
        {
            switch (category)
            {
                case SettingsCategory.Gameplay: return "gameplay";
                case SettingsCategory.Hud: return "hud";
                case SettingsCategory.KeyboardMouse: return "keyboardMouse";
                case SettingsCategory.Controller: return "controller";
                case SettingsCategory.Audio: return "audio";
                case SettingsCategory.Graphics: return "graphics";
                case SettingsCategory.Accessibility: return "accessibility";
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        public static bool TryParseCategoryKey(string key, out SettingsCategory category)
        {
            category = SettingsCategory.Gameplay;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            switch (key)
            {
                case "gameplay": category = SettingsCategory.Gameplay; return true;
                case "hud": category = SettingsCategory.Hud; return true;
                case "keyboardMouse": category = SettingsCategory.KeyboardMouse; return true;
                case "controller": category = SettingsCategory.Controller; return true;
                case "audio": category = SettingsCategory.Audio; return true;
                case "graphics": category = SettingsCategory.Graphics; return true;
                case "accessibility": category = SettingsCategory.Accessibility; return true;
                default: return false;
            }
        }

        /// <summary>모든 분류. 순서는 좌측 탐색 순서와 저장 순회 순서를 겸한다.</summary>
        public static SettingsCategory[] Categories()
        {
            return new[]
            {
                SettingsCategory.Gameplay,
                SettingsCategory.Hud,
                SettingsCategory.KeyboardMouse,
                SettingsCategory.Controller,
                SettingsCategory.Audio,
                SettingsCategory.Graphics,
                SettingsCategory.Accessibility
            };
        }

        public static string FormatFloat(float value)
        {
            return value.ToString(FloatFormat, CultureInfo.InvariantCulture);
        }

        public static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed);
        }

        public static string FormatInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseInt(string value, out int parsed)
        {
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed);
        }

        public static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        public static bool TryParseBool(string value, out bool parsed)
        {
            parsed = false;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", StringComparison.Ordinal) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            {
                parsed = true;
                return true;
            }

            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "0", StringComparison.Ordinal) ||
                string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            {
                parsed = false;
                return true;
            }

            return false;
        }

        public static string FormatTimestamp(DateTime utc)
        {
            var normalized = utc.Kind == DateTimeKind.Utc
                ? utc
                : (utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc));
            return normalized.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        public static bool TryParseTimestamp(string value, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (!DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces,
                    out var parsed))
            {
                return false;
            }

            utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        /// <summary>파싱 불가/미기록 타임스탬프는 가장 오래된 시각으로 취급한다.</summary>
        public static DateTime TimestampOrMin(string value)
        {
            return TryParseTimestamp(value, out var utc) ? utc : DateTime.MinValue;
        }
    }
}
