namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// 매니페스트를 쓸 수 없게 만든 이유. <b>정책이 아니라 사실만 담는다</b> —
    /// "오류 화면으로 갈지 로컬 캐시로 진행할지" 는 부트 시퀀스가 결정한다 (계약 4-1절).
    /// </summary>
    public enum RemoteManifestError
    {
        None = 0,

        /// <summary>본문이 비었거나 JSON 이 아니어서 역직렬화 결과가 없다.</summary>
        EmptyPayload,

        /// <summary>
        /// 우리가 모르는 <c>schemaVersion</c>. 계약 3절이 "클라는 모르는 값이면 오류 처리" 로 규정한다.
        /// 오래된 클라이언트가 새 계약을 만난 상황일 수 있다.
        /// </summary>
        UnsupportedSchemaVersion,

        /// <summary>
        /// data 블록이 없거나 <c>version</c> 이 비었다. Data 가 아직 배포되지 않은 상태일 수 있다.
        /// </summary>
        MissingDataBlock,

        /// <summary>
        /// <c>data.version</c> 이 경로 세그먼트로 쓸 수 없는 문자를 담고 있다.
        /// 버전은 불투명 문자열이지만 로컬 캐시 디렉터리 이름이 되므로(계약 4-2절) 경로 탈출을 막는다.
        /// </summary>
        InvalidDataVersion,

        /// <summary><c>data.file</c> 이 비었거나 상대 경로가 아니다.</summary>
        InvalidDataFile,

        /// <summary><c>data.sha256</c> 이 소문자 hex 64자가 아니다 (계약 3절).</summary>
        InvalidDataSha256,

        /// <summary><c>data.sizeBytes</c> 가 0 이하다.</summary>
        InvalidDataSize
    }
}
