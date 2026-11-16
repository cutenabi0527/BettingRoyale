using System;

namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// Base URL 하나에서 <see cref="DeploymentEnvironment"/> 를 판정한다 (S15P21A606-230).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 클래스가 환경 판정의 유일한 자리다.</b> Photon AppId·데이터 캐시·REST 가 모두 여기를
    /// 통해 같은 답을 받는다. 판정을 각자 두면 <b>주소는 Preview 인데 방은 Production</b> 같은
    /// 어긋남이 생기고, 그때 매치 결과가 서로 다른 DB 로 쪼개진다.
    /// </para>
    /// <para>
    /// <b>모르면 <see cref="DeploymentEnvironment.Production"/> 이 아니라
    /// <see cref="DeploymentEnvironment.Local"/> 로 떨어진다.</b> 알 수 없는 주소를 운영으로 읽으면
    /// 개발자가 띄운 서버를 보면서 운영 방 목록에 들어간다 — 그쪽이 훨씬 위험하다.
    /// 다만 주소가 <b>비어 있으면</b> Production 이다. 그것은 <c>RemoteConfig</c> 가 기본값을
    /// 적용하기 전 상태이고, 기본값이 곧 운영 주소이기 때문이다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class DeploymentEnvironmentResolver
    {
        /// <summary>배포 호스트. 두 환경이 같은 EC2 를 쓰고 경로로만 갈린다 (D-REL-NM9GTF).</summary>
        public const string DeployHost = "bettingroyale.p.example.invalid";

        /// <summary>Preview 를 가리키는 첫 경로 조각.</summary>
        public const string PreviewPathSegment = "preview";

        /// <summary>
        /// Base URL 을 환경으로 옮긴다. 스킴·후행 슬래시·대소문자에 영향받지 않는다.
        /// </summary>
        public static DeploymentEnvironment Resolve(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                // RemoteConfig 가 아직 기본값을 적용하지 않은 상태다. 그 기본값이 운영 주소다.
                return DeploymentEnvironment.Production;
            }

            SplitHostAndPath(baseUrl, out string host, out string path);

            // 경로 프리픽스가 호스트보다 우선한다 — 어떤 호스트에 올리든 /preview 는 Preview 다.
            if (HasPreviewPrefix(path))
            {
                return DeploymentEnvironment.Preview;
            }

            return string.Equals(host, DeployHost, StringComparison.OrdinalIgnoreCase)
                ? DeploymentEnvironment.Production
                : DeploymentEnvironment.Local;
        }

        /// <summary>
        /// 환경을 파일 시스템에 쓸 수 있는 캐시 키로 옮긴다 (S15P21A606-231).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 필요한가.</b> <c>Application.persistentDataPath</c> 는 환경과 무관하게 같은 폴더다.
        /// 한 PC 에서 Preview 빌드와 Production 빌드를 번갈아 실행하면 서로의 Data 캐시를 지우고
        /// (<c>CleanUpOtherVersions</c>), 두 환경이 같은 버전 문자열을 내면 <b>다른 내용을 그대로 쓴다</b> —
        /// 캐시가 있으면 해시를 다시 검증하지 않기 때문이다.
        /// </para>
        /// <para>
        /// <see cref="DeploymentEnvironment.Local"/> 은 주소에서 뽑은 짧은 해시를 덧붙인다.
        /// 개발자가 서버를 여러 개 띄워도 서로의 캐시를 밟지 않는다.
        /// </para>
        /// <para>
        /// <b><see cref="string.GetHashCode"/> 를 쓰지 않는다.</b> .NET Core 에서 그 값은 실행할 때마다
        /// 달라져 캐시 폴더가 매번 새로 생긴다. 직접 계산한 FNV-1a 는 언제 어디서 돌려도 같은 값이다.
        /// </para>
        /// </remarks>
        public static string ResolveCacheKey(string baseUrl)
        {
            DeploymentEnvironment environment = Resolve(baseUrl);

            switch (environment)
            {
                case DeploymentEnvironment.Production:
                    return "production";
                case DeploymentEnvironment.Preview:
                    return "preview";
                default:
                    return "local-" + ShortHash(Normalize(baseUrl));
            }
        }

        /// <summary>비교·해시 전에 주소를 한 모양으로 만든다. 스킴·대소문자·후행 슬래시를 지운다.</summary>
        private static string Normalize(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return string.Empty;
            }

            SplitHostAndPath(baseUrl, out string host, out string path);
            return (host + path).TrimEnd('/').ToLowerInvariant();
        }

        /// <summary>FNV-1a 32비트. 암호용이 아니라 폴더 이름을 가르기 위한 것이다.</summary>
        private static string ShortHash(string value)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;

                uint hash = offset;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= prime;
                }

                return hash.ToString("x8");
            }
        }

        private static void SplitHostAndPath(string baseUrl, out string host, out string path)
        {
            string rest = baseUrl.Trim();

            int scheme = rest.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                rest = rest.Substring(scheme + 3);
            }

            int slash = rest.IndexOf('/');
            if (slash < 0)
            {
                host = rest;
                path = string.Empty;
                return;
            }

            host = rest.Substring(0, slash);
            path = rest.Substring(slash);
        }

        private static bool HasPreviewPrefix(string path)
        {
            string trimmed = path.Trim('/');
            if (trimmed.Length == 0)
            {
                return false;
            }

            int end = trimmed.IndexOf('/');
            string first = end < 0 ? trimmed : trimmed.Substring(0, end);

            return string.Equals(first, PreviewPathSegment, StringComparison.OrdinalIgnoreCase);
        }
    }
}
