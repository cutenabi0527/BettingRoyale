using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Profile
{
    /// <summary>
    /// 외형 아이템 ID → 화면에 보일 한국어 이름
    /// ([S-UX-KJR3NR](../../../../../../docs/specs/ui-ux/S-UX-KJR3NR.md)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 아이템 ID(<c>Outwear_36</c>)는 서드파티 에셋의 파일 이름이라 사람이 읽을 것이 못 된다.
    /// 그렇다고 화면에서 밑줄만 공백으로 바꾸면 "Outwear 36" 이 되어 <b>무엇을 사는지 알 수 없다</b>.
    /// 그래서 이름을 여기 한 곳에 모은다.
    /// </para>
    /// <para>
    /// ⚠️ <b>이름은 에이전트가 썸네일을 보고 지었다.</b> 원본 에셋에는 한국어 이름이 없고 기획이
    /// 정한 이름도 아직 없다. 기획이 이름을 정하면 이 표를 그 값으로 바꾼다 — 표를 고치는 것 말고는
    /// 할 일이 없도록 이름을 코드 곳곳에 흩뿌리지 않는다.
    /// </para>
    /// <para>
    /// 얼굴 40종만 규칙으로 만든다. <c>Male_Emotion_Angry_02</c> 처럼 성별·감정·번호가 ID 안에
    /// 그대로 들어 있어 표로 적으면 같은 문장을 40번 쓰게 되고, 에셋이 늘면 그만큼 빠뜨린다.
    /// </para>
    /// <para>
    /// 모르는 ID 는 밑줄만 공백으로 바꿔 돌려준다 — 빈 문자열로 만들지 않는다. 이름을 못 지은
    /// 아이템이 화면에서 <b>사라지는</b> 것보다 영문 ID 로라도 보이는 편이 낫다.
    /// </para>
    /// </remarks>
    public static class WardrobeItemNames
    {
        private const string EmotionMarker = "_Emotion_";

        /// <summary>얼굴 ID 의 감정 토큰 → 한국어. 철자는 원본 에셋을 따른다(<c>Surpriced</c>).</summary>
        private static readonly Dictionary<string, string> Emotions = new(StringComparer.Ordinal)
        {
            ["Anger"] = "성난",
            ["Angry"] = "골난",
            ["Confuse"] = "갸웃한",
            ["Cry"] = "우는",
            ["Evil"] = "음흉한",
            ["Happy"] = "웃는",
            ["Love"] = "반한",
            ["Neutral"] = "무표정한",
            ["Sad"] = "시무룩한",
            ["Shout"] = "외치는",
            ["Surpriced"] = "놀란",
            ["Tongue"] = "메롱하는",
            ["Usual"] = "평범한",
        };

        private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
        {
            // 피부 — 밝은 쪽에서 어두운 쪽 순서다.
            ["Body_09"] = "백자빛 피부",
            ["Body_10"] = "살구빛 피부",
            ["Body_11"] = "황토빛 피부",
            ["Body_12"] = "구릿빛 피부",
            ["Body_13"] = "흑단빛 피부",

            // 코스튬 — 한 벌이 여러 칸을 함께 채운다. 같은 옷의 조각임이 이름에서 보여야 한다.
            ["Costume_1_01"] = "별토끼 슈트",
            ["Costume_1_02"] = "별토끼 두건",
            ["Costume_1_03"] = "별토끼 부츠",
            ["Costume_5_02"] = "초록 가면 히어로",

            ["Glasses_05"] = "레몬 선글라스",
            ["Glasses_06"] = "핑크 동글 안경",
            ["Glasses_14"] = "3D 입체 안경",

            ["Gloves_03"] = "코발트 장갑",
            ["Gloves_07"] = "가죽 목장 장갑",
            ["Gloves_09"] = "흑요석 장갑",
            ["Gloves_10"] = "올리브 전술 장갑",
            ["Gloves_11"] = "잿빛 니트 장갑",
            ["Gloves_12"] = "체리 커프 장갑",

            ["Hairstyle_Female_06"] = "은빛 롱 웨이브",
            ["Hairstyle_Female_Single_008"] = "노랑 두 갈래 번",
            ["Hairstyle_Male_12"] = "잿빛 곱슬머리",
            ["Hairstyle_Male_13"] = "갈색 단정 머리",
            ["Hairstyle_Male_Single"] = "밤색 짧은 머리",

            ["Hat_03"] = "꽃게 모자",
            ["Hat_24"] = "분홍 토끼 모자",
            ["Hat_25"] = "광부 헬멧",
            ["Hat_44"] = "개구리 모자",
            ["Hat_48"] = "꼬끼오 모자",

            ["Mask_02"] = "검은 복면",

            ["Outwear_19"] = "흰 반팔 티",
            ["Outwear_28"] = "산호빛 반팔 티",
            ["Outwear_30"] = "블랙 라글란 티",
            ["Outwear_36"] = "벚꽃 후드 집업",
            ["Outwear_42"] = "귤빛 정장 재킷",

            ["Pants_02"] = "레몬 바지",
            ["Pants_06"] = "회색 카고 바지",
            ["Pants_07"] = "보라 줄무늬 바지",
            ["Pants_11"] = "숲빛 등산 바지",

            ["Shorts_01"] = "흰 롤업 반바지",
            ["Shorts_03"] = "초록 반바지",

            ["Shoe_Slippers_05"] = "개구리 슬리퍼",
            ["Shoe_Slippers_07"] = "여름 샌들",
            ["Shoe_Sneakers_05"] = "검정 운동화",
            ["Shoe_Sneakers_10"] = "분홍 운동화",
            ["Shoe_Sneakers_14"] = "흑백 러닝화",
        };

        /// <summary>화면에 보일 이름. 모르는 ID 면 밑줄을 공백으로 바꿔 돌려준다.</summary>
        public static string Resolve(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return string.Empty;
            if (Names.TryGetValue(itemId, out string name)) return name;
            if (TryResolveFace(itemId, out string face)) return face;

            return itemId.Replace('_', ' ');
        }

        /// <summary>
        /// <c>Male_Emotion_Angry_02</c> → "남 · 골난 얼굴 2".
        /// </summary>
        /// <remarks>
        /// 번호는 같은 감정이 두 벌 있을 때만 붙는다. 한 벌뿐인 감정에 "1" 을 붙이면 없는 형제가
        /// 있는 것처럼 보인다.
        /// </remarks>
        private static bool TryResolveFace(string itemId, out string name)
        {
            name = string.Empty;
            int marker = itemId.IndexOf(EmotionMarker, StringComparison.Ordinal);
            if (marker <= 0) return false;

            string gender = itemId.Substring(0, marker) switch
            {
                "Male" => "남",
                "Female" => "여",
                _ => string.Empty,
            };
            if (gender.Length == 0) return false;

            string rest = itemId.Substring(marker + EmotionMarker.Length);
            int split = rest.LastIndexOf('_');
            string token = split < 0 ? rest : rest.Substring(0, split);
            string ordinal = split < 0 ? string.Empty : rest.Substring(split + 1);
            if (!Emotions.TryGetValue(token, out string emotion)) return false;

            name = ordinal.Length == 0 || ordinal == "01"
                ? $"{gender} · {emotion} 얼굴"
                : $"{gender} · {emotion} 얼굴 {ordinal.TrimStart('0')}";
            return true;
        }
    }
}
