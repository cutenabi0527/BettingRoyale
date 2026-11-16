namespace BettingRoyale.Data.Remote
{
    /// <summary>매니페스트 검증 결과. 성공이면 <see cref="Manifest"/>, 실패면 <see cref="Error"/> 가 채워진다.</summary>
    public readonly struct RemoteManifestParseResult
    {
        private RemoteManifestParseResult(RemoteManifest manifest, RemoteManifestError error, string detail)
        {
            Manifest = manifest;
            Error = error;
            Detail = detail;
        }

        public RemoteManifest Manifest { get; }

        public RemoteManifestError Error { get; }

        /// <summary>로그에 남길 사람용 설명. UI 문구로 쓰지 않는다.</summary>
        public string Detail { get; }

        public bool IsSuccess => Error == RemoteManifestError.None;

        internal static RemoteManifestParseResult Success(RemoteManifest manifest)
            => new RemoteManifestParseResult(manifest, RemoteManifestError.None, string.Empty);

        internal static RemoteManifestParseResult Failure(RemoteManifestError error, string detail)
            => new RemoteManifestParseResult(null, error, detail);
    }
}
