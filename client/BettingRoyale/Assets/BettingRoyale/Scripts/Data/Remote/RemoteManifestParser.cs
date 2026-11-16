using BettingRoyale.Data.Dto;

namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// 유선 DTO 를 검증해 <see cref="RemoteManifest"/> 로 옮긴다. 계약 3절의 필드 규칙이 여기 한 곳에만 있다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>JSON 문자열을 받지 않는다.</b> 역직렬화는 <c>JsonUtility</c>(UnityEngine)가 하고,
    /// 이 클래스는 그 결과만 검사한다 — 그래서 Unity 없이 테스트할 수 있다 (D-060).
    /// </para>
    /// <para>
    /// <b>정책을 담지 않는다.</b> 실패 이유만 돌려주고, 오류 화면·오프라인 진행 판단은 호출자 몫이다.
    /// </para>
    /// </remarks>
    public static class RemoteManifestParser
    {
        /// <summary>우리가 해석할 수 있는 매니페스트 스키마 (계약 3절).</summary>
        public const int SupportedSchemaVersion = 1;

        private const int Sha256HexLength = 64;

        public static RemoteManifestParseResult Validate(RemoteManifestDto dto)
        {
            if (dto == null)
            {
                return RemoteManifestParseResult.Failure(
                    RemoteManifestError.EmptyPayload, "역직렬화 결과가 없습니다.");
            }

            if (dto.schemaVersion != SupportedSchemaVersion)
            {
                return RemoteManifestParseResult.Failure(
                    RemoteManifestError.UnsupportedSchemaVersion,
                    $"모르는 schemaVersion {dto.schemaVersion} (지원 {SupportedSchemaVersion}).");
            }

            // JsonUtility 는 없는 중첩 객체를 빈 인스턴스로 채우므로 null 검사만으로는 부족하다.
            // 필수 문자열이 비었는지로 "블록이 실제로 있는가" 를 판정한다.
            var data = dto.data;
            if (data == null || string.IsNullOrWhiteSpace(data.version))
            {
                return RemoteManifestParseResult.Failure(
                    RemoteManifestError.MissingDataBlock,
                    "data 블록이 없거나 version 이 비었습니다 (Data 미배포일 수 있습니다).");
            }

            // 버전은 불투명 문자열이지만 로컬 캐시 디렉터리 이름이 된다(계약 4-2절).
            // 매니페스트는 신뢰 경계 밖에서 오므로 경로로 해석될 수 있는 값을 통과시키지 않는다.
            var dataVersion = data.version.Trim();
            if (!IsPathSafeSegment(dataVersion))
            {
                return RemoteManifestParseResult.Failure(
                    RemoteManifestError.InvalidDataVersion,
                    $"data.version 을 캐시 디렉터리 이름으로 쓸 수 없습니다: '{data.version}'.");
            }

            if (!TryNormalizeRelativePath(data.file, out var relativePath))
            {
                return RemoteManifestParseResult.Failure(
                    RemoteManifestError.InvalidDataFile,
                    $"data.file 이 Base URL 기준 상대 경로가 아닙니다: '{data.file}'.");
            }

            if (!IsLowerHexSha256(data.sha256))
            {
                return RemoteManifestParseResult.Failure(
                    RemoteManifestError.InvalidDataSha256,
                    "data.sha256 이 소문자 hex 64자가 아닙니다.");
            }

            if (data.sizeBytes <= 0)
            {
                return RemoteManifestParseResult.Failure(
                    RemoteManifestError.InvalidDataSize,
                    $"data.sizeBytes 가 유효하지 않습니다: {data.sizeBytes}.");
            }

            // asset 블록은 없어도 정상이다 — 표시용 라벨이라 클라 로직이 이 값으로 분기하지 않는다.
            var assetLabel = dto.asset == null || string.IsNullOrWhiteSpace(dto.asset.version)
                ? string.Empty
                : dto.asset.version.Trim();

            var manifest = new RemoteManifest(
                dto.schemaVersion,
                dataVersion,
                relativePath,
                data.sha256,
                data.sizeBytes,
                assetLabel);

            return RemoteManifestParseResult.Success(manifest);
        }

        /// <summary>
        /// 앞의 <c>/</c> 를 떼고 상대 경로로 정규화한다.
        /// 절대 URL·상위 경로 탈출·백슬래시는 거부한다 — 이 값이 URL 과 로컬 경로 양쪽 조립에 쓰인다.
        /// </summary>
        private static bool TryNormalizeRelativePath(string value, out string normalized)
        {
            normalized = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();

            if (trimmed.Contains("://") || trimmed.Contains("\\"))
            {
                return false;
            }

            trimmed = trimmed.TrimStart('/');

            if (trimmed.Length == 0)
            {
                return false;
            }

            foreach (var segment in trimmed.Split('/'))
            {
                if (segment == ".." || segment.Length == 0)
                {
                    return false;
                }
            }

            normalized = trimmed;
            return true;
        }

        /// <summary>
        /// 파일 시스템 경로 한 칸으로 안전한 값인가. 영숫자와 <c>-</c> <c>_</c> <c>.</c> 만 허용하고
        /// <c>.</c> 로만 이루어진 값(<c>.</c>, <c>..</c>)은 거부한다.
        /// </summary>
        /// <remarks>
        /// 계약은 버전 형식을 강제하지 않으므로(1절 — 불투명 문자열) 형식이 아니라
        /// <b>경로 안전성만</b> 검사한다. 현행 스탬프 <c>yyyyMMdd-HHmmss</c> 는 이 조건을 통과한다.
        /// </remarks>
        private static bool IsPathSafeSegment(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var hasNonDot = false;
            foreach (var c in value)
            {
                var isDigit = c >= '0' && c <= '9';
                var isLower = c >= 'a' && c <= 'z';
                var isUpper = c >= 'A' && c <= 'Z';
                var isAllowedPunctuation = c == '-' || c == '_' || c == '.';

                if (!isDigit && !isLower && !isUpper && !isAllowedPunctuation)
                {
                    return false;
                }

                if (c != '.')
                {
                    hasNonDot = true;
                }
            }

            return hasNonDot;
        }

        private static bool IsLowerHexSha256(string value)
        {
            if (value == null || value.Length != Sha256HexLength)
            {
                return false;
            }

            foreach (var c in value)
            {
                var isDigit = c >= '0' && c <= '9';
                var isLowerHexLetter = c >= 'a' && c <= 'f';
                if (!isDigit && !isLowerHexLetter)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
