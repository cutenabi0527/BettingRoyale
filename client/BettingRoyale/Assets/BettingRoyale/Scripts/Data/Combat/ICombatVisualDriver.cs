using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 캐릭터·의상 선택과 계정 소유 초기 프리셋을 권위 상태로 표현하기 위한 Fusion 비의존 경계.
    /// 네트워크 계층은 계정 프로필의 preset seed만 전달하고, 실제 Body/Face/의상 조합은 구현체가 카탈로그에서 해석한다.
    /// </summary>
    public interface ICombatVisualDriver
    {
        int CharacterIndex { get; }
        int OutfitIndex { get; }
        string CharacterName { get; }
        string OutfitName { get; }
        Transform RootTransform { get; }

        void SelectPreviousCharacter();
        void SelectNextCharacter();
        void SelectPreviousOutfit();
        void SelectNextOutfit();
        void ApplyPresentation(int characterIndex, int outfitIndex);
        bool ApplyRandomPreset(int seed);

        /// <summary>
        /// 저장 외형(문자열 ID)을 이 드라이버의 공용 카탈로그 칸 번호로 옮긴다 (S-CLI-CQETNV).
        /// 슬롯 순서는 <c>CharacterAppearanceSlotCatalog.All</c>, 모르는 ID 는 -1(기본 파츠). 하나라도 옮겼으면 <c>true</c>.
        /// </summary>
        bool TryEncodeAppearancePreset(in BettingRoyale.Data.Contracts.CharacterAppearancePreset preset, int[] slotIndices);

        /// <summary>칸 번호(-1 = 기본 파츠)를 슬롯 순서대로 적용한다. 몸 → 얼굴 → 의상 순서를 드라이버가 지킨다.</summary>
        bool ApplyAppearanceIndices(System.Collections.Generic.IReadOnlyList<int> slotIndices);
    }

    /// <summary>
    /// 같은 빈사 상태라도 로컬 관전자에 따라 유령을 보일지 결정하는 표현 경계.
    /// 생존 피어에는 시체만 남고, 입력 권한을 가진 빈사 플레이어만 자기 유령을 본다.
    /// </summary>
    public interface ICombatGhostPresentationDriver
    {
        void SetGhostVisibleToLocalViewer(bool visible);
    }
}
