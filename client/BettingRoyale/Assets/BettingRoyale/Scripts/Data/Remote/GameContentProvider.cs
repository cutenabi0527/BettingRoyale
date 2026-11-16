namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// 기동 때 복원한 <see cref="GameContent"/> 를 게임 코드에 공급한다 (S15P21A606-201).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RemoteConfig</c> 와 같은 모양이다 — 기동 순서의 한 지점에서 한 번 채워지고 그 뒤로는 읽기만 한다.
    /// 매니저 싱글톤으로 만들지 않은 이유는 <b>이 값이 <see cref="UnityEngine"/> 을 몰라야</b>
    /// <c>Scripts/Data/</c> 규칙(D-060)을 지키고 도메인 테스트에서 그대로 쓸 수 있기 때문이다.
    /// </para>
    /// <para>
    /// <b><see cref="IsLoaded"/> 가 <c>false</c> 인 경로가 정상적으로 존재한다</b> —
    /// 배치모드(CI·EditMode 테스트)에서는 <c>Bootstrap</c> 이 원격 콘텐츠 준비를 통째로 건너뛴다
    /// (D-CLI-CAJZW6). 소비하는 쪽은 <b>반드시</b> 안 불러온 경우를 처리해야 한다.
    /// </para>
    /// </remarks>
    public static class GameContentProvider
    {
        private static GameContent current;

        /// <summary>불러왔으면 <c>true</c>. 배치모드에서는 <c>false</c> 다.</summary>
        public static bool IsLoaded => current != null;

        /// <summary>복원된 정본 데이터. 안 불러왔으면 <c>null</c> 이다 — 호출 전에 <see cref="IsLoaded"/> 를 본다.</summary>
        public static GameContent Current => current;

        /// <summary>기동 경로에서 한 번 호출한다.</summary>
        public static void Set(GameContent content) => current = content;

        /// <summary>
        /// 테스트가 서로 영향을 주지 않게 되돌린다. <b>게임 코드에서 부르지 않는다.</b>
        /// </summary>
        public static void Reset() => current = null;
    }
}
