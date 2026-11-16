namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// 검증을 통과한 매니페스트. 정본은 docs/infra/version-contract.md 3절 (D-REL-Y4RH6K).
    /// </summary>
    /// <remarks>
    /// 이 타입은 <see cref="RemoteManifestParser"/> 만 만든다 — 생성됐다는 사실이 곧
    /// "계약이 요구하는 필드가 전부 유효하다" 는 뜻이다. 유효하지 않은 매니페스트는
    /// 이 타입이 되지 못하고 <see cref="RemoteManifestError"/> 로 남는다.
    /// </remarks>
    public sealed class RemoteManifest
    {
        internal RemoteManifest(
            int schemaVersion,
            string dataVersion,
            string dataFileRelativePath,
            string dataSha256,
            long dataSizeBytes,
            string assetVersionLabel)
        {
            SchemaVersion = schemaVersion;
            DataVersion = dataVersion;
            DataFileRelativePath = dataFileRelativePath;
            DataSha256 = dataSha256;
            DataSizeBytes = dataSizeBytes;
            AssetVersionLabel = assetVersionLabel;
        }

        public int SchemaVersion { get; }

        /// <summary>Data 전환의 기준값. 불투명 문자열이므로 동등 비교만 한다 (계약 1절 1번).</summary>
        public string DataVersion { get; }

        /// <summary>Base URL 기준 상대 경로. 앞의 <c>/</c> 는 파서가 제거한다.</summary>
        public string DataFileRelativePath { get; }

        /// <summary>소문자 hex 64자 (계약 3절).</summary>
        public string DataSha256 { get; }

        /// <summary>진행률 표시·디스크 사전 확인용. 무결성 판정에도 함께 쓴다.</summary>
        public long DataSizeBytes { get; }

        /// <summary>
        /// 표시·운영용 라벨. 블록이 없으면 빈 문자열이다.
        /// <b>전환 판단에 쓰지 않는다</b> — 에셋 전환은 Addressables 카탈로그가 담당한다 (계약 2·3절).
        /// </summary>
        public string AssetVersionLabel { get; }

        public bool HasAssetVersionLabel => !string.IsNullOrEmpty(AssetVersionLabel);
    }
}
