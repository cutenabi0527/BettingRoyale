using System.Text;

namespace BettingRoyale.Data.Online
{
    /// <summary>
    /// Photon <c>AppVersion</c> 에 실을 문자열을 만드는 규칙 (S-ONL-RBCJKD · D-ONL-WD9WX9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 값이 무엇을 하는가.</b> Photon 은 <c>AppVersion</c> 이 다른 피어를 같은 AppId 안에서도
    /// <b>서로 보이지 않게 격리한다.</b> 방 목록에도 뜨지 않고 이름을 알아도 참가되지 않는다.
    /// 비어 있으면 격리가 전혀 없어 <b>복제 구조체의 모양이 다른 빌드끼리 접속한다</b> —
    /// 역직렬화가 밀려 위치·체력·대시 상태가 조용히 어긋난 채 돌아간다
    /// ([S15P21A606-234]( · 사고 원본은 -229).
    /// </para>
    /// <para>
    /// <b>왜 순수 코드인가.</b> 이 규칙은 <c>UnityEngine</c> 도 Fusion 도 참조하지 않는다.
    /// 그래야 <c>client/Tests/</c> 의 도메인 테스트가 씬·에디터 없이 그대로 검증한다(D-060).
    /// Fusion 에 실제로 넘기는 일은 <c>Scripts/Network/</c> 가 맡는다(D-009).
    /// </para>
    /// </remarks>
    public static class NetworkProtocolVersion
    {
        /// <summary>
        /// 이 코드가 말하는 복제 계약의 세대. <b>복제 계약을 바꾸면 이 값을 올린다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// 올려야 하는 변경은 <b>회선을 넘는 모양이 달라지는 것</b>이다 —
        /// <c>INetworkStruct</c> 의 필드 추가·삭제·순서 변경·타입 변경, <c>[Networked]</c> 속성의
        /// 추가·삭제, 신뢰 데이터 스냅샷(<c>'ECMD'</c>/<c>'ESTA'</c>)의 형식 변경, RPC 시그니처 변경.
        /// 표시·연출만 바뀌었다면 올리지 않는다.
        /// </para>
        /// <para>
        /// ⚠️ <b>이걸 올리는 것을 잊으면 격리가 동작하지 않는다.</b> 이 값이 팀원끼리 서로 붙을 수
        /// 있게 해 주는 대가다(D-ONL-WD9WX9). 배포 빌드는 커밋 스탬프가 따로 붙어 잊더라도
        /// 다른 빌드와 섞이지 않지만, <b>개발 빌드끼리는 이 값 하나에만 기댄다.</b>
        /// </para>
        /// <para>
        /// <b>세대 이력.</b> 올릴 때 여기에 한 줄 추가한다 — <b>이 목록이 "올려야 하나" 의 판단
        /// 기준이다.</b> 위 문단의 규칙은 사람마다 해석이 갈리지만, 실제로 올렸던 사례는 갈리지 않는다.
        /// </para>
        /// <list type="bullet">
        ///   <item><description>
        ///     <c>br-proto-1</c> · 2026-09-18 · 최초 도입. 그전까지 <c>AppVersion</c> 이 비어 있어
        ///     복제 계약이 다른 빌드끼리 접속했다 (S15P21A606-234 · 사고 원본 -229).
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-2</c> · 2026-09-20 · 대기방 봇 (S15P21A606-188). 전투원에
        ///     <c>[Networked] ReplicatedLobbyBotNumber</c> 가 늘어 <b>복제 상태 블록의 배치가 달라졌고</b>,
        ///     훈련 스냅샷 판도 5 → 6 으로 올랐다(전투 봇 수). 둘 다 예전 빌드와 섞이면 값이 어긋난다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-3</c> · 2026-09-21 · 무기 선택 거절을 소유자에게 돌려주는 RPC 추가
        ///     (S15P21A606-257 · S-CLI-XTRYQD). RPC 테이블이 달라져 이전 세대와 격리한다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-4</c> · 2026-09-21 · 상자 중심 위치 <c>[Networked]</c> 2필드 추가 (S15P21A606-274 · S-GD-XRNJ7R).
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-5</c> · 2026-09-21 · 저장 외형 복제 (S15P21A606-274 · S-CLI-CQETNV). 전투원에
        ///     <c>[Networked] ReplicatedAppearancePresets</c>(8워드 구조체)가 늘고 <c>PlayerInfo</c> 에
        ///     <c>RPC_SetAppearancePresets</c> 가 추가돼 복제 블록·RPC 테이블이 모두 달라졌다.
        ///     같은 세대에서 <c>'ESTA'</c> 축복 계약 블록(Kind 14)도 거절 2바이트만큼 늘었다(11 → 13,
        ///     S-GD-XBP3VA) — 세대가 이미 갈렸으므로 구버전과 섞이지 않는다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-6</c> · 2026-09-23 · 코어루프 스냅샷 판 7 → 8 (S15P21A606-292 · S-ONL-9D34TP).
        ///     순위표 줄마다 Host 가 정한 이번 라운드 결과(순위·확정 여부·라운드 점수·킬·어시)가 늘었다.
        ///     판 번호가 달라 구버전 스냅샷은 해석하지 않고 버리지만, 그러면 코어루프가 멈춘 채로
        ///     방에 남으므로 세대를 갈라 애초에 같은 방에 들어오지 못하게 한다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-7</c> · 2026-09-23 · 강화 성공률 (S15P21A606-304). <c>'ESTA'</c> 강화 상태 블록(Kind 12)의
        ///     성공률 한 칸(int)이 무기·방어구 다음 단계 성공률 두 칸이 되어 4바이트 늘었다. 구버전과 섞이면 칸이
        ///     어긋나 확률이 틀리게 보이므로 세대를 가른다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-8</c> · 2026-09-23 · 틱 송신 간격 1 (S15P21A606-275 · D-ONL-KXGR35).
        ///     <b>복제 블록이나 RPC 표가 달라진 것은 아니다</b> — 대신 <c>NetworkProjectConfig</c> 의
        ///     <c>ClientSendInterval</c>·<c>ServerSendInterval</c> 이 2 에서 1 로 바뀌어 <b>스냅샷이 오가는
        ///     주기 자체</b>가 달라졌다. 이 값은 <b>두 피어가 같아야 하는 세션 계약</b>이라 옛 빌드와
        ///     섞이면 한쪽이 기대한 주기로 상태가 오지 않는다. 계약의 "모양" 이 아니라 "박자" 가
        ///     달라진 경우이며, 같은 방에 들어오지 못하게 하는 이유는 똑같다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-9</c> · 2026-09-24 · HUD 효과 줄을 주인에게만 (S15P21A606-300 · D-ONL-S62W2S).
        ///     전투원의 <c>[Networked] ReplicatedHudEffects</c>(36칸)·<c>ReplicatedHudEffectCount</c> 가 빠져
        ///     <b>복제 상태 블록의 배치가 달라졌고</b>, 신뢰 데이터에 주인 전용 통로 <c>'HUDE'</c> 가 늘었다.
        ///     구버전과 섞이면 전투원 상태를 엇갈려 읽고, 구버전 client 는 효과 칸이 빈다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-10</c> · 2026-09-25 · 전용 게임 서버와 방장 분리 (S15P21A606-321 · D-ONL-WXYT89).
        ///     신뢰 데이터에 방장 → 서버 요청 통로 <c>'OCMD'</c> 가 늘었고, <c>PlayerInfo.IsHost</c> 의 뜻이
        ///     "Fusion Host" 에서 "방장" 으로 바뀌었다(전용 서버에서는 서버가 정하고 방장이 나가면 넘긴다).
        ///     구버전 client 는 전용 서버 방에서 방장 버튼을 쓸 수 없다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-11</c> · 2026-09-26 · 전투 진입 절차 (S15P21A606-326 · S-GD-C66RN8). 코어 루프 스냅샷이 v9 로
        ///     올라 전투 진입 현황(대기·카운트다운·준비 인원)을 싣고, client → Host 전투 맵 준비 보고 <c>'BRCE'</c> 가 늘었다.
        ///   </description></item>
        ///   <item><description>
        ///     <c>br-proto-12</c> · 2026-09-26 · 준비 → 전투 페이드 전환 (S15P21A606-326 · S-GD-4JDCGB). 코어 루프 스냅샷 v10 이
        ///     대기 여부 대신 전환 단계(페이드 아웃·검은 화면·페이드 인·카운트다운)와 단계 남은 시간을 싣는다.
        ///   </description></item>
        /// </list>
        /// </remarks>
        public const string ProtocolId = "br-proto-12";

        /// <summary>프로토콜 세대와 빌드 스탬프를 가르는 글자. <c>Application.version</c> 에서도 같은 글자를 쓴다.</summary>
        public const char BuildStampSeparator = '+';

        /// <summary>
        /// 빌드 스탬프에 허용하는 길이. 짧은 커밋 SHA(7~12자)를 넉넉히 담으면서,
        /// 실수로 긴 문자열이 들어와도 <c>AppVersion</c> 이 비대해지지 않게 자른다.
        /// </summary>
        public const int MaxBuildStampLength = 24;

        /// <summary>
        /// 이 실행이 Photon 에 보낼 <c>AppVersion</c>. <paramref name="applicationVersion"/> 은
        /// <c>Application.version</c>(= <c>bundleVersion</c>) 을 그대로 넘긴다.
        /// </summary>
        /// <remarks>
        /// 에디터와 스탬프 없는 개발 빌드는 <c>bundleVersion</c> 에 구분자가 없으므로 결과가
        /// <see cref="ProtocolId"/> 하나다 — 그래서 <b>팀원끼리, 그리고 에디터와 빌드가 서로 붙는다.</b>
        /// </remarks>
        public static string Compose(string applicationVersion) =>
            Compose(ProtocolId, ExtractBuildStamp(applicationVersion));

        /// <summary>프로토콜 세대와 빌드 스탬프를 합친다. <b>어떤 입력에도 빈 문자열을 돌려주지 않는다.</b></summary>
        /// <remarks>
        /// 빈 값을 돌려주면 이 버그가 그대로 되살아나므로, 세대가 정제 후 비면
        /// <see cref="ProtocolId"/> 로 되돌린다. 조용히 격리를 푸는 경로를 남기지 않는다.
        /// </remarks>
        public static string Compose(string protocolId, string buildStamp)
        {
            string generation = Sanitize(protocolId);
            if (generation.Length == 0) generation = ProtocolId;

            string stamp = ClampStamp(buildStamp);
            return stamp.Length == 0 ? generation : generation + BuildStampSeparator + stamp;
        }

        /// <summary><c>Application.version</c> 에 붙어 있는 빌드 스탬프를 꺼낸다. 없으면 빈 문자열.</summary>
        public static string ExtractBuildStamp(string applicationVersion)
        {
            if (string.IsNullOrEmpty(applicationVersion)) return string.Empty;

            int separator = applicationVersion.IndexOf(BuildStampSeparator);
            return separator < 0 ? string.Empty : ClampStamp(applicationVersion.Substring(separator + 1));
        }

        /// <summary>
        /// 빌드 진입점이 <c>bundleVersion</c> 에 스탬프를 붙일 때 쓴다 (<c>CommandLineBuild</c>).
        /// 이미 붙어 있던 스탬프는 갈아 끼운다 — 두 번 붙여 <c>0.1.0+a+b</c> 가 되지 않게 한다.
        /// </summary>
        public static string StampApplicationVersion(string applicationVersion, string buildStamp)
        {
            string baseVersion = StripBuildStamp(applicationVersion);
            string stamp = ClampStamp(buildStamp);

            if (stamp.Length == 0) return baseVersion;
            return baseVersion.Length == 0 ? stamp : baseVersion + BuildStampSeparator + stamp;
        }

        /// <summary>스탬프를 뗀 원래 <c>bundleVersion</c>. 빌드가 끝나고 되돌릴 값을 구할 때 쓴다.</summary>
        public static string StripBuildStamp(string applicationVersion)
        {
            if (string.IsNullOrEmpty(applicationVersion)) return string.Empty;

            int separator = applicationVersion.IndexOf(BuildStampSeparator);
            return separator < 0 ? applicationVersion : applicationVersion.Substring(0, separator);
        }

        /// <summary>
        /// <c>AppVersion</c> 에 실어도 안전한 글자만 남긴다 — 영숫자와 <c>. _ -</c>.
        /// </summary>
        /// <remarks>
        /// 공백·구분자·제어 문자가 섞이면 값이 어디서 잘렸는지 사람이 알아채기 어렵다.
        /// 버리지 않고 남기면 "왜 격리가 됐지" 를 추적할 수 없게 되므로 애초에 받지 않는다.
        /// </remarks>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool allowed = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || c == '.' || c == '_' || c == '-';
                if (allowed) builder.Append(c);
            }

            return builder.ToString();
        }

        private static string ClampStamp(string buildStamp)
        {
            string stamp = Sanitize(buildStamp);
            return stamp.Length > MaxBuildStampLength ? stamp.Substring(0, MaxBuildStampLength) : stamp;
        }
    }
}
