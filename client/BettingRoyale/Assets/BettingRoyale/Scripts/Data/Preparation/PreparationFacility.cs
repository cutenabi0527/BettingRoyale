namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// 준비 콜로세움의 기능 구역 (S-GD-66GTWA).
    ///
    /// <para>
    /// 세 곳 모두 <b>다가가서 쓰는 장소</b>다 — 화면 어딘가의 버튼이 아니다
    /// ([S-GD-7RCHF8] 이 배치와 시점을 정했다). 그래서 "지금 어느 구역 안에 있는가" 가
    /// 상호작용이 열리는 조건이 된다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 값은 뒤에만 붙인다.</b> 이 열거형은 계약(<c>PreparationFacilityInRangeChanged</c>)에
    /// 실려 회선을 넘는다. 중간에 끼우면 배포된 값의 뜻이 밀린다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public enum PreparationFacility
    {
        /// <summary>
        /// 어느 구역에도 있지 않다.
        ///
        /// <b>이 값이 열린 화면을 닫는 신호다</b> — 상점·강화·룰렛 창을 열어둔 채 걸어 나가면
        /// "구역 안에서만 쓴다" 는 규칙이 무의미해진다 (조영운 확정, 2026-09-08).
        /// </summary>
        None = 0,

        /// <summary>상점 — 소모품·장신구 구매 (S-GD-PVVHP9).</summary>
        Shop = 1,

        /// <summary>모루 — 무기 강화. 도메인은 아직 없다.</summary>
        Upgrade = 2,

        /// <summary>룰렛 — 골드 베팅 (S-GD-EYJEH7). 도메인은 아직 없다.</summary>
        Roulette = 3,
    }
}
