namespace BettingRoyale.Data.Online
{
    /// <summary>
    /// 네트워크 왕복 지연(RTT)의 표시 캐시. <b>Network 가 쓰고 UI 가 읽는다</b> (S-UX-TXGZWZ, D-009).
    /// </summary>
    /// <remarks>
    /// <para>
    /// UI 는 <c>Scripts/Network/</c> 를 참조할 수 없고 Fusion 타입도 볼 수 없다. 그래서 지연 값은
    /// 여기 <b>초가 아닌 밀리초</b>로 한 번 번역돼 올라오고, 화면은 그 숫자만 읽는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>세션이 없을 때 값을 지어내지 않는다.</b> 로컬 플레이·훈련장·샌드박스에는 왕복 지연이라는
    /// 것이 없으므로 <see cref="Available"/> 가 <c>false</c> 이고, 화면은 그 자리를 비워 둔다.
    /// 0ms 로 적으면 "아주 빠름" 으로 읽혀 거짓말이 된다.
    /// </para>
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다(D-060). Play 재진입 초기화는 이 값을 소비하는
    /// 런타임 쪽(<c>PerformanceReadoutView</c>)이 자기 초기화에서 함께 부른다.
    /// </para>
    /// </remarks>
    public static class NetworkLatencySnapshot
    {
        /// <summary>지금 읽을 수 있는 실제 측정값이 있는가. 세션 밖에서는 <c>false</c>.</summary>
        public static bool Available { get; private set; }

        /// <summary>가장 최근 왕복 지연(ms). <see cref="Available"/> 가 <c>false</c> 면 의미 없다.</summary>
        public static float Milliseconds { get; private set; }

        /// <summary>측정값을 올린다. 유한하지 않거나 음수인 값은 측정 실패로 보고 버린다.</summary>
        public static void Set(float milliseconds)
        {
            if (float.IsNaN(milliseconds) || float.IsInfinity(milliseconds) || milliseconds < 0f)
            {
                Clear();
                return;
            }

            Available = true;
            Milliseconds = milliseconds;
        }

        /// <summary>세션이 끝났거나 측정할 수 없다. 마지막 값을 남겨 두지 않는다.</summary>
        public static void Clear()
        {
            Available = false;
            Milliseconds = 0f;
        }

        /// <summary>Play 재진입 초기화. 도메인 리로드를 끈 상태에서 이전 세션 값이 남지 않게 한다.</summary>
        public static void Reset() => Clear();
    }
}
