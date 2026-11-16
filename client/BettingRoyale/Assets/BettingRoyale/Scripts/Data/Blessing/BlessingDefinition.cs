using System;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 한 종류의 정의 (S-GD-S46S28, S-GD-SB84DQ).
    ///
    /// 효과 계수는 BlessingEffectCatalog가 소유하고, 설명은 콘텐츠 공급자가 채운다.
    /// 현재 목업 카탈로그는 같은 효과 행에서 수치 설명을 생성한다 (S-UX-Z78WCK).
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class BlessingDefinition : IEquatable<BlessingDefinition>
    {
        public BlessingDefinition(
            string id, string displayName, WeaponTag tag, BlessingTier tier, string description = null,
            bool isActive = false)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("축복 id 는 비어 있을 수 없습니다.", nameof(id));
            }

            if (tier < BlessingTier.Silver || tier > BlessingTier.Platinum)
            {
                // default(BlessingTier) 가 0 이라 등급을 빠뜨리면 여기서 잡힌다 (S-GD-EQCVWS).
                throw new ArgumentOutOfRangeException(
                    nameof(tier), tier, "축복 등급은 실버·골드·플래티넘 중 하나여야 합니다.");
            }

            if (!BlessingWeaponTagCatalog.IsDefined(tag))
            {
                // 외부 데이터(D-041)에서 복원한 값이 여기로 들어온다. 캐스팅은 검사 없이 통과하므로
                // (WeaponTag)99 같은 값이 그대로 살아남아 "아무 무기와도 호환되지 않는 축복" 이 된다 —
                // 예외도 로그도 없이 후보에서만 사라진다. 경계에서 막는다.
                throw new ArgumentOutOfRangeException(
                    nameof(tag), tag, "정의되지 않은 무기 태그입니다 (BlessingWeaponTagCatalog.All 참조).");
            }

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Tag = tag;
            Tier = tier;
            Description = description ?? string.Empty;
            IsActive = isActive;
        }

        /// <summary>
        /// 플레이어가 <b>직접 눌러서 쓰는</b> 축복인가(키보드 Q · 게임패드 Y — S-GD-NJQ5NM).
        /// 엑셀 <c>duration_kind</c> 가 <c>Active</c> 인 것이다. <b>한 번에 하나만 가진다</b> — 뽑기가 이 값으로 거른다.
        /// </summary>
        public bool IsActive { get; }

        /// <summary>중복 없는 식별자. 네트워크로 오갈 때 이 값만 보낸다.</summary>
        public string Id { get; }

        /// <summary>UI 표시용 이름.</summary>
        public string DisplayName { get; }

        /// <summary>
        /// 카드에 표시할 설명문. <c>Scripts/UI/BlessingCardView.cs</c> 가 이 자리를 그리고 있다.
        ///
        /// 공급자가 설명을 주지 않은 외부/테스트 정의의 기본값은 빈 문자열이다.
        /// 실제 Local/Online 목업 카탈로그는 효과 수치가 포함된 설명을 제공한다.
        /// </summary>
        public string Description { get; }

        /// <summary>호환 무기 태그 (S-GD-SB84DQ).</summary>
        public WeaponTag Tag { get; }

        /// <summary>
        /// 등급 (S-GD-EQCVWS). 실버 &lt; 골드 &lt; 플래티넘 순서로 비교할 수 있다.
        ///
        /// <b>이 값이 후보에 뜰 라운드를 정한다</b> — 등급은 뽑히는 것이 아니라 라운드로 정해지므로
        /// (S-GD-HE87JY) 실버 축복은 1라운드에만, 플래티넘은 3라운드에만 나온다.
        /// 효과 크기는 별도 효과 공급자가 등급별 id로 제공한다.
        /// </summary>
        public BlessingTier Tier { get; }

        /// <summary>
        /// 이 축복이 해당 무기와 함께 뽑힐 수 있는가.
        ///
        /// <b>판정은 <see cref="BlessingWeaponTagCatalog.Accepts"/> 한 곳에만 있다.</b>
        /// 여기서 다시 구현하면 상점·보상 등 다른 호출처와 규칙이 갈린다.
        /// </summary>
        /// <param name="equippedPrimaryWeapon">
        /// 플레이어가 <b>실제로 든 무기</b>의 태그. <see cref="WeaponTag.Common"/> 을 넘기면 안 된다 —
        /// 그건 축복이 갖는 값이지 장착 가능한 무기가 아니다. 넘기면 공용 축복만 통과해
        /// <b>후보가 조용히 좁아진다.</b>
        /// </param>
        public bool IsCompatibleWith(WeaponTag equippedPrimaryWeapon) =>
            BlessingWeaponTagCatalog.Accepts(Tag, equippedPrimaryWeapon);

        public bool Equals(BlessingDefinition other) =>
            other != null && string.Equals(Id, other.Id, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as BlessingDefinition);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() => $"{DisplayName}({Tier}/{Tag})";
    }
}
