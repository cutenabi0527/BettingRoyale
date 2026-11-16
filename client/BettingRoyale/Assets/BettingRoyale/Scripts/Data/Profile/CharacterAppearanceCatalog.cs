using System;
using System.Collections.Generic;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Online.Lobby;

namespace BettingRoyale.Data.Profile
{
    /// <summary>
    /// 문자열 항목 ID ↔ 런타임 선택기 인덱스 매핑
    /// ([S-UX-Q4FKHA](../../../../../../docs/specs/ui-ux/S-UX-Q4FKHA.md),
    /// [S-UX-F3CJEP](../../../../../../docs/specs/ui-ux/S-UX-F3CJEP.md)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 래퍼가 필요한가.</b> <c>CharacterVisualSelector</c> 는 슬롯마다 0부터 세는 인덱스로
    /// 돈다. 그 인덱스를 계약·서버에 내보내면 아트가 의상 하나를 목록 가운데에 끼워 넣는 순간
    /// <b>저장된 모든 계정의 외형이 한 칸씩 밀린다</b> — 초상화를 문자열 ID 로 저장하는 이유와
    /// 똑같다(S-UX-RQ5N9E). 그래서 인덱스는 이 클래스 안에서만 살고 밖으로는 ID 만 나간다.
    /// </para>
    /// <para>
    /// 이 클래스는 <c>UnityEngine</c> 을 모른다(D-060). 실제 카탈로그를 읽어 이 목록을 만드는 일은
    /// <c>Gameplay</c> 의 미리보기 리그가 한다.
    /// </para>
    /// </remarks>
    public sealed class CharacterAppearanceCatalog
    {
        /// <summary>의상 칸의 0번. "원본 파츠를 그대로 둔다" 는 뜻이다.</summary>
        public const string DefaultItemId = "default";

        /// <summary>
        /// 편집할 수 있는 무기 유형. <b>대기방에서 실제로 고를 수 있는 것만</b> 넣는다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>S-UX-Q4FKHA 는 "무기 유형 전환" 을 요구하지만 고를 수 있는 무기는 둘뿐이다.</b>
        /// <see cref="WeaponType.Mace"/> 는 <c>WeaponCatalog</c> 에 태그가 없는 예약 확장 지점이고
        /// <see cref="LobbyWeaponChoice"/> 에도 없다 — 탭을 그리면 <b>살 수 없는 물건을 진열하는</b>
        /// 것과 같은 실수가 된다(D-CLI-R69V68 과 같은 함정이다).
        /// </para>
        /// <para>
        /// 그래서 목록을 손으로 적지 않고 <see cref="LobbyWeaponChoice"/> 중 이미 무기가 정해진
        /// 것만 뽑는다. 새 무기가 대기방 선택지에 들어오는 날 이 목록도 함께 늘어난다.
        /// </para>
        /// </remarks>
        public static readonly IReadOnlyList<string> EditableWeaponKeys = BuildEditableWeaponKeys();

        private readonly Dictionary<string, IReadOnlyList<string>> optionsBySlot;

        /// <param name="optionsBySlot">
        /// 슬롯 ID → 항목 ID 목록. <b>목록의 자리가 곧 선택기 인덱스</b>이므로 순서를 바꾸지 않는다.
        /// </param>
        public CharacterAppearanceCatalog(IReadOnlyDictionary<string, IReadOnlyList<string>> optionsBySlot)
        {
            this.optionsBySlot = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (optionsBySlot == null) return;

            foreach (KeyValuePair<string, IReadOnlyList<string>> pair in optionsBySlot)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null) continue;
                this.optionsBySlot[pair.Key] = pair.Value;
            }
        }

        /// <summary>선택기에서 읽어 온 칸이 하나도 없다. 카탈로그가 비어 있으면 편집할 수 없다.</summary>
        public bool IsEmpty => optionsBySlot.Count == 0;

        public IReadOnlyList<string> GetOptions(string slotId) =>
            slotId != null && optionsBySlot.TryGetValue(slotId, out IReadOnlyList<string> options)
                ? options
                : Array.Empty<string>();

        /// <summary>선택기에 넣을 인덱스. 모르는 항목이면 <c>-1</c> 이다.</summary>
        public int IndexOf(string slotId, string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;
            IReadOnlyList<string> options = GetOptions(slotId);
            for (var i = 0; i < options.Count; i++)
                if (string.Equals(options[i], itemId, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>인덱스에 해당하는 항목 ID. 범위 밖이면 빈 문자열이다.</summary>
        public string ItemIdAt(string slotId, int index)
        {
            IReadOnlyList<string> options = GetOptions(slotId);
            return index >= 0 && index < options.Count ? options[index] : string.Empty;
        }

        public bool IsKnown(string slotId, string itemId) => IndexOf(slotId, itemId) >= 0;

        /// <summary>
        /// 실제로 고를 수 있는 항목만 추린다 (S-UX-F3CJEP — 소유한 의상만 고를 수 있다).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <paramref name="hasOwnershipData"/> 가 <c>false</c> 면 <b>빈 목록</b>을 준다. 소유를 모르는
        /// 상태에서 전부 열어 주면 스펙 위반이고, 전부 닫으면 화면이 고장처럼 보인다 — 그래서
        /// 화면이 "아직 받지 못했다" 를 <b>글로</b> 알리도록 여기서는 비워서 돌려준다.
        /// </para>
        /// <para>
        /// <see cref="DefaultItemId"/> 는 소유 목록에 없어도 항상 고를 수 있다. "아무것도 안 입은
        /// 상태" 를 사는 것이 아니기 때문이며, 이게 없으면 <b>한 번 입은 모자를 벗을 수 없다</b>.
        /// </para>
        /// </remarks>
        public IReadOnlyList<string> GetSelectableOptions(
            string slotId, ICollection<string> ownedItemIds, bool hasOwnershipData)
        {
            if (!hasOwnershipData) return Array.Empty<string>();

            IReadOnlyList<string> options = GetOptions(slotId);
            var selectable = new List<string>(options.Count);
            for (var i = 0; i < options.Count; i++)
            {
                bool always = string.Equals(options[i], DefaultItemId, StringComparison.Ordinal);
                if (always || (ownedItemIds != null && ownedItemIds.Contains(options[i])))
                    selectable.Add(options[i]);
            }

            return selectable;
        }

        /// <summary>
        /// 고를 수 있는 것들 안에서 한 칸 옮긴다. 목록 끝에서는 반대쪽으로 돈다.
        /// </summary>
        /// <remarks>
        /// 현재 값이 목록에 없으면(미보유를 입고 있는 계정) <b>첫 칸으로 떨어뜨린다</b> — 그래야
        /// 화살표를 눌렀을 때 아무 일도 안 일어나는 상태에 갇히지 않는다.
        /// </remarks>
        public static string Step(IReadOnlyList<string> selectable, string currentItemId, int direction)
        {
            if (selectable == null || selectable.Count == 0) return currentItemId ?? string.Empty;

            var current = -1;
            for (var i = 0; i < selectable.Count; i++)
                if (string.Equals(selectable[i], currentItemId, StringComparison.Ordinal)) { current = i; break; }

            if (current < 0) return selectable[0];
            if (direction == 0) return selectable[current];

            int next = current + (direction > 0 ? 1 : -1);
            if (next < 0) next = selectable.Count - 1;
            else if (next >= selectable.Count) next = 0;
            return selectable[next];
        }

        /// <summary>서버·계약에 쓰는 무기 키. 열거형 숫자를 내보내지 않는다.</summary>
        public static string WeaponKey(WeaponType weapon)
        {
            switch (weapon)
            {
                case WeaponType.Sword: return "sword";
                case WeaponType.Bow: return "bow";
                case WeaponType.Mace: return "mace";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 무기 키를 전투 무기로 되돌린다. <b>편집할 수 없는 무기는 <c>false</c></b> 다 —
        /// 구현이 없는 <see cref="WeaponType.Mace"/> 가 조용히 들어오지 않게 막는 관문이다.
        /// </summary>
        public static bool TryGetWeaponType(string weaponKey, out WeaponType weapon)
        {
            if (string.Equals(weaponKey, "sword", StringComparison.Ordinal)) { weapon = WeaponType.Sword; return true; }
            if (string.Equals(weaponKey, "bow", StringComparison.Ordinal)) { weapon = WeaponType.Bow; return true; }
            weapon = WeaponType.None;
            return false;
        }

        /// <summary>편집 화면이 탭으로 그려도 되는 무기인가.</summary>
        public static bool IsEditableWeaponKey(string weaponKey)
        {
            for (var i = 0; i < EditableWeaponKeys.Count; i++)
                if (string.Equals(EditableWeaponKeys[i], weaponKey, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// 어댑터가 준 목록에서 <b>우리가 실제로 그릴 수 있는 무기만</b> 남긴다.
        /// 남는 것이 없으면 기본 목록으로 떨어뜨린다 — 빈 탭 줄을 만들지 않는다(S-UX-RQ5N9E).
        /// </summary>
        public static IReadOnlyList<string> ResolveWeaponKeys(IReadOnlyList<string> requested)
        {
            if (requested == null || requested.Count == 0) return EditableWeaponKeys;

            var resolved = new List<string>(requested.Count);
            for (var i = 0; i < requested.Count; i++)
                if (IsEditableWeaponKey(requested[i]) && !resolved.Contains(requested[i]))
                    resolved.Add(requested[i]);

            return resolved.Count > 0 ? resolved : EditableWeaponKeys;
        }

        private static IReadOnlyList<string> BuildEditableWeaponKeys()
        {
            var keys = new List<string>(2);
            foreach (LobbyWeaponChoice choice in (LobbyWeaponChoice[])Enum.GetValues(typeof(LobbyWeaponChoice)))
            {
                if (!LobbyWeaponChoiceRules.IsDecided(choice)) continue;
                if (!LobbyWeaponChoiceRules.TryGetWeaponType(choice, out WeaponType weapon)) continue;

                string key = WeaponKey(weapon);
                if (!string.IsNullOrEmpty(key) && !keys.Contains(key)) keys.Add(key);
            }

            return keys.AsReadOnly();
        }
    }
}
