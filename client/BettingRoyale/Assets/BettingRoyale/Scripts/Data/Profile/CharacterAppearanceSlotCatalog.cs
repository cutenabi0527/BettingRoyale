using System;
using System.Collections.Generic;
using System.Text;

namespace BettingRoyale.Data.Profile
{
    /// <summary>
    /// 인게임 외형에서 편집하는 칸의 정본 목록
    /// ([S-UX-Q4FKHA](../../../../../../docs/specs/ui-ux/S-UX-Q4FKHA.md)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>여기에는 ID 와 순서만 둔다.</b> 사람이 읽을 한국어 이름은 UI 계층이 매핑한다 —
    /// <c>LobbyWeaponChoiceRules</c> 와 같은 규칙이다(<c>docs/client/ui-domain-contract.md</c>).
    /// 실제 프리팹·메시는 런타임 선택기가 들고 있고 이 계층은 <c>UnityEngine</c> 을 모른다(D-060).
    /// </para>
    /// <para>
    /// 목록은 S-UX-Q4FKHA 의 "최소 편집 항목" 을 그대로 옮긴 것이다 — Body(+Ears), Face 와
    /// 9개 의상 슬롯으로 <b>모두 11칸</b>이다. Ears 는 Body 와 같은 피부색 쌍이라 한 칸으로 다룬다
    /// (<c>CharacterBodyVariantCatalogEntry</c> 가 Body·Ears 프리팹을 함께 들고 있다).
    /// </para>
    /// <para>
    /// ⚠️ <b>ID 를 바꾸거나 지우지 않는다.</b> 서버에 저장된 프리셋의 키이므로, 바꾸면 저장된
    /// 외형이 어느 칸의 것인지 알 수 없게 된다. 새 칸은 뒤에 추가만 한다.
    /// </para>
    /// </remarks>
    public static class CharacterAppearanceSlotCatalog
    {
        public const string Body = "body";
        public const string Face = "face";
        public const string Glasses = "glasses";
        public const string Gloves = "gloves";
        public const string Hairstyle = "hairstyle";
        public const string Hat = "hat";
        public const string Mask = "mask";
        public const string Outwear = "outwear";
        public const string Pants = "pants";
        public const string Shorts = "shorts";
        public const string Shoe = "shoe";

        /// <summary>
        /// 화면에 그리는 순서. Body·Face 가 먼저이고 그 뒤는 디버그 패널이 쓰던 의상 순서와 같다
        /// — 검수하는 사람이 같은 순서를 두 곳에서 보게 하려는 것이다.
        /// </summary>
        public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
        {
            Body, Face, Glasses, Gloves, Hairstyle, Hat, Mask, Outwear, Pants, Shorts, Shoe,
        });

        /// <summary>
        /// 0번이 "기본"(원본 파츠)인 칸. 의상 9칸이 여기 해당한다.
        /// </summary>
        /// <remarks>
        /// Body·Face 에는 "없음" 이 없다 — 얼굴 없는 캐릭터를 만들 수 없기 때문이다.
        /// 선택기도 두 칸만 0부터 카탈로그를 세고 나머지는 0을 원본으로 비워 둔다.
        /// </remarks>
        public static bool HasDefaultOption(string slotId) =>
            IsKnown(slotId)
            && !string.Equals(slotId, Body, StringComparison.Ordinal)
            && !string.Equals(slotId, Face, StringComparison.Ordinal);

        public static bool IsKnown(string slotId) => IndexOf(slotId) >= 0;

        /// <summary>표시 순서 안에서의 자리. 모르는 ID 면 <c>-1</c>.</summary>
        public static int IndexOf(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return -1;
            for (var i = 0; i < All.Count; i++)
                if (string.Equals(All[i], slotId, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// 공급사 프리팹 이름·표시 이름을 저장 가능한 항목 ID 로 바꾼다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 아트 원본 이름은 공백·한글·괄호가 섞일 수 있고 공급사가 바꿀 수 있다. 그대로 저장하면
        /// 서버 열 길이·URL·JSON 키에서 매번 다른 문제를 만든다. 그래서 <b>영숫자와 밑줄만</b>
        /// 남긴다.
        /// </para>
        /// <para>
        /// ⚠️ <b>대소문자는 보존한다.</b> 서버 옷장 카탈로그(<c>V6__create_account_wardrobe.sql</c>)가
        /// 공급사 이름을 그대로 담고 있어(<c>Costume_1_01</c>·<c>Female_Emotion_Anger_01</c>),
        /// 소문자로 내리면 저장이 전부 <c>400 VALIDATION_ERROR</c> 가 된다. 두 카탈로그는 같은
        /// 문자열을 써야 한다 (api.md 8절).
        /// </para>
        /// <para>
        /// ⚠️ <b>되돌릴 수 없는 변환이다.</b> 서로 다른 두 원본 이름이 같은 ID 로 뭉개질 수 있으므로,
        /// 이 결과를 그대로 저장 키로 쓰는 쪽(<c>CharacterAppearanceCatalog</c>)이 중복을 검사한다.
        /// </para>
        /// </remarks>
        public static string NormalizeId(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            var builder = new StringBuilder(rawName.Length);
            var lastWasSeparator = true;
            for (var i = 0; i < rawName.Length; i++)
            {
                char c = rawName[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    builder.Append(c);
                    lastWasSeparator = false;
                }
                else if (!lastWasSeparator)
                {
                    builder.Append('_');
                    lastWasSeparator = true;
                }
            }

            // 꼬리 밑줄은 떼어 낸다 — "Hat (A)" 와 "Hat A" 가 다른 ID 가 되지 않게 한다.
            while (builder.Length > 0 && builder[builder.Length - 1] == '_')
                builder.Length -= 1;

            return builder.ToString();
        }
    }
}
