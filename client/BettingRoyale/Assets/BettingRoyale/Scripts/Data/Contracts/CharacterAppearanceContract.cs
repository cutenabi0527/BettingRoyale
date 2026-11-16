using System;
using System.Collections.Generic;
using BettingRoyale.Core;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// 인게임 외형("옷장") 한 칸의 선택
    /// ([S-UX-Q4FKHA](../../../../../../docs/specs/ui-ux/S-UX-Q4FKHA.md)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>슬롯도 항목도 문자열 ID 다. 순번이 아니다.</b> 카탈로그 순서는 아트가 하나를 끼워 넣기만
    /// 해도 바뀌는데, 순번을 저장해 두면 <b>그 순간 전 계정의 외형이 한 칸씩 밀린다</b> — 초상화에서
    /// 같은 이유로 문자열 ID 를 쓴다(S-UX-RQ5N9E). 런타임 선택기가 인덱스로 도는 것은 사실이지만
    /// 그 변환은 클라이언트 안에서만 일어나고 계약을 넘지 않는다.
    /// </para>
    /// </remarks>
    public readonly struct CharacterAppearanceSelection
    {
        public CharacterAppearanceSelection(string slotId, string itemId)
        {
            SlotId = slotId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
        }

        /// <summary><c>body</c>·<c>face</c>·<c>hat</c> 처럼 슬롯을 가리키는 고정 ID.</summary>
        public string SlotId { get; }

        /// <summary>그 슬롯에 넣은 항목의 ID. 비워 두면 "정하지 않음" 이다.</summary>
        public string ItemId { get; }
    }

    /// <summary>무기 유형 하나에 저장하는 외형 프리셋 (S-GD-5NRMCY).</summary>
    /// <remarks>
    /// 무기 유형마다 따로 저장한다 — 한쪽을 바꿔도 다른 쪽을 덮어쓰지 않는 것이 스펙의 요구다.
    /// <see cref="WeaponKey"/> 역시 열거형 숫자가 아니라 문자열(<c>sword</c>·<c>bow</c>)이다.
    /// </remarks>
    public readonly struct CharacterAppearancePreset
    {
        private readonly IReadOnlyList<CharacterAppearanceSelection> selections;

        public CharacterAppearancePreset(string weaponKey, IReadOnlyList<CharacterAppearanceSelection> selections)
        {
            WeaponKey = weaponKey ?? string.Empty;
            this.selections = selections;
        }

        public string WeaponKey { get; }

        public IReadOnlyList<CharacterAppearanceSelection> Selections =>
            selections ?? Array.Empty<CharacterAppearanceSelection>();

        /// <summary>슬롯 하나의 선택을 꺼낸다. 프리셋에 없으면 <c>false</c> 다.</summary>
        public bool TryGetItemId(string slotId, out string itemId)
        {
            IReadOnlyList<CharacterAppearanceSelection> list = Selections;
            for (var i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].SlotId, slotId, StringComparison.Ordinal)) continue;
                itemId = list[i].ItemId;
                return true;
            }

            itemId = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// 화면 → 어댑터. 저장된 외형 프리셋과 <b>소유 목록</b>을 달라는 요청 (S-UX-F3CJEP).
    /// </summary>
    /// <remarks>
    /// 화면은 이 명령만 낸다. REST 경로·토큰·DTO 는 어댑터의 몫이다(D-065).
    /// ⚠️ <b>받는 쪽이 아직 없다.</b> 서버 계약은 별도 작업으로 붙으며, 그때까지 화면은 응답 없음
    /// 상태를 글로 알린다 — 전부 소유했다고 가정하지 않는다.
    /// </remarks>
    public readonly struct CharacterAppearanceRequested : IGameEvent
    {
    }

    /// <summary>어댑터 → 화면. 조회 결과. 성공·실패를 한 이벤트로 받는다.</summary>
    /// <remarks>
    /// <para>
    /// 실패도 같은 이벤트인 이유는 초상화와 같다 — <b>로딩을 끝낼 자리가 한 곳</b>이어야 화면이
    /// 영원히 "불러오는 중" 에 머무르지 않는다.
    /// </para>
    /// <para>
    /// <b><see cref="HasOwnershipData"/> 가 소유 목록이 비어 있는 것과 다르다.</b> 아무것도 소유하지
    /// 않은 계정과 소유를 아직 모르는 상태를 구분하지 못하면, 화면은 둘 다 "전부 잠김" 으로 그려
    /// 고장처럼 보인다. 서버가 소유를 판정했다는 사실 자체를 별도 bool 로 받는다.
    /// </para>
    /// </remarks>
    public readonly struct CharacterAppearanceSnapshotChanged : IGameEvent
    {
        private readonly IReadOnlyList<string> weaponKeys;
        private readonly IReadOnlyList<string> ownedItemIds;
        private readonly IReadOnlyList<CharacterAppearancePreset> presets;

        private CharacterAppearanceSnapshotChanged(
            bool success,
            IReadOnlyList<string> weaponKeys,
            IReadOnlyList<string> ownedItemIds,
            bool hasOwnershipData,
            IReadOnlyList<CharacterAppearancePreset> presets,
            string failureMessage)
        {
            Success = success;
            this.weaponKeys = weaponKeys;
            this.ownedItemIds = ownedItemIds;
            HasOwnershipData = hasOwnershipData;
            this.presets = presets;
            FailureMessage = failureMessage ?? string.Empty;
        }

        public bool Success { get; }

        /// <summary>
        /// 편집할 수 있는 무기 유형. <b>비어 있으면 화면이 자기 기본값을 쓴다.</b>
        /// </summary>
        /// <remarks>
        /// ⚠️ 서버가 구현되지 않은 무기를 넣지 않는다. 현재 고를 수 있는 무기는 검·활 둘뿐이며
        /// <c>Mace</c> 는 구현이 없다 — 탭을 그리면 고를 수 없는 것을 진열하게 된다.
        /// </remarks>
        public IReadOnlyList<string> WeaponKeys => weaponKeys ?? Array.Empty<string>();

        /// <summary>계정이 소유한 항목 ID 전체. 슬롯 구분 없이 한 집합이다.</summary>
        public IReadOnlyList<string> OwnedItemIds => ownedItemIds ?? Array.Empty<string>();

        /// <summary>서버가 소유 목록을 실제로 내려줬는가. <c>false</c> 면 편집을 열지 않는다.</summary>
        public bool HasOwnershipData { get; }

        /// <summary>무기 유형별 저장된 프리셋. 없는 무기는 빠져 있을 수 있다.</summary>
        public IReadOnlyList<CharacterAppearancePreset> Presets =>
            presets ?? Array.Empty<CharacterAppearancePreset>();

        /// <summary>사람이 읽을 실패 사유. 성공이면 빈 문자열이다.</summary>
        public string FailureMessage { get; }

        public static CharacterAppearanceSnapshotChanged Loaded(
            IReadOnlyList<string> weaponKeys,
            IReadOnlyList<string> ownedItemIds,
            IReadOnlyList<CharacterAppearancePreset> presets) =>
            new CharacterAppearanceSnapshotChanged(
                true, weaponKeys, ownedItemIds, true, presets, string.Empty);

        /// <summary>
        /// 조회는 됐지만 <b>소유 목록이 없다</b>. 구버전 서버이거나 소유 표가 아직 없다.
        /// </summary>
        public static CharacterAppearanceSnapshotChanged LoadedWithoutOwnership(
            IReadOnlyList<string> weaponKeys,
            IReadOnlyList<CharacterAppearancePreset> presets) =>
            new CharacterAppearanceSnapshotChanged(
                true, weaponKeys, Array.Empty<string>(), false, presets, string.Empty);

        public static CharacterAppearanceSnapshotChanged Failed(string failureMessage) =>
            new CharacterAppearanceSnapshotChanged(
                false, Array.Empty<string>(), Array.Empty<string>(), false,
                Array.Empty<CharacterAppearancePreset>(), failureMessage);
    }

    /// <summary>화면 → 어댑터. 한 무기 유형의 외형을 저장해 달라는 요청.</summary>
    /// <remarks>
    /// <b>한 번에 한 무기만 보낸다.</b> 편집한 무기만 보내면 다른 무기의 프리셋을 덮어쓸 자리가
    /// 아예 없어진다 (S-GD-5NRMCY 3항).
    /// </remarks>
    public readonly struct CharacterAppearanceSaveRequested : IGameEvent
    {
        public CharacterAppearanceSaveRequested(CharacterAppearancePreset preset)
        {
            Preset = preset;
        }

        public CharacterAppearancePreset Preset { get; }
    }

    /// <summary>어댑터 → 화면. 저장 결과.</summary>
    /// <remarks>
    /// 실패면 <see cref="Preset"/> 은 <b>바뀌기 전 저장 값</b>이다 — 화면이 초안을 어디로 되돌릴지
    /// 알 수 있어야 하기 때문이다. 소유 검증은 서버가 하므로 화면은 응답 전에 확정하지 않는다.
    /// </remarks>
    public readonly struct CharacterAppearanceSaved : IGameEvent
    {
        private CharacterAppearanceSaved(bool success, CharacterAppearancePreset preset, string failureMessage)
        {
            Success = success;
            Preset = preset;
            FailureMessage = failureMessage ?? string.Empty;
        }

        public bool Success { get; }

        public CharacterAppearancePreset Preset { get; }

        public string FailureMessage { get; }

        public static CharacterAppearanceSaved Saved(CharacterAppearancePreset preset) =>
            new CharacterAppearanceSaved(true, preset, string.Empty);

        public static CharacterAppearanceSaved Failed(CharacterAppearancePreset currentPreset, string failureMessage) =>
            new CharacterAppearanceSaved(false, currentPreset, failureMessage);
    }

    /// <summary>
    /// 잠긴 외형 의상을 메달로 사 달라는 요청
    /// ([S-GD-5R953J](../../../../../../docs/specs/game-design/S-GD-5R953J.md)).
    /// </summary>
    /// <remarks>
    /// 상점이 생기기 전까지는 잠긴 칸에서 바로 산다. 화면은 요청만 내고 잔액 확인·차감은
    /// 서버가 한다 — UI 가 서버 DTO 를 직접 쓰지 않는다(D-065).
    /// </remarks>
    public readonly struct WardrobeItemPurchaseRequested : IGameEvent
    {
        public WardrobeItemPurchaseRequested(string itemId)
        {
            ItemId = itemId ?? string.Empty;
        }

        public string ItemId { get; }
    }

    /// <summary>구매 결과. 성공·실패를 한 이벤트로 받아 화면이 로딩을 한 곳에서 끝낸다.</summary>
    public readonly struct WardrobeItemPurchased : IGameEvent
    {
        private readonly IReadOnlyList<string> owned;

        private WardrobeItemPurchased(
            bool success, string itemId, long medalBalance,
            IReadOnlyList<string> ownedItemIds, string failureMessage)
        {
            Success = success;
            ItemId = itemId ?? string.Empty;
            MedalBalance = medalBalance;
            owned = ownedItemIds;
            FailureMessage = failureMessage ?? string.Empty;
        }

        public bool Success { get; }

        public string ItemId { get; }

        /// <summary>구매 뒤 남은 잔액. 실패면 의미 없다.</summary>
        public long MedalBalance { get; }

        /// <summary>갱신된 소유 목록. 화면이 옷장을 다시 조회하지 않게 한다.</summary>
        public IReadOnlyList<string> OwnedItemIds => owned ?? Array.Empty<string>();

        public string FailureMessage { get; }

        public static WardrobeItemPurchased Bought(
            string itemId, long medalBalance, IReadOnlyList<string> ownedItemIds) =>
            new(true, itemId, medalBalance, ownedItemIds, string.Empty);

        public static WardrobeItemPurchased Failed(string itemId, string failureMessage) =>
            new(false, itemId, 0, Array.Empty<string>(), failureMessage);
    }
}
