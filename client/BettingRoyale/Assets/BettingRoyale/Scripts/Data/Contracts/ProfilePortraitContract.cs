using System;
using System.Collections.Generic;
using BettingRoyale.Core;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// 프로필 초상화를 바꿔 달라는 요청
    /// ([S-UX-RQ5N9E](../../../../../../docs/specs/ui-ux/S-UX-RQ5N9E.md)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 화면은 이 명령만 발행하고 끝난다. 서버 호출·토큰·복제는 어댑터의 몫이다 — UI 가
    /// 서버 DTO 나 Fusion 타입을 직접 쓰지 않는다(D-065).
    /// </para>
    /// <para>
    /// ⚠️ <b>이 요청이 곧 확정은 아니다.</b> 소유 검증은 서버가 하며 결과는
    /// <see cref="ProfilePortraitChanged"/> 로 돌아온다. 화면은 응답을 기다리지 않고
    /// 먼저 그려 두지 않는다 — 실패하면 되돌릴 자리가 없기 때문이다.
    /// </para>
    /// </remarks>
    public readonly struct ProfilePortraitChangeRequested : IGameEvent
    {
        public ProfilePortraitChangeRequested(string portraitId)
        {
            PortraitId = portraitId ?? string.Empty;
        }

        public string PortraitId { get; }
    }

    /// <summary>
    /// 프로필 화면을 열어 달라는 요청 (S-UX-RQ5N9E).
    /// </summary>
    /// <remarks>
    /// 진입점이 여럿이라(로비 계정 칩·메인 메뉴 프로필 칩) 각자 화면을 직접 켜지 않고 이 요청을
    /// 낸다. 받는 쪽은 상주 라우터 하나뿐이라 어느 진입점을 눌러도 같은 화면이 열린다.
    /// </remarks>
    public readonly struct ProfileScreenRequested : IGameEvent
    {
        private ProfileScreenRequested(bool openAppearance)
        {
            OpenAppearance = openAppearance;
        }

        /// <summary>
        /// 인게임 외형(옷장) 탭을 펼친 채로 열지 여부. 기본은 <c>false</c> — 초상화 탭이다.
        /// </summary>
        /// <remarks>
        /// <b>왜 요청이 탭을 정하는가.</b> 대기방의 진입점은 "내 의상을 바꾸겠다" 는 뜻이 분명한데
        /// (S15P21A606-153), 화면이 늘 초상화 탭으로 열리면 사용자가 탭을 한 번 더 찾아야 한다.
        /// 반대로 계정 칩·메인 메뉴의 프로필은 초상화가 먼저다. 진입점이 의도를 알고 있으므로
        /// 요청에 싣는다 — 화면이 어디서 눌렸는지 되짚게 만들지 않는다.
        /// <para>
        /// 기본 생성자(<c>default</c>)가 초상화 탭이므로 기존 진입점은 한 글자도 바꾸지 않는다.
        /// </para>
        /// </remarks>
        public bool OpenAppearance { get; }

        /// <summary>내 의상(인게임 외형) 편집으로 바로 들어가는 요청.</summary>
        public static ProfileScreenRequested ToAppearance() => new ProfileScreenRequested(true);
    }

    /// <summary>프로필 초상화 저장 결과. 성공·실패를 한 이벤트로 받는다.</summary>
    /// <remarks>
    /// 실패도 같은 이벤트로 오는 이유는 화면이 <b>로딩을 끝낼 자리</b>가 한 곳이어야 하기
    /// 때문이다. 성공 이벤트만 있으면 실패했을 때 저장 버튼이 영원히 비활성으로 남는다.
    /// </remarks>
    public readonly struct ProfilePortraitChanged : IGameEvent
    {
        private readonly IReadOnlyList<string> owned;

        private ProfilePortraitChanged(
            bool success, string portraitId, IReadOnlyList<string> ownedPortraitIds, string failureMessage)
        {
            Success = success;
            PortraitId = portraitId ?? string.Empty;
            owned = ownedPortraitIds;
            FailureMessage = failureMessage ?? string.Empty;
        }

        public bool Success { get; }

        /// <summary>확정된 초상화. 실패면 <b>바뀌기 전 값</b>이다.</summary>
        public string PortraitId { get; }

        /// <summary>갱신된 소유 목록. 서버 응답에 없으면 비어 있다.</summary>
        public IReadOnlyList<string> OwnedPortraitIds => owned ?? Array.Empty<string>();

        /// <summary>사람이 읽을 실패 사유. 성공이면 빈 문자열이다.</summary>
        public string FailureMessage { get; }

        public static ProfilePortraitChanged Saved(string portraitId, IReadOnlyList<string> ownedPortraitIds) =>
            new(true, portraitId, ownedPortraitIds, string.Empty);

        public static ProfilePortraitChanged Failed(string currentPortraitId, string failureMessage) =>
            new(false, currentPortraitId, Array.Empty<string>(), failureMessage);
    }
}
