using System;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 무기 식별·특성 태그. 공용·검 전용·활 전용 축복 후보 필터링의 조건으로 쓴다 (S-GD-7DRYPY).
    /// 무기 하나가 여러 태그를 가질 수 있어 [Flags] 로 정의한다.
    /// </summary>
    [Flags]
    public enum WeaponTag
    {
        None = 0,
        Melee = 1 << 0,
        Ranged = 1 << 1,

        /// <summary>투사체를 발사한다. 명중이 즉발이 아니라 투사체 도달 시점에 판정된다.</summary>
        Projectile = 1 << 2,

        Sword = 1 << 3,
        Bow = 1 << 4,

        /// <summary>예약된 확장 태그. Mace 구현 전에는 어떤 무기에도 부여하지 않는다.</summary>
        Mace = 1 << 5,
    }
}
