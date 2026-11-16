namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// SettingDefinition.Platform 에 쓰는 플랫폼 식별자.
    /// 빈 문자열은 모든 플랫폼을 뜻한다.
    /// </summary>
    public static class SettingsPlatforms
    {
        public const string Any = "";

        /// <summary>PC 스탠드얼론 빌드 전용(화면 모드/해상도/주사율 등).</summary>
        public const string Standalone = "Standalone";

        public static bool Matches(string definitionPlatform, string runtimePlatform)
        {
            if (string.IsNullOrEmpty(definitionPlatform))
            {
                return true;
            }

            return string.Equals(definitionPlatform, runtimePlatform, System.StringComparison.Ordinal);
        }
    }
}
