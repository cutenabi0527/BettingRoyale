using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 같은 수치가 <b>기획 엑셀</b>과 <b>프리팹</b> 두 곳에 있을 때, 둘이 어긋났는지 가린다 (D-CLI-VMX6AN).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 프리팹 값은 <b>게임 데이터 없이 도는 경로</b>(EditMode 테스트·CI·CombatLab)의 대역이다. 지우면
    /// 그 경로가 0 으로 돈다. 그래서 두 곳에 값이 있는 것 자체는 의도다 — <b>문제는 조용히 달라지는 것</b>이다.
    /// 프리팹만 고치고 엑셀을 안 고치면 배포본에서 값이 돌아가고, 아무도 모른다.
    /// </para>
    /// <para>
    /// ⚠️ 이 검사는 <b>경고만</b> 한다. 진행을 막지 않는다 — 대역이 최신이 아니라고 게임을 못 켜게 하면,
    /// 데이터를 못 받는 로컬 경로가 통째로 막힌다.
    /// </para>
    /// </remarks>
    public static class BalanceValueOwnership
    {
        /// <summary>값이 같다고 볼 오차. 부동소수 반올림만 흡수한다.</summary>
        public const float Epsilon = 0.0005f;

        /// <summary>엑셀 값과 프리팹 대역이 어긋났는가. 둘 중 하나가 없으면(0·비유한) 비교하지 않는다.</summary>
        public static bool IsMismatch(float dataValue, float prefabFallback)
        {
            if (!IsComparable(dataValue) || !IsComparable(prefabFallback))
            {
                return false;
            }

            return Math.Abs(dataValue - prefabFallback) > Epsilon;
        }

        /// <summary>어긋난 항목의 경고 문구. 어긋나지 않았으면 <c>null</c>.</summary>
        public static string DescribeMismatch(string statKey, float dataValue, float prefabFallback) =>
            IsMismatch(dataValue, prefabFallback)
                ? $"{statKey}: 기획 데이터 {dataValue:0.###} vs 프리팹 {prefabFallback:0.###} — "
                  + "게임은 데이터 값으로 돈다. 프리팹 값은 데이터 없이 도는 경로의 대역이므로 같이 맞춰야 한다."
                : null;

        /// <summary>여러 항목을 한 번에 본다. 어긋난 것만 돌려준다.</summary>
        public static IReadOnlyList<string> DescribeMismatches(
            IEnumerable<(string StatKey, float DataValue, float PrefabFallback)> pairs)
        {
            var found = new List<string>();
            if (pairs == null)
            {
                return found;
            }

            foreach ((string statKey, float dataValue, float prefabFallback) in pairs)
            {
                string note = DescribeMismatch(statKey, dataValue, prefabFallback);
                if (note != null)
                {
                    found.Add(note);
                }
            }

            return found;
        }

        private static bool IsComparable(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
