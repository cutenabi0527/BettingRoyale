using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// ⚠️ <b>임시 구역 배치다</b> (S-GD-66GTWA, status: assumed).
    ///
    /// <para>
    /// 중심 좌표는 <b>임의값이 아니다</b> — 준비 콜로세움에 실제로 서 있는 시설 위치를 그대로 옮겼다
    /// ([S-CLI-EYCKKC](../../../../../docs/specs/client-engineering/S-CLI-EYCKKC.md) §3).
    /// <b>반경은 사용자가 정했고 Play 로 한 번 고쳤다</b> — 상점·모루는 3m 가 너무 좁아
    /// 5m 로 넓혔다(S-GD-VYGZTJ, 2026-09-15 실측).
    /// </para>
    ///
    /// <code>
    ///                            허수아비 (0, 12)
    ///
    ///    ◍ 모루(-11,0) r5      ◍ 룰렛(0,0) r8      ◍ 상점(11,0) r5
    ///      [-16 ~ -6]            [-8 ~ 8]             [6 ~ 16]
    ///                  ↑ 겹침(x 6~8): 중심이 가까운 상점·모루가 이긴다 → 경계 x = ±6
    ///
    ///                  ▬▬ 집결·스폰 (z = -12) ▬▬
    ///                     콜로세움 반경 20
    /// </code>
    ///
    /// <para>
    /// <b>룰렛만 큰 이유</b>: 룰렛은 방에 바퀴가 하나라 다 같이 모여서 본다. 상점·강화는 각자
    /// 자기 창을 여는 개인 행동이라 몰릴 이유가 없다.
    /// </para>
    ///
    /// <para>
    /// <b>룰렛은 8m 이고 구역이 겹친다</b>(S-GD-EMDEBM, 2026-09-26 사용자 확정). 게임 데이터의 현행 값
    /// (S-GD-R2PPN3 · S-GD-3DEQ2M)과 같게 둔다 — 예전 5m 로는 준비 콜로세움의 룰렛 받침(반지름 4.65m)에 막혀
    /// 캐릭터가 구역에 들어갈 수 없었다. 겹친 자리는 중심이 가까운 시설이 열린다.
    /// </para>
    ///
    /// <para>
    /// <b>이 클래스는 지우기 위해 있다.</b> 확정 수치가 데이터로 들어오면(D-041) 이 파일을 삭제한다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class PlaceholderPreparationFacilityMap
    {
        /// <summary>
        /// 룰렛 반경. 게임 데이터 첫 조정값(8m)과 같다(S-GD-EMDEBM). 받침 충돌체 밖에서 멈춘 캐릭터(중심 5.17~5.23m)가
        /// 구역 안에 들어와야 한다.
        /// </summary>
        public const float RouletteRadius = 8f;

        /// <summary>
        /// 상점 반경. <b>사용자 확정값</b>(S-GD-VYGZTJ, 2026-09-15).
        /// </summary>
        /// <remarks>
        /// 3m 였다가 늘렸다 — Play 에서 <i>"너무 좁아서 들어가기 힘들어"</i> 였다.
        /// 룰렛 구역과 겹치는 자리(x 6~8)는 중심이 더 가까운 이쪽이 이긴다 — 경계는 이 구역 가장자리 x = ±6(S-GD-EMDEBM).
        /// </remarks>
        public const float ShopRadius = 5f;

        /// <summary>모루(강화) 반경. <b>사용자 확정값</b>(S-GD-VYGZTJ). 상점과 같은 이유로 늘렸다.</summary>
        public const float UpgradeRadius = 5f;

        /// <summary>구역이 겹친다 — 룰렛 8 + 상점 5 가 간격 11 을 넘는다. 겹친 자리는 중심이 가까운 시설이 이긴다.</summary>
        public const bool AllowOverlap = true;

        public static PreparationFacilityMap Create() => new PreparationFacilityMap(new[]
        {
            new PreparationFacilityArea(
                PreparationFacility.Roulette, new WorldPosition(0f, 0f, 0f), RouletteRadius),
            new PreparationFacilityArea(
                PreparationFacility.Upgrade, new WorldPosition(-11f, 0f, 0f), UpgradeRadius),
            new PreparationFacilityArea(
                PreparationFacility.Shop, new WorldPosition(11f, 0f, 0f), ShopRadius),
        }, AllowOverlap);
    }
}
