using System;
using BettingRoyale.Data.Profile;

namespace BettingRoyale.Data.Dto
{
    // ─────────────────────────────────────────────────────────────
    //  계정 외형("옷장") 전송 형식. 정본은 docs/server/api.md 8절 (D-BE-Z2HMR6).
    //
    //  ⚠️ 필드 이름이 곧 계약이다. JsonUtility 는 이름으로만 맞추므로 바꾸면 조용히 null 이 된다.
    //     서버와 이름을 맞추기 위해 public 필드를 쓴다 — lint-rules.ps1 이 이 폴더를 예외로 둔다.
    //
    //  ⚠️ UnityEngine 을 참조하지 않는다(D-060). JSON 을 읽고 쓰는 것은 Network/Api 의 몫이고,
    //     이 파일은 그 결과가 담기는 그릇과 순수 변환만 갖는다.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 프리셋 한 벌의 슬롯→항목 대응 (api.md 8-1 의 11칸).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>서버는 이 자리를 자유로운 JSON 객체로 정의했지만 우리는 11칸을 고정 필드로 받는다.</b>
    /// <c>JsonUtility</c> 에는 <c>Dictionary</c> 가 없고(Json.NET 계열은 MCP for Unity 의 간접
    /// 의존성이라 쓰지 않는다 — D-021), 슬롯 목록은
    /// <see cref="CharacterAppearanceSlotCatalog"/> 가 이미 정본으로 갖고 있다.
    /// </para>
    /// <para>
    /// 그래서 <b>서버가 슬롯을 늘리면 그 칸은 조용히 버려진다.</b> 화면도 모르는 칸은 버리므로
    /// (<c>CharacterAppearanceDraft.StoreInto</c>) 동작이 어긋나지는 않지만, 슬롯이 늘어나는 날에는
    /// 이 클래스와 <see cref="CharacterAppearanceSlotCatalog"/> 를 함께 고쳐야 한다.
    /// </para>
    /// <para>
    /// 값이 <c>null</c> 인 칸은 "정하지 않음" 이다 — 서버는 빈 슬롯을 응답에 싣지 않는다.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class WardrobeItemsDto
    {
        public string body;
        public string face;
        public string glasses;
        public string gloves;
        public string hairstyle;
        public string hat;
        public string mask;
        public string outwear;
        public string pants;
        public string shorts;
        public string shoe;

        /// <summary>
        /// 슬롯 ID 로 항목을 꺼낸다. 모르는 슬롯이거나 정하지 않았으면 빈 문자열이다.
        /// </summary>
        /// <remarks>
        /// <b>슬롯 이름을 여기에 다시 적지 않는다.</b> <see cref="CharacterAppearanceSlotCatalog"/>
        /// 의 상수로만 갈라서, 칸 이름이 바뀌면 이 <c>switch</c> 가 함께 깨지게 한다.
        /// </remarks>
        public string Get(string slotId)
        {
            switch (slotId)
            {
                case CharacterAppearanceSlotCatalog.Body: return body ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Face: return face ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Glasses: return glasses ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Gloves: return gloves ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Hairstyle: return hairstyle ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Hat: return hat ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Mask: return mask ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Outwear: return outwear ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Pants: return pants ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Shorts: return shorts ?? string.Empty;
                case CharacterAppearanceSlotCatalog.Shoe: return shoe ?? string.Empty;
                default: return string.Empty;
            }
        }
    }

    /// <summary>무기 유형 하나의 저장된 프리셋 (api.md 8-3 의 <c>presets[]</c> 원소, 8-4 의 응답).</summary>
    /// <remarks>
    /// ⚠️ <b><see cref="revision"/> 이 0 이면 "한 번도 저장하지 않음" 이다.</b> "전부 기본값으로
    /// 저장함"(<c>revision ≥ 1</c> + 빈 <c>items</c>)과 다른 상태이며, 0 인 동안 화면은 계속
    /// <c>appearancePresetSeed</c> 외형을 쓴다(api.md 8-2). 둘을 섞으면 저장 전 외형이 바뀐다.
    /// </remarks>
    [Serializable]
    public sealed class WardrobePresetDto
    {
        /// <summary>서버 정본 표기의 무기 유형(<c>Sword</c>). 계약 키는 소문자라 변환해서 쓴다.</summary>
        public string weaponType;

        public long revision;

        /// <summary>마지막 저장 시각. 한 번도 저장하지 않았으면 <c>null</c> 이다.</summary>
        public string updatedAt;

        public WardrobeItemsDto items;
    }

    /// <summary>옷장 조회 응답 (api.md 8-3).</summary>
    [Serializable]
    public sealed class WardrobeSnapshotDto
    {
        /// <summary>편집할 수 있는 무기 유형. 서버 설정(<c>wardrobe.weapon-types</c>)이 정한다.</summary>
        public string[] weaponTypes;

        /// <summary>서버가 받아들이는 슬롯 이름 11개. 화면은 자기 카탈로그를 쓰므로 참고용이다.</summary>
        public string[] slots;

        /// <summary>
        /// 계정이 소유한 항목 ID. <b>빈 배열과 "필드 자체가 없음" 은 다른 뜻이다</b> —
        /// 후자는 구버전 서버라 소유를 판정하지 못한 상태다(S-UX-4HAX9Z 6항).
        /// </summary>
        public string[] ownedItemIds;

        /// <summary><see cref="weaponTypes"/> 전부가 항상 들어온다. 저장 전이어도 빠지지 않는다.</summary>
        public WardrobePresetDto[] presets;
    }

    /// <summary>외형 의상 구매 요청 본문 (api.md 8-7).</summary>
    [Serializable]
    public sealed class WardrobePurchaseRequestDto
    {
        public string itemId;
    }

    /// <summary>
    /// 구매 응답 (api.md 8-7) — 산 아이템과 <b>남은 잔액·갱신된 소유 목록</b>.
    /// </summary>
    /// <remarks>
    /// 잔액을 함께 싣는 이유는 화면이 구매 직후 계정을 다시 조회하지 않게 하기 위해서다.
    /// </remarks>
    [Serializable]
    public sealed class WardrobePurchaseDto
    {
        public string itemId;
        public long medalBalance;
        public string[] ownedItemIds;
    }
}
