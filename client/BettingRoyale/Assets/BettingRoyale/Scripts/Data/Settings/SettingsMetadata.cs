using System;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 문서 메타데이터. 저장 리비전과 마지막 적용 시각/기기를 담아 충돌을 결정적으로 해결한다.
    /// </summary>
    [Serializable]
    public sealed class SettingsMetadata
    {
        /// <summary>저장 리비전. 적용할 때 증가하며 문서 스키마는 UserSettings.version에 둔다.</summary>
        public int settingsVersion;

        /// <summary>마지막 적용 UTC ISO-8601 문자열. 미기록이면 빈 문자열.</summary>
        public string updatedAt;

        /// <summary>마지막 적용 기기 ID. 동일 시각 충돌의 결정적 타이브레이커.</summary>
        public string deviceId;

        public SettingsMetadata()
        {
            settingsVersion = 0;
            updatedAt = string.Empty;
            deviceId = string.Empty;
        }

        public SettingsMetadata Clone()
        {
            return new SettingsMetadata
            {
                settingsVersion = settingsVersion,
                updatedAt = updatedAt,
                deviceId = deviceId
            };
        }

        public void Normalize()
        {
            updatedAt = updatedAt ?? string.Empty;
            deviceId = deviceId ?? string.Empty;
            if (settingsVersion < 0)
            {
                settingsVersion = 0;
            }
        }
    }
}
