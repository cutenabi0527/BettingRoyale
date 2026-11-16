using System;
using System.Collections.Generic;
using System.Text;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Profile;

namespace BettingRoyale.Data.Dto
{
    /// <summary>
    /// 옷장 전송 형식(api.md 8절) ↔ 외형 계약(<c>CharacterAppearanceContract</c>) 변환.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>순수 함수만 둔다.</b> <c>UnityEngine</c> 도 <c>UnityWebRequest</c> 도 모른다(D-060) —
    /// 그래야 <c>revision</c> 해석과 요청 본문 구성을 에디터 테스트가 서버 없이 검증할 수 있다.
    /// REST 호출은 <c>Scripts/Network/Api/WardrobeApi.cs</c> 가 맡는다(D-BE-V4EMNZ).
    /// </para>
    /// <para>
    /// ⚠️ <b>무기 키의 대소문자가 양쪽에서 다르다.</b> 서버 정본 표기는 <c>Sword</c>·<c>Bow</c> 이고
    /// (api.md 8-1), 클라이언트 계약 키는 <c>sword</c>·<c>bow</c> 다
    /// (<c>CharacterAppearanceCatalog.WeaponKey</c>). 이 클래스가 그 사이를 번역하며,
    /// <b>저장 경로에는 조회에서 받은 서버 표기를 그대로 돌려보낸다</b> — 서버가 대소문자를 가리지
    /// 않고 받더라도(<c>WardrobeProperties.canonicalWeaponType</c>) 우리 쪽에서 표기를 지어내지 않는다.
    /// </para>
    /// </remarks>
    public static class WardrobeContractMapper
    {
        /// <summary>한 번도 저장하지 않은 프리셋의 <c>revision</c> (api.md 8-2).</summary>
        public const long UnsavedRevision = 0;

        /// <summary>소유 목록이 실렸는지 판정할 때 찾는 응답 필드 이름.</summary>
        public const string OwnedItemIdsField = "ownedItemIds";

        /// <summary>서버 표기의 무기 유형을 계약 키로 내린다.</summary>
        public static string ToWeaponKey(string serverWeaponType) =>
            string.IsNullOrWhiteSpace(serverWeaponType)
                ? string.Empty
                : serverWeaponType.Trim().ToLowerInvariant();

        /// <summary>
        /// 조회 응답의 <c>weaponTypes</c> 를 계약 키 목록으로 바꾼다. 빈 값·중복은 버린다.
        /// </summary>
        /// <remarks>
        /// 그릴 수 있는 무기만 남기는 것은 여기가 아니라 화면 쪽
        /// <c>CharacterAppearanceCatalog.ResolveWeaponKeys</c> 의 일이다(S-UX-4HAX9Z 1항) —
        /// 어댑터가 미리 걸러 버리면 서버가 무엇을 보냈는지 화면이 알 수 없게 된다.
        /// </remarks>
        public static IReadOnlyList<string> ToWeaponKeys(IReadOnlyList<string> serverWeaponTypes)
        {
            if (serverWeaponTypes == null || serverWeaponTypes.Count == 0) return Array.Empty<string>();

            var keys = new List<string>(serverWeaponTypes.Count);
            for (var i = 0; i < serverWeaponTypes.Count; i++)
            {
                string key = ToWeaponKey(serverWeaponTypes[i]);
                if (key.Length == 0 || keys.Contains(key)) continue;
                keys.Add(key);
            }

            return keys;
        }

        /// <summary>
        /// 저장 경로(<c>PUT .../presets/{weaponType}</c>)에 실을 무기 표기를 고른다.
        /// </summary>
        /// <param name="serverWeaponTypes">가장 최근 조회 응답의 <c>weaponTypes</c>. 없으면 <c>null</c>.</param>
        /// <param name="weaponKey">화면이 보낸 계약 키(<c>sword</c>).</param>
        /// <returns>서버 정본 표기. 아직 조회한 적이 없으면 <paramref name="weaponKey"/> 그대로.</returns>
        public static string ResolveServerWeaponType(IReadOnlyList<string> serverWeaponTypes, string weaponKey)
        {
            string key = ToWeaponKey(weaponKey);
            if (key.Length == 0 || serverWeaponTypes == null) return key;

            for (var i = 0; i < serverWeaponTypes.Count; i++)
                if (string.Equals(ToWeaponKey(serverWeaponTypes[i]), key, StringComparison.Ordinal))
                    return serverWeaponTypes[i];

            return key;
        }

        /// <summary>
        /// 저장된 프리셋인가. <c>revision == 0</c> 은 <b>저장본이 없는 상태</b>다 (api.md 8-2).
        /// </summary>
        /// <remarks>
        /// ⚠️ 이 판정을 "items 가 비었다" 로 대신하면, 모든 칸을 기본으로 고르고 저장한 사용자의
        /// 선택이 매번 <c>appearancePresetSeed</c> 외형으로 되돌아간다. 두 상태는 다르다.
        /// </remarks>
        public static bool HasSavedPreset(WardrobePresetDto dto) => dto != null && dto.revision >= 1;

        /// <summary>
        /// 프리셋 하나를 계약 값으로 바꾼다. <b><c>revision</c> 을 보지 않는다</b> — 저장 응답처럼
        /// "방금 서버가 확정한 내용" 을 그대로 화면에 돌려줄 때 쓴다.
        /// </summary>
        /// <param name="fallbackWeaponKey">응답에 <c>weaponType</c> 이 없을 때 쓸 계약 키.</param>
        public static CharacterAppearancePreset ToPreset(WardrobePresetDto dto, string fallbackWeaponKey)
        {
            string weaponKey = dto != null && !string.IsNullOrWhiteSpace(dto.weaponType)
                ? ToWeaponKey(dto.weaponType)
                : ToWeaponKey(fallbackWeaponKey);

            var selections = new List<CharacterAppearanceSelection>(CharacterAppearanceSlotCatalog.All.Count);
            WardrobeItemsDto items = dto?.items;
            if (items != null)
            {
                IReadOnlyList<string> order = CharacterAppearanceSlotCatalog.All;
                for (var i = 0; i < order.Count; i++)
                {
                    string itemId = items.Get(order[i]);
                    if (string.IsNullOrEmpty(itemId)) continue;
                    selections.Add(new CharacterAppearanceSelection(order[i], itemId));
                }
            }

            return new CharacterAppearancePreset(weaponKey, selections);
        }

        /// <summary>
        /// 조회 응답의 프리셋 목록을 계약 값으로 바꾼다.
        /// <b><c>revision == 0</c> 인 무기는 아예 뺀다</b> — 화면이 seed 외형을 계속 쓰게 하려는 것이다.
        /// </summary>
        public static IReadOnlyList<CharacterAppearancePreset> ToPresets(IReadOnlyList<WardrobePresetDto> dtos)
        {
            if (dtos == null || dtos.Count == 0) return Array.Empty<CharacterAppearancePreset>();

            var presets = new List<CharacterAppearancePreset>(dtos.Count);
            for (var i = 0; i < dtos.Count; i++)
            {
                if (!HasSavedPreset(dtos[i])) continue;

                CharacterAppearancePreset preset = ToPreset(dtos[i], string.Empty);
                if (string.IsNullOrEmpty(preset.WeaponKey)) continue;
                presets.Add(preset);
            }

            return presets;
        }

        /// <summary>
        /// 저장 요청 본문을 만든다 — <c>{"items":{"body":"Body_11", ...}}</c> (api.md 8-4).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><c>JsonUtility</c> 로 만들 수 없다.</b> 11칸 고정 필드를 직렬화하면 고르지 않은 칸이
        /// <c>"glasses":""</c> 로 실려 나가고, 빈 문자열은 카탈로그에 없는 ID 라
        /// <b>저장 전체가 400 VALIDATION_ERROR</b> 가 된다. 고른 칸만 싣는다.
        /// </para>
        /// <para>
        /// 전체 교체(PUT)이므로 <b>이 무기의 모든 칸</b>이 들어 있어야 한다 — 화면이
        /// 초안 전체를 담아 보내는 것이 계약 단위다(S-UX-Q4FKHA).
        /// 다른 무기 유형은 경로에 실리지 않으므로 덮어쓸 자리가 없다(S-GD-5NRMCY 3항).
        /// </para>
        /// <para>
        /// 모르는 슬롯은 버린다 — 서버가 400 을 주기 전에 우리가 만들지 않는다.
        /// </para>
        /// </remarks>
        public static string BuildPresetRequestJson(CharacterAppearancePreset preset)
        {
            var builder = new StringBuilder(256);
            builder.Append("{\"items\":{");

            IReadOnlyList<string> order = CharacterAppearanceSlotCatalog.All;
            var written = 0;
            for (var i = 0; i < order.Count; i++)
            {
                if (!preset.TryGetItemId(order[i], out string itemId)) continue;
                if (string.IsNullOrEmpty(itemId)) continue;
                // UI's empty-slot sentinel is not a server catalog item. PUT omission clears the slot.
                if (string.Equals(itemId, CharacterAppearanceCatalog.DefaultItemId, StringComparison.Ordinal)) continue;

                if (written > 0) builder.Append(',');
                AppendJsonString(builder, order[i]);
                builder.Append(':');
                AppendJsonString(builder, itemId);
                written++;
            }

            builder.Append("}}");
            return builder.ToString();
        }

        /// <summary>
        /// 응답에 소유 목록 필드가 실려 있었는가.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>파싱 결과가 빈 배열인 것으로는 판정할 수 없다.</b> 아무것도 소유하지 않은 계정과
        /// 소유를 판정하지 못한 구버전 서버가 같은 값이 되고, 화면은 둘 다 "전부 잠김" 으로 그려
        /// 고장처럼 보인다(S-UX-4HAX9Z 6항, 계약의 <c>HasOwnershipData</c> 주석).
        /// 그래서 <b>원문에 키가 있었는지</b>를 본다.
        /// <para>
        /// 문자열을 훑는 것으로 충분하다 — 이 응답에 <c>ownedItemIds</c> 와 같은 이름의 값이
        /// 들어올 자리가 없다(항목 ID 는 영숫자·밑줄뿐이다, api.md 8-1).
        /// </para>
        /// </remarks>
        public static bool ContainsOwnershipField(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;

            string key = "\"" + OwnedItemIdsField + "\"";
            int at = json.IndexOf(key, StringComparison.Ordinal);
            while (at >= 0)
            {
                int cursor = at + key.Length;
                while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
                if (cursor < json.Length && json[cursor] == ':') return true;
                at = json.IndexOf(key, at + key.Length, StringComparison.Ordinal);
            }

            return false;
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }

            builder.Append('"');
        }
    }
}
