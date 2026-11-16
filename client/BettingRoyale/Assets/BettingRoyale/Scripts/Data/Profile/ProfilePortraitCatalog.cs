using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Profile
{
    /// <summary>
    /// 프로필 초상화 카탈로그 ([S-UX-RQ5N9E](../../../../../../docs/specs/ui-ux/S-UX-RQ5N9E.md)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ID 는 배열 순번이 아니라 그림을 알아볼 수 있는 이름이다 — 순번을 저장하면 목록에 하나를
    /// 끼워 넣는 순간 저장된 모든 계정의 프로필이 다른 그림으로 바뀐다.
    /// </para>
    /// <para>
    /// ⚠️ <b>서버의 <c>ProfilePortraits</c> 와 같은 ID 집합이어야 한다.</b> 소유와 유효성은 서버가
    /// 판정하므로, 한쪽만 늘리면 새 초상화를 고를 수 없거나(400) 화면에 빈 칸이 뜬다.
    /// </para>
    /// <para>
    /// 여기서는 <b>ID 와 순서만</b> 정한다. 스프라이트는 화면이 자기 아틀라스에서 같은 순서로 꺼낸다 —
    /// 도메인 계층은 <c>UnityEngine</c> 을 참조하지 않는다(D-060).
    /// </para>
    /// </remarks>
    public static class ProfilePortraitCatalog
    {
        /// <summary>표시 순서. <c>UI_LobbyPortraitAtlas</c> 의 슬라이스 순서와 같다.</summary>
        public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
        {
            "hood_cat",       // Portrait_0 보라 고양이 후드
            "hood_bear",      // Portrait_1 주황 곰 후드
            "hood_ranger",    // Portrait_2 초록 레인저 후드
            "hat_musketeer",  // Portrait_3 파란 깃털 모자
            "hood_rabbit",    // Portrait_4 분홍 토끼 후드
        });

        /// <summary>서버가 신규 계정에 주는 기본값. 응답이 비었을 때의 대체값이기도 하다.</summary>
        public const string Default = "hat_musketeer";

        /// <summary>DB 열 길이와 같다. 넘는 값은 보내기 전에 막는다.</summary>
        public const int MaxIdLength = 16;

        public static bool IsKnown(string portraitId) => IndexOf(portraitId) >= 0;

        /// <summary>표시 순서 안에서의 자리. 모르는 ID 면 <c>-1</c>.</summary>
        public static int IndexOf(string portraitId)
        {
            if (string.IsNullOrEmpty(portraitId)) return -1;
            for (var i = 0; i < All.Count; i++)
                if (string.Equals(All[i], portraitId, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// 그릴 수 있는 ID 로 떨어뜨린다. 모르는 값이나 빈 값이면 <see cref="Default"/> 다.
        /// </summary>
        /// <remarks>
        /// 서버가 아직 초상화를 모르는 구버전이거나, 카탈로그에서 빠진 초상화를 계정이 들고 있어도
        /// 화면에 빈 칸이 나지 않게 한다. <b>저장 값을 고치지는 않는다</b> — 표시용 대체일 뿐이다.
        /// </remarks>
        public static string Resolve(string portraitId) => IsKnown(portraitId) ? portraitId : Default;
    }
}
