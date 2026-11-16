namespace BettingRoyale.Data.Scenes
{
    /// <summary>
    /// 훈련장 구역의 <b>사람이 읽는 이름</b>. 화면과 Host 안내 문구가 같은 이름을 쓰게 한다.
    /// </summary>
    /// <remarks>
    /// 이름을 화면 안에만 두면 Host 가 보내는 안내 문구는 다른 말을 쓰게 되고, 사용자는 같은 곳을
    /// 두 이름으로 배우게 된다. 순수 문자열이라 <c>UnityEngine</c> 에 기대지 않는다(D-060).
    /// </remarks>
    public static class TrainingZoneNames
    {
        private static readonly string[] Long = { "근접 훈련", "사격장", "고저차 훈련", "엄폐 훈련", "입구 광장" };

        /// <summary>좁은 칸용 두 글자 이름. 긴 이름의 <b>앞부분</b>이라 같은 곳으로 읽힌다.</summary>
        private static readonly string[] Narrow = { "근접", "사격", "고저", "엄폐", "입구" };

        public static string Of(TrainingZoneId zone) => InRange(zone) ? Long[(int)zone] : string.Empty;

        public static string Short(TrainingZoneId zone) => InRange(zone) ? Narrow[(int)zone] : string.Empty;

        public static int Count => Long.Length;

        private static bool InRange(TrainingZoneId zone) => (int)zone >= 0 && (int)zone < Long.Length;
    }
}
