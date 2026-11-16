namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// 클라이언트가 붙어 있는 배포 환경 (D-REL-VT3MBF 의 2스택).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>판정의 출처는 <c>RemoteConfig.BaseUrl</c> 하나다.</b> REST·데이터·Photon 이 각자 환경을
    /// 판정하면 반드시 한쪽만 갱신된다 — 주소는 Preview 인데 방은 Production 같은 상태가 생긴다.
    /// </para>
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </para>
    /// </remarks>
    public enum DeploymentEnvironment
    {
        /// <summary>맨 경로(<c>/api</c>·<c>/manifest.json</c>). <c>main</c> 브랜치가 배포한다.</summary>
        Production = 0,

        /// <summary><c>/preview</c> 프리픽스. <c>dev</c> 브랜치가 배포한다.</summary>
        Preview = 1,

        /// <summary>개발자가 직접 띄운 서버(<c>localhost</c> 등). 배포 환경이 아니다.</summary>
        Local = 2,
    }
}
