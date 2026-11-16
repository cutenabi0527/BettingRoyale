using System;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 저장 문서의 입력 바인딩 항목. path 는 Unity InputSystem 컨트롤 경로 문자열이다.
    /// 빈 path 는 "바인딩 없음"을 뜻하며 충돌 판정에서 제외된다.
    /// </summary>
    [Serializable]
    public sealed class SettingsBinding
    {
        public string actionId;
        public string path;

        public SettingsBinding()
        {
            actionId = string.Empty;
            path = string.Empty;
        }

        public SettingsBinding(string actionId, string path)
        {
            this.actionId = actionId ?? string.Empty;
            this.path = path ?? string.Empty;
        }

        public SettingsBinding Clone()
        {
            return new SettingsBinding(actionId, path);
        }
    }
}
