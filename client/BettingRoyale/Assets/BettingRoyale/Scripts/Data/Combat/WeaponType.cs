namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 무기 종류. 계약 v0.1 의 확정 범위는 Sword / Bow 두 가지다 (S-GD-7DRYPY).
    /// </summary>
    /// <remarks>
    /// 이 값은 네트워크 복제·저장 경계를 넘을 수 있으므로 기존 멤버의 숫자 값을 바꾸지 않는다.
    /// 새 무기는 뒤에 추가만 한다.
    /// </remarks>
    public enum WeaponType
    {
        /// <summary>무기 슬롯이 빈 비무장 상태. 기존 구현 무기의 숫자값은 유지한다.</summary>
        None = -1,
        Sword = 0,
        Bow = 1,

        /// <summary>
        /// ⚠️ 예약된 확장 지점 — 구현이 없다 (S-GD-7DRYPY).
        /// <see cref="WeaponCatalog.TryGetTags"/> 가 false 를 반환하므로 장착·스폰 경로에 넣으면 안 된다.
        /// 구현할 때 WeaponCatalog 에 태그를 등록하고 이 주석을 갱신한다.
        /// </summary>
        Mace = 2,
    }
}
