using System.IO;

namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// Data 캐시의 경로 규약. 정본은 docs/infra/version-contract.md 4-2절.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 경로 조립만 한다 — 디렉터리를 만들거나 지우지 않는다. 파일 조작은 Managers 레이어가 한다.
    /// 루트를 인자로 받으므로(<c>Application.persistentDataPath</c> 를 직접 읽지 않는다)
    /// Unity 없이 테스트할 수 있다 (D-060).
    /// </para>
    /// <para>
    /// <b>버전 마커 파일을 두지 않는다.</b> 계약 4-2절이 캐시 위치를
    /// <c>data/&lt;dataVersion&gt;/gamedata.bin</c> 으로 정하므로 <b>디렉터리 이름이 곧 버전</b>이다.
    /// 별도 마커를 두면 마커와 실제 파일이 어긋나는 상태가 생긴다 — 상태를 하나로 유지한다.
    /// </para>
    /// </remarks>
    public static class RemoteDataCacheLayout
    {
        public const string DataRootFolderName = "data";

        public const string DataFileName = "gamedata.bin";

        /// <summary>
        /// 다운로드 중인 파일의 이름. <b>버전 디렉터리 밖</b>에 둔다 —
        /// 계약 4-2절의 "검증을 통과하지 못한 파일은 임시 경로 밖으로 나오지 않는다" 를 경로로 보장한다.
        /// </summary>
        public const string DownloadTempFileName = "gamedata.download";

        /// <summary>
        /// 배포 환경별 캐시 루트 (S15P21A606-231). 나머지 경로 조립은 이 값을
        /// <c>persistentRoot</c> 자리에 넣어 그대로 쓴다.
        /// </summary>
        /// <remarks>
        /// <b>환경을 가르지 않으면 두 환경이 같은 폴더를 공유한다.</b> 한 PC 에서 Preview 와
        /// Production 을 번갈아 실행하면 <c>CleanUpOtherVersions</c> 가 서로의 캐시를 지우고,
        /// 두 환경이 우연히 같은 버전 문자열을 내면 다른 내용을 그대로 쓴다 — 캐시가 있으면
        /// 해시를 다시 검증하지 않기 때문이다.
        /// <para>
        /// 키가 비면 예전 경로(<c>persistentRoot/data/...</c>)를 그대로 돌려준다. 테스트가
        /// 환경을 신경 쓰지 않아도 되게 하기 위한 것이며, 게임 코드는 항상 키를 넘긴다.
        /// </para>
        /// </remarks>
        public static string GetEnvironmentRoot(string persistentRoot, string environmentKey)
            => string.IsNullOrWhiteSpace(environmentKey)
                ? persistentRoot
                : Path.Combine(persistentRoot, environmentKey);

        /// <summary>모든 버전 디렉터리의 부모. 임시 파일도 여기 놓인다.</summary>
        public static string GetDataRoot(string persistentRoot)
            => Path.Combine(persistentRoot, DataRootFolderName);

        public static string GetVersionDirectory(string persistentRoot, string dataVersion)
            => Path.Combine(GetDataRoot(persistentRoot), dataVersion);

        public static string GetDataFilePath(string persistentRoot, string dataVersion)
            => Path.Combine(GetVersionDirectory(persistentRoot, dataVersion), DataFileName);

        public static string GetDownloadTempPath(string persistentRoot)
            => Path.Combine(GetDataRoot(persistentRoot), DownloadTempFileName);
    }
}
