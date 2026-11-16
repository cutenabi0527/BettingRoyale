using System;
using System.Collections.Generic;
using BettingRoyale.Data.Contracts;

namespace BettingRoyale.Data.Profile
{
    /// <summary>
    /// 저장 외형 프리셋을 <b>공용 캐릭터 카탈로그의 칸 번호</b>로 옮긴 것 (S-CLI-CQETNV).
    /// 슬롯 순서는 <see cref="CharacterAppearanceSlotCatalog.All"/>, 값은 <see cref="CharacterAppearanceCatalog.IndexOf"/> 의 결과다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>번호는 한 빌드 안에서만 뜻이 있다.</b> 서버·저장 계약은 여전히 문자열 ID 다
    /// (<c>CharacterAppearanceContract</c>). 같은 프로토콜 세대(<c>NetworkProtocolVersion</c>)의 피어끼리만 방에 들어오므로
    /// 방 안 복제에는 번호를 쓴다 — 이미 <c>ReplicatedCharacterIndex</c>·<c>ReplicatedOutfitIndex</c> 가 같은 전제다.
    /// </para>
    /// <para><see cref="Missing"/>(-1) 은 "저장본에 그 슬롯이 없다 = 기본 파츠" 다 (api.md 8-2·8-4).</para>
    /// </remarks>
    public static class CharacterAppearanceSlotIndices
    {
        public const int Missing = -1;

        /// <summary>슬롯 수 = 11. 유선 구조체가 이 크기를 전제한다.</summary>
        public static int Count => CharacterAppearanceSlotCatalog.All.Count;

        public static int[] CreateEmpty()
        {
            var indices = new int[Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = Missing;
            return indices;
        }

        /// <summary>
        /// 프리셋 → 칸 번호. 카탈로그가 모르는 ID 는 <see cref="Missing"/> 으로 두고 <paramref name="unknownItemIds"/> 에 남긴다 —
        /// 예외·무한 로드 없이 기본 파츠로 복구하되 이유를 기록한다 (S-CLI-CQETNV 6항).
        /// </summary>
        /// <returns>하나라도 유효한 칸이 있으면 <c>true</c>.</returns>
        public static bool TryEncode(CharacterAppearanceCatalog catalog, in CharacterAppearancePreset preset,
            int[] slotIndices, List<string> unknownItemIds = null)
        {
            if (catalog == null || slotIndices == null || slotIndices.Length < Count) return false;
            bool any = false;
            IReadOnlyList<string> order = CharacterAppearanceSlotCatalog.All;
            for (int i = 0; i < order.Count; i++)
            {
                slotIndices[i] = Missing;
                if (!preset.TryGetItemId(order[i], out string itemId) || string.IsNullOrEmpty(itemId)) continue;
                int index = catalog.IndexOf(order[i], itemId);
                if (index < 0)
                {
                    unknownItemIds?.Add(order[i] + ":" + itemId);
                    continue;
                }

                slotIndices[i] = index;
                any = true;
            }

            return any;
        }

        /// <summary>칸 번호 → 프리셋(문자열 ID). 표시·검증용이다.</summary>
        public static CharacterAppearancePreset Decode(CharacterAppearanceCatalog catalog, string weaponKey, IReadOnlyList<int> slotIndices)
        {
            var selections = new List<CharacterAppearanceSelection>();
            if (catalog != null && slotIndices != null)
            {
                IReadOnlyList<string> order = CharacterAppearanceSlotCatalog.All;
                for (int i = 0; i < order.Count && i < slotIndices.Count; i++)
                {
                    if (slotIndices[i] < 0) continue;
                    string itemId = catalog.ItemIdAt(order[i], slotIndices[i]);
                    if (!string.IsNullOrEmpty(itemId)) selections.Add(new CharacterAppearanceSelection(order[i], itemId));
                }
            }

            return new CharacterAppearancePreset(weaponKey, selections);
        }

        public static bool HasAny(IReadOnlyList<int> slotIndices)
        {
            if (slotIndices == null) return false;
            for (int i = 0; i < slotIndices.Count; i++) if (slotIndices[i] >= 0) return true;
            return false;
        }

        public static bool SequenceEqual(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }
}
