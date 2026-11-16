using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Scenes;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// 훈련장에 세워 둘 <b>전투 봇</b>의 규칙 (S-GD-V9ZMN3 · 무기는 S-GD-YAEB2H).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>봇은 개수가 아니라 자리다.</b> 봇 번호가 곧 구역 번호(<c>번호 − 1</c>)이고, 세워진 봇은
    /// 그 구역을 영원히 벗어나지 않는다. 그래서 "몇 기" 로 다루면 "1번부터 N번까지" 라는 접두
    /// 규칙이 되어, 엄폐 구역 봇 하나만 두고 싶을 때도 넷을 전부 켜야 한다.
    /// </para>
    /// <para>
    /// 상수를 Network 레이어에 두지 않는 것은 <b>규칙을 읽는 쪽이 셋</b>이기 때문이다 —
    /// 요청을 검사하는 도메인, 봇을 세우는 Host, 칸을 그리는 화면. 한 곳에서만 알고 있으면
    /// 나머지 둘이 각자 숫자를 적게 되고, 그중 하나만 고치는 날이 온다.
    /// </para>
    /// <para>
    /// ⚠️ <b>연습 더미(5~9번)는 이 규칙의 대상이 아니다.</b> 그쪽은 피해량을 재는 과녁이라
    /// 항상 서 있어야 한다(S-GD-VWW8YK).
    /// </para>
    /// </remarks>
    public static class TrainingBotRule
    {
        /// <summary>전투 봇이 서는 구역 수. 훈련 NPC 번호 1~4 가 구역 0~3 에 하나씩 대응한다.</summary>
        public const int ZoneCount = 4;

        /// <summary>전투 봇이 설 수 있는 가장 큰 훈련 NPC 번호.</summary>
        public const int MaxBotNumber = ZoneCount;

        /// <summary>네 구역이 모두 켜진 조합. <b>아무도 조작하지 않은 방의 값</b>이다.</summary>
        /// <remarks>
        /// 이 기능이 생기기 전의 훈련장이 1~4번을 언제나 세우고 있었으므로, 기본값을 "전부 켜짐"
        /// 으로 두어 조작하지 않은 방의 경험이 바뀌지 않게 한다.
        /// </remarks>
        public const int AllZones = (1 << ZoneCount) - 1;

        /// <inheritdoc cref="AllZones"/>
        public const int Default = AllZones;

        /// <summary>한 구역도 켜지 않은 조합. 봇 없이 허수아비만 두고 수치를 잴 때의 값이다.</summary>
        public const int NoZones = 0;

        /// <summary>범위 안의 조합인가. 전송받은 요청을 <b>자르기 전에</b> 거절할 때 쓴다.</summary>
        public static bool IsValid(int zones) => zones >= NoZones && zones <= AllZones;

        /// <summary>요청받은 조합에서 모르는 자리를 떨어낸다. <c>UnityEngine.Mathf</c> 를 쓰지 않는다(D-060).</summary>
        public static int Clamp(int zones) => zones <= NoZones ? NoZones : zones & AllZones;

        /// <summary>훈련 NPC 번호(1~4)가 이 조합에 들어 있는가.</summary>
        /// <remarks>범위 밖 번호는 언제나 <c>false</c> 다 — 연습 더미를 이 판단에 태우지 않는다.</remarks>
        public static bool Contains(int zones, int botNumber) =>
            botNumber >= 1 && botNumber <= MaxBotNumber && (zones & Bit(botNumber)) != 0;

        /// <summary>훈련 NPC 번호(1~4) 하나를 켜거나 끈 조합을 돌려준다. 원본은 바꾸지 않는다.</summary>
        public static int With(int zones, int botNumber, bool enabled)
        {
            if (botNumber < 1 || botNumber > MaxBotNumber) return Clamp(zones);
            int bit = Bit(botNumber);
            return enabled ? Clamp(zones) | bit : Clamp(zones) & ~bit;
        }

        /// <summary>켜져 있는 구역 수. 화면 문구와 로그에만 쓴다 — 조작 단위가 아니다.</summary>
        public static int Count(int zones)
        {
            int count = 0;
            for (int number = 1; number <= MaxBotNumber; number++) if (Contains(zones, number)) count++;
            return count;
        }

        /// <summary>이 봇이 서는 구역. <b>번호 − 1</b> 이며 그 자리를 영원히 벗어나지 않는다.</summary>
        public static TrainingZoneId ZoneOf(int botNumber) => (TrainingZoneId)(botNumber - 1);

        /// <summary>구역 이름. 화면과 Host 안내가 같은 말을 쓰게 한다.</summary>
        public static string NameOf(int botNumber) => TrainingZoneNames.Of(ZoneOf(botNumber));

        /// <summary>좁은 칸용 두 글자 이름.</summary>
        public static string ShortNameOf(int botNumber) => TrainingZoneNames.Short(ZoneOf(botNumber));

        /// <summary>
        /// 활을 쓰는 봇의 번호. <b>사격장 하나뿐이다</b> (S-GD-YAEB2H).
        /// </summary>
        /// <remarks>
        /// ⚠️ 이 값은 <b>승인받은 적이 없다.</b> 코드에 박혀 있던 것을 스펙으로 옮겨 적었을 뿐이며,
        /// 기획이 다르게 정하면 스펙과 함께 바뀐다.
        /// </remarks>
        public const int BowBotNumber = 2;

        /// <summary>이 봇이 드는 무기. 봇이 <b>처음 설 때 한 번</b> 정해진다.</summary>
        public static WeaponType WeaponOf(int botNumber) =>
            botNumber == BowBotNumber ? WeaponType.Bow : WeaponType.Sword;

        /// <summary>활을 쓰는 봇인가.</summary>
        public static bool UsesBow(int botNumber) => botNumber == BowBotNumber;

        /// <summary>
        /// 칸에 마우스를 올렸을 때 띄울 한 줄. <b>어디에 서고, 무엇을 들고, 어디까지 쫓아오는지</b>
        /// 를 말한다 — 번호만으로는 이 셋 중 어느 것도 알 수 없다.
        /// </summary>
        public static string Describe(int botNumber) =>
            botNumber < 1 || botNumber > MaxBotNumber ? string.Empty
                : NameOf(botNumber) + " 구역에 봇 1기 · " + (UsesBow(botNumber) ? "활" : "검")
                    + " · 이 구역 안에서만 싸우고, 쓰러지면 3초 뒤 같은 자리에 다시 선다.";

        private static int Bit(int botNumber) => 1 << (botNumber - 1);
    }
}
