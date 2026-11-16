using System;
using System.Collections.Generic;
using BettingRoyale.Data.Contracts;

namespace BettingRoyale.Data.Profile
{
    /// <summary>
    /// 편집 중인 외형 초안과 마지막 저장 상태
    /// ([S-UX-Q4FKHA](../../../../../../docs/specs/ui-ux/S-UX-Q4FKHA.md)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 스펙은 <b>"저장 전 변경은 미리보기 초안이고, 저장 성공 후에만 확정되며, 취소하면 마지막 저장
    /// 상태로 돌아간다"</b> 를 요구한다. 그러려면 두 벌이 필요하다 — 초안과 저장본. 한 벌만 들고
    /// 있으면 취소했을 때 되돌릴 자리가 없다.
    /// </para>
    /// <para>
    /// 무기 유형별로 따로 들고 있다. 한 무기를 편집하는 동안 다른 무기의 초안이 사라지면
    /// 사용자는 탭을 옮겼다는 이유로 편집 내용을 잃는다(S-GD-5NRMCY 3항).
    /// </para>
    /// <para>
    /// <c>UnityEngine</c> 을 모른다(D-060). 화면은 이 클래스가 계산한 결과만 그린다.
    /// </para>
    /// </remarks>
    public sealed class CharacterAppearanceDraft
    {
        private readonly Dictionary<string, Dictionary<string, string>> saved =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        private readonly Dictionary<string, Dictionary<string, string>> draft =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>서버(또는 어댑터)에서 저장본을 한 번이라도 받았는가.</summary>
        public bool HasSaved { get; private set; }

        /// <summary>저장본을 통째로 갈아 끼우고 초안을 그 위로 맞춘다. 조회 응답이 오면 부른다.</summary>
        public void LoadSaved(IReadOnlyList<CharacterAppearancePreset> presets)
        {
            saved.Clear();
            if (presets != null)
                for (var i = 0; i < presets.Count; i++) StoreInto(saved, presets[i]);

            HasSaved = true;
            Revert();
        }

        /// <summary>저장에 성공한 무기 하나만 저장본에 반영한다. 다른 무기의 초안은 건드리지 않는다.</summary>
        public void CommitSaved(CharacterAppearancePreset preset)
        {
            StoreInto(saved, preset);
            HasSaved = true;
            Revert(preset.WeaponKey);
        }

        /// <summary>초안을 마지막 저장 상태로 되돌린다 (취소).</summary>
        public void Revert()
        {
            draft.Clear();
            foreach (KeyValuePair<string, Dictionary<string, string>> pair in saved)
                draft[pair.Key] = new Dictionary<string, string>(pair.Value, StringComparer.Ordinal);
        }

        /// <summary>무기 하나의 초안만 되돌린다.</summary>
        public void Revert(string weaponKey)
        {
            if (string.IsNullOrEmpty(weaponKey)) return;

            if (saved.TryGetValue(weaponKey, out Dictionary<string, string> source))
                draft[weaponKey] = new Dictionary<string, string>(source, StringComparer.Ordinal);
            else
                draft.Remove(weaponKey);
        }

        /// <summary>초안에 한 칸을 적는다. 빈 항목 ID 는 "정하지 않음" 으로 지운다.</summary>
        public void Select(string weaponKey, string slotId, string itemId)
        {
            if (string.IsNullOrEmpty(weaponKey) || string.IsNullOrEmpty(slotId)) return;

            if (!draft.TryGetValue(weaponKey, out Dictionary<string, string> slots))
            {
                slots = new Dictionary<string, string>(StringComparer.Ordinal);
                draft[weaponKey] = slots;
            }

            if (string.IsNullOrEmpty(itemId)) slots.Remove(slotId);
            else slots[slotId] = itemId;
        }

        /// <summary>초안의 현재 값. 정하지 않았으면 빈 문자열이다.</summary>
        public string GetSelection(string weaponKey, string slotId) => Read(draft, weaponKey, slotId);

        /// <summary>마지막 저장 값. 정하지 않았으면 빈 문자열이다.</summary>
        public string GetSavedSelection(string weaponKey, string slotId) => Read(saved, weaponKey, slotId);

        /// <summary>
        /// 이 무기의 초안이 저장본과 다른가. <b>저장 버튼을 켜는 유일한 조건</b>이다.
        /// </summary>
        public bool IsDirty(string weaponKey)
        {
            if (string.IsNullOrEmpty(weaponKey)) return false;

            draft.TryGetValue(weaponKey, out Dictionary<string, string> current);
            saved.TryGetValue(weaponKey, out Dictionary<string, string> stored);

            int currentCount = current != null ? current.Count : 0;
            int storedCount = stored != null ? stored.Count : 0;
            if (currentCount != storedCount) return true;
            if (current == null) return false;

            foreach (KeyValuePair<string, string> pair in current)
            {
                if (stored == null) return true;
                if (!stored.TryGetValue(pair.Key, out string value)) return true;
                if (!string.Equals(value, pair.Value, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>어느 무기든 저장하지 않은 편집이 남아 있는가. 닫기 확인에 쓴다.</summary>
        public bool IsAnyDirty()
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> pair in draft)
                if (IsDirty(pair.Key)) return true;
            foreach (KeyValuePair<string, Dictionary<string, string>> pair in saved)
                if (IsDirty(pair.Key)) return true;
            return false;
        }

        /// <summary>
        /// 저장 요청에 실을 프리셋을 만든다. 칸 순서는 언제나 카탈로그 순서다 —
        /// 서버가 받은 순서에 의미를 두지 않아도 되게 하려는 것이다.
        /// </summary>
        public CharacterAppearancePreset BuildPreset(string weaponKey)
        {
            var selections = new List<CharacterAppearanceSelection>(CharacterAppearanceSlotCatalog.All.Count);
            if (!string.IsNullOrEmpty(weaponKey) && draft.TryGetValue(weaponKey, out Dictionary<string, string> slots))
            {
                IReadOnlyList<string> order = CharacterAppearanceSlotCatalog.All;
                for (var i = 0; i < order.Count; i++)
                    if (slots.TryGetValue(order[i], out string itemId) && !string.IsNullOrEmpty(itemId))
                        selections.Add(new CharacterAppearanceSelection(order[i], itemId));
            }

            return new CharacterAppearancePreset(weaponKey ?? string.Empty, selections);
        }

        private static string Read(
            Dictionary<string, Dictionary<string, string>> source, string weaponKey, string slotId)
        {
            if (string.IsNullOrEmpty(weaponKey) || string.IsNullOrEmpty(slotId)) return string.Empty;
            return source.TryGetValue(weaponKey, out Dictionary<string, string> slots)
                   && slots.TryGetValue(slotId, out string itemId)
                ? itemId
                : string.Empty;
        }

        private static void StoreInto(
            Dictionary<string, Dictionary<string, string>> target, CharacterAppearancePreset preset)
        {
            if (string.IsNullOrEmpty(preset.WeaponKey)) return;

            var slots = new Dictionary<string, string>(StringComparer.Ordinal);
            IReadOnlyList<CharacterAppearanceSelection> selections = preset.Selections;
            for (var i = 0; i < selections.Count; i++)
            {
                // 모르는 칸은 버린다. 서버가 나중에 칸을 늘려도 화면이 그리지 못하는 값을
                // 초안에 담고 있다가 저장 때 되돌려 보내면, 있지도 않은 칸이 계속 살아남는다.
                if (!CharacterAppearanceSlotCatalog.IsKnown(selections[i].SlotId)) continue;
                if (string.IsNullOrEmpty(selections[i].ItemId)) continue;
                slots[selections[i].SlotId] = selections[i].ItemId;
            }

            target[preset.WeaponKey] = slots;
        }
    }
}
