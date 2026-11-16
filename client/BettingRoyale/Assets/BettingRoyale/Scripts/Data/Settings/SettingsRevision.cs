using System;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 문서 리비전 비교 규칙(S-CLI-JZKPDF: UpdatedAt 과 DeviceId 의 결정적 순서).
    /// 순수 함수만 두어 동기화 정책을 테스트 가능한 형태로 유지한다.
    /// </summary>
    public static class SettingsRevision
    {
        /// <summary>수신 시각보다 이만큼 이상 미래인 문서는 신뢰하지 않는다.</summary>
        public static readonly TimeSpan FutureSkewLimit = TimeSpan.FromMinutes(5);

        public static DateTime UpdatedAt(UserSettings settings)
        {
            if (settings == null || settings.metadata == null)
            {
                return DateTime.MinValue;
            }

            return SettingsSchema.TimestampOrMin(settings.metadata.updatedAt);
        }

        public static string DeviceId(UserSettings settings)
        {
            if (settings == null || settings.metadata == null)
            {
                return string.Empty;
            }

            return settings.metadata.deviceId ?? string.Empty;
        }

        public static int SchemaVersion(UserSettings settings)
        {
            if (settings == null)
            {
                return 0;
            }

            return settings.version;
        }

        /// <summary>left 가 더 새로우면 양수, 더 오래되면 음수, 완전히 같은 리비전이면 0.</summary>
        public static int Compare(UserSettings left, UserSettings right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return right == null ? 0 : -1;
            }

            if (right == null)
            {
                return 1;
            }

            var leftTime = UpdatedAt(left);
            var rightTime = UpdatedAt(right);
            if (leftTime != rightTime)
            {
                return leftTime > rightTime ? 1 : -1;
            }

            return string.CompareOrdinal(DeviceId(left), DeviceId(right));
        }

        public static bool IsNewer(UserSettings candidate, UserSettings baseline)
        {
            return Compare(candidate, baseline) > 0;
        }

        /// <summary>같은 적용 시점을 가리키는 문서인지(늦은 응답 판별용).</summary>
        public static bool SameRevision(UserSettings left, UserSettings right)
        {
            if (left == null || right == null || left.metadata == null || right.metadata == null)
            {
                return false;
            }

            return left.metadata.settingsVersion == right.metadata.settingsVersion &&
                   UpdatedAt(left) == UpdatedAt(right) &&
                   string.Equals(DeviceId(left), DeviceId(right), StringComparison.Ordinal);
        }

        /// <summary>수신 시각 기준으로 지나치게 미래인 문서인지.</summary>
        public static bool IsImplausibleFuture(UserSettings settings, DateTime nowUtc)
        {
            var updated = UpdatedAt(settings);
            if (updated == DateTime.MinValue)
            {
                return false;
            }

            return updated - nowUtc > FutureSkewLimit;
        }
    }
}
