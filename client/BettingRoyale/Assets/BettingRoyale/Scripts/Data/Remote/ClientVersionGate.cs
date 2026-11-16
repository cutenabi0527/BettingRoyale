using System;

namespace BettingRoyale.Data.Remote
{
    /// <summary>이 실행이 서버가 배포한 최신 클라이언트인지 판정한다 (S15P21A606-289).</summary>
    /// <remarks>
    /// <para>
    /// 버전이 다르면 같이 플레이할 수 없다 — Photon 이 <c>AppVersion</c> 으로 호환 경계를 나누기
    /// 때문에 방이 서로 보이지 않는다(D-ONL-WD9WX9). 그런데 지금은 게임이 그 사실을 알려주지
    /// 않아 사용자는 "방이 안 보인다" 로만 겪는다. 이 판정이 그 원인을 말해 주기 위한 것이다.
    /// </para>
    /// <para>
    /// <b>비교하는 값은 커밋 스탬프다.</b> <c>latest.json</c> 의 <c>version</c>(<c>b7-758e1586</c>)과
    /// <c>Application.version</c>(<c>0.1.0+758e1586</c>)은 값 공간이 다르므로 직접 비교할 수 없다.
    /// 둘이 공유하는 유일한 열쇠가 커밋이며, 빌드가 <c>bundleVersion</c> 에 심어 둔다
    /// (<c>CommandLineBuild.ApplyBuildStamp</c>).
    /// </para>
    /// <para>
    /// ⚠️ <b>확신이 없으면 통과시킨다.</b> 스탬프가 없는 개발 빌드, 읽지 못한 <c>latest.json</c>,
    /// 빈 커밋 값, 커밋처럼 생기지 않은 값(롤백 스크립트가 적는 <c>unknown</c> 등)은 모두
    /// <see cref="ClientVersionVerdict.Unknown"/> 이다. 막는 것은 <b>양쪽 값을
    /// 모두 확인했고 서로 다를 때뿐</b>이다. 판정이 틀려서 막으면 아무도 게임에 들어오지 못하는데,
    /// 그 사고는 배포 직후가 아니라 사용자 PC 에서만 드러나 되돌리기가 느리다.
    /// </para>
    /// </remarks>
    public static class ClientVersionGate
    {
        /// <summary>서버가 배포한 최신 클라이언트와 이 실행을 견준 결과.</summary>
        public enum ClientVersionVerdict
        {
            /// <summary>판정할 수 없다. 개발 빌드이거나 서버 정보를 읽지 못했다 — 통과시킨다.</summary>
            Unknown = 0,

            /// <summary>서버가 배포한 것과 같은 빌드다.</summary>
            Current = 1,

            /// <summary>서버에 다른 빌드가 올라가 있다. 같이 플레이할 수 없다.</summary>
            Outdated = 2,
        }

        /// <summary>
        /// <paramref name="applicationVersion"/>(<c>Application.version</c>)과 서버가 알려준
        /// <paramref name="publishedCommit"/>(<c>latest.json</c> 의 <c>commit</c>)을 견준다.
        /// </summary>
        /// <param name="localCommit">이 빌드의 커밋 스탬프. 판정 결과와 무관하게 채워 준다(표시용).</param>
        public static ClientVersionVerdict Evaluate(
            string applicationVersion, string publishedCommit, out string localCommit)
        {
            localCommit = Data.Online.NetworkProtocolVersion.ExtractBuildStamp(applicationVersion);

            // 스탬프가 없는 빌드는 사람이 손으로 만든 개발 빌드다. 그것까지 막으면 개발이 멈춘다.
            if (string.IsNullOrWhiteSpace(localCommit))
            {
                return ClientVersionVerdict.Unknown;
            }

            if (string.IsNullOrWhiteSpace(publishedCommit))
            {
                return ClientVersionVerdict.Unknown;
            }

            // 서버는 짧은 커밋(8자)을 주고 빌드 스탬프도 같은 값에서 왔다. 그래도 길이가
            // 어긋날 수 있으므로 짧은 쪽 길이로 견준다 — 한쪽이 더 길다고 다른 빌드는 아니다.
            string a = localCommit.Trim();
            string b = publishedCommit.Trim();

            // ⚠️ 커밋처럼 생기지 않은 값은 모르는 값이다 (D-REL-RBJ9AA). rollback-client.sh 는 과거
            // 기록이 없는 버전으로 되돌릴 때 commit 을 지어내지 않고 "unknown" 으로 적는다. 그 문자열을
            // 커밋으로 견주면 모든 배포 빌드가 Outdated 가 되어 롤백한 그 빌드까지 진입이 막힌다.
            // 너무 짧은 값도 마찬가지다 — 한 글자짜리 접두사는 아무 커밋과도 "같다" 가 된다.
            if (!LooksLikeCommit(a) || !LooksLikeCommit(b))
            {
                return ClientVersionVerdict.Unknown;
            }

            int length = Math.Min(a.Length, b.Length);

            bool same = string.Equals(
                a.Substring(0, length), b.Substring(0, length), StringComparison.OrdinalIgnoreCase);
            return same ? ClientVersionVerdict.Current : ClientVersionVerdict.Outdated;
        }

        /// <summary>Windows 클라이언트의 최신 빌드 정보 경로(정적 루트 기준).</summary>
        public const string WindowsLatestPath = "client/latest.json";

        /// <summary>Apple Silicon Mac 클라이언트의 최신 빌드 정보 경로(S15P21A606-311, Apple Silicon 계약 7절).</summary>
        public const string MacOSLatestPath = "client/macos/latest.json";

        /// <summary>
        /// 이 실행이 견줘야 할 <c>latest.json</c> 경로. 플랫폼마다 따로 게시되므로 서로의 커밋을 보면 안 된다 —
        /// Mac 게시가 늦거나 실패했을 때 Windows 커밋과 견주면 Mac 사용자 전원이 Outdated 로 막힌다.
        /// </summary>
        /// <param name="isMacOSPlayer">Mac 빌드(<c>RuntimePlatform.OSXPlayer</c>)인가. 에디터는 <c>false</c> 로 넘겨 기존 동작을 유지한다.</param>
        public static string LatestInfoPath(bool isMacOSPlayer) =>
            isMacOSPlayer ? MacOSLatestPath : WindowsLatestPath;

        /// <summary>git 짧은 커밋의 최소 길이. 이보다 짧으면 서로 다른 커밋을 가르지 못한다.</summary>
        public const int MinimumCommitLength = 7;

        /// <summary>16진수로만 이루어진 <see cref="MinimumCommitLength"/>자 이상의 값인가.</summary>
        private static bool LooksLikeCommit(string value)
        {
            if (value.Length < MinimumCommitLength)
            {
                return false;
            }

            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
