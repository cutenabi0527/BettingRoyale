using BlessingWeaponTag = BettingRoyale.Data.Blessing.WeaponTag;
using CombatWeaponType = BettingRoyale.Data.Combat.WeaponType;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 축복 태그(<c>Data.Blessing.WeaponTag</c>)를 강화 표의 키(<c>Data.Combat.WeaponType</c>)로 옮긴다.
    ///
    /// <para>
    /// <b>둘은 서로 다른 질문에 답하는 별개 열거형이다</b>(<c>WeaponTag</c> 주석, O-021 은 철회됐다).
    /// 다만 무기별 태그는 <c>WeaponType</c> 의 같은 이름에 대응한다고 그 주석이 못박고 있고,
    /// <c>Scripts/Editor/BlessingWeaponTagCatalogTests.cs</c> 가 그 쌍을 검사한다. 이 함수는 그
    /// 대응만 옮긴다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 이것은 임시 보조 경로다.</b> 강화 표를 고를 진짜 근거는 <b>실제 전투원이 들고 있는 무기</b>
    /// 이고(<c>ICombatActionDriver.EquippedWeapon</c>), 어댑터가 그것을
    /// <c>MatchEconomyState.TrySetEquippedWeaponType</c> 로 넣는다. 이 변환은 전투원이 아직 없을 때의
    /// 출발값에만 쓴다 — <b>스폰 전에는 무기를 물어볼 대상이 없다.</b>
    /// </para>
    ///
    /// <para>
    /// 리플렉션을 쓰지 않는다 — <c>Enum.TryParse</c> 는 IL2CPP 스트리핑에 취약하고
    /// 분류 태그(<c>Melee</c>·<c>Ranged</c>)처럼 <b>무기가 아닌 값</b>에 이름이 없다고 해서
    /// 거부해 주지도 않는다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class UpgradeWeaponResolver
    {
        /// <summary>
        /// 무기별 태그를 무기 종류로 옮긴다.
        /// <b>공용·분류 태그는 무기가 아니므로 <see cref="CombatWeaponType.None"/></b> 이다 —
        /// 그때는 무기 강화 표가 없어 <see cref="UpgradeAttemptResult.NoTrack"/> 이 된다.
        /// </summary>
        public static CombatWeaponType ToWeaponType(BlessingWeaponTag tag)
        {
            switch (tag)
            {
                case BlessingWeaponTag.Sword:
                    return CombatWeaponType.Sword;

                case BlessingWeaponTag.Bow:
                    return CombatWeaponType.Bow;

                case BlessingWeaponTag.Mace:
                    return CombatWeaponType.Mace;

                default:
                    // Common·Melee·Ranged — 무기 하나를 지목하지 않는 태그다.
                    return CombatWeaponType.None;
            }
        }
    }
}
