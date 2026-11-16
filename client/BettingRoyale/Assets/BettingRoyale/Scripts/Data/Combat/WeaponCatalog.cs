namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 무기 종류 → 태그의 불변 매핑. Network(추후 어댑터)와 Gameplay 가 함께 참조하는 공유 계약 데이터다 (D-044).
    /// 수치(피해량·사거리 등)는 여기 없다 — <see cref="WeaponConfig"/> 로 호출자가 공급한다 (S-GD-7DRYPY).
    /// </summary>
    public static class WeaponCatalog
    {
        /// <summary>
        /// 무기의 태그를 조회한다. 미구현 무기(Mace)는 false 를 반환한다 —
        /// 예외를 흐름 제어로 쓰지 않는 TryGet 패턴이다(coding-convention 5절).
        /// </summary>
        public static bool TryGetTags(WeaponType type, out WeaponTag tags)
        {
            switch (type)
            {
                case WeaponType.Sword:
                    tags = WeaponTag.Sword | WeaponTag.Melee;
                    return true;
                case WeaponType.Bow:
                    tags = WeaponTag.Bow | WeaponTag.Ranged | WeaponTag.Projectile;
                    return true;
                default: // WeaponType.Mace — 예약만 되어 있고 구현이 없다 (S-GD-7DRYPY)
                    tags = WeaponTag.None;
                    return false;
            }
        }
    }
}
