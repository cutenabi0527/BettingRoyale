namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// Base URL 과 계약상 상대 경로를 잇는다. 정본은 docs/infra/version-contract.md 2절.
    /// </summary>
    /// <remarks>
    /// 우리 다운로드 코드가 주소를 아는 파일은 <see cref="ManifestFileName"/> 하나다 (계약 1절 4번).
    /// 나머지 경로는 매니페스트 또는 Addressables 카탈로그가 알려준다.
    /// </remarks>
    public static class RemoteContentUrl
    {
        /// <summary>진입점 파일 이름. 이 상수 외의 원격 주소를 코드에 적지 않는다.</summary>
        public const string ManifestFileName = "manifest.json";

        public static string GetManifestUrl(string baseUrl) => Combine(baseUrl, ManifestFileName);

        /// <summary>
        /// 후행 슬래시 유무와 무관하게 <c>base/relative</c> 를 만든다.
        /// 상대 경로는 <see cref="RemoteManifestParser"/> 가 이미 정규화한 값을 넣는다.
        /// </summary>
        public static string Combine(string baseUrl, string relativePath)
        {
            var trimmedBase = string.IsNullOrEmpty(baseUrl) ? string.Empty : baseUrl.TrimEnd('/');
            var trimmedRelative = string.IsNullOrEmpty(relativePath)
                ? string.Empty
                : relativePath.TrimStart('/');

            if (trimmedBase.Length == 0)
            {
                return trimmedRelative;
            }

            return trimmedRelative.Length == 0
                ? trimmedBase
                : trimmedBase + "/" + trimmedRelative;
        }
    }
}
