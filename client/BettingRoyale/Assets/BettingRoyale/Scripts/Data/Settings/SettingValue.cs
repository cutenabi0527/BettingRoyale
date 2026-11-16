using System;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 저장 문서의 값 항목. Dictionary 직렬화를 쓰지 않기 위한 배열 원소다.
    /// value 는 항상 InvariantCulture 문자열이다.
    /// </summary>
    [Serializable]
    public sealed class SettingValue
    {
        public string id;
        public string value;

        public SettingValue()
        {
            id = string.Empty;
            value = string.Empty;
        }

        public SettingValue(string id, string value)
        {
            this.id = id ?? string.Empty;
            this.value = value ?? string.Empty;
        }

        public SettingValue Clone()
        {
            return new SettingValue(id, value);
        }
    }
}
