namespace BettingRoyale.Data.LocalPlay
{
    /// <summary>
    /// Editor 전용 Local Play MVP 실행이 요청되었는지 런타임 초기화 코드에 알린다.
    /// 네트워크 전투 검증 패널이 같은 CombatSandbox를 Bootstrap으로 가로채지 않게 하는
    /// 개발 도구 간의 최소 경계이며, 플레이어 빌드에서는 항상 비활성이다.
    /// </summary>
    public static class LocalPlayDevelopmentLaunch
    {
        private const string SessionKey = "BettingRoyale.LocalPlayMvpLauncher.Pending";

        public static bool IsRequested
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetBool(SessionKey, false);
#else
                return false;
#endif
            }
        }

        public static void Request()
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool(SessionKey, true);
#endif
        }

        public static void Clear()
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool(SessionKey, false);
#endif
        }
    }
}
