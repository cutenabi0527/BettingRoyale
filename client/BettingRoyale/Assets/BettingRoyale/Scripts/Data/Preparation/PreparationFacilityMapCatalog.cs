using BettingRoyale.Data.Scenes;

namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// <b>어느 준비 맵을 골라도 세 시설이 조립되게</b> 하는 유일한 조회처.
    ///
    /// <para>
    /// <b>무엇을 막는가.</b> 준비 맵 키는 이미 두 개다(<c>preparation/01</c>·<c>preparation/02</c>)
    /// 이고 대기실이 고를 수 있다. 그런데 구역표를 부르는 자리마다
    /// <see cref="PlaceholderPreparationFacilityMap.Create"/> 를 직접 박아 두면, 새 준비 맵이 늘었을 때
    /// <b>아무도 컴파일 에러를 보지 않은 채 그 맵에서만 상점·룰렛·모루가 사라진다.</b> 그 증상은
    /// "E 를 눌러도 아무 일도 없다" 뿐이라 원인을 찾기 어렵다.
    /// </para>
    ///
    /// <para>
    /// <b>지금은 모든 준비 맵이 같은 배치를 쓴다.</b> 두 맵 모두 같은 저작 앵커
    /// (<c>RouletteAnchor_01</c>·<c>AnvilAnchor_01</c>·<c>ShopAnchor_01</c>)를 같은 맵 로컬 좌표에
    /// 두기로 한 계약이기 때문이다. 배치가 맵마다 달라지면 <b>이 파일만</b> 키별 분기를 갖는다 —
    /// 호출부는 바뀌지 않는다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ 반경·중심은 여전히 임시값이다 (S-GD-66GTWA · <c>PlaceholderPreparationFacilityMap</c>).
    /// 이 카탈로그는 값을 새로 정하지 않고 <b>어느 맵이 어느 배치를 쓰는지</b>만 정한다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class PreparationFacilityMapCatalog
    {
        // 기획 엑셀 facilities 시트가 정본이다 (S-CLI-QXRTWX). 데이터가 없으면 BalanceCatalogs 가 코드 대역
        // (PlaceholderPreparationFacilityMap)을 돌려준다. 맵 키별 배치가 생기면 여기서 키로 고른다.

        /// <summary>
        /// 이 키가 등록된 준비 맵이면 그 맵의 구역표를 만든다.
        /// <b>전투 맵 키나 모르는 키에는 <c>false</c></b> — 조용히 기본 배치를 주면 잘못된 맵에서도
        /// 시설이 열려 버린다.
        /// </summary>
        public static bool TryCreate(MatchSceneKey key, out PreparationFacilityMap map)
        {
            if (MatchSceneCatalog.TryGetForPhase(key, MatchScenePhase.Preparation, out _))
            {
                map = BettingRoyale.Data.Balance.BalanceCatalogs.Current.Facilities.DefaultMap;
                return true;
            }

            map = null;
            return false;
        }

        /// <summary>
        /// 구역표를 반드시 얻는다. 키를 모르면 <b>기본 준비 맵의 배치</b>로 되돌린다.
        ///
        /// <para>
        /// 네트워크 없는 준비 경로(Local Play · 96_TrainingLab)처럼 맵 키가 없는 조립도 세 시설을
        /// 그대로 가져야 하므로, 여기서는 던지지 않는다. 대신 <paramref name="isRegistered"/> 로
        /// "등록된 키였는가" 를 함께 돌려줘 호출부가 진단 로그를 남길 수 있게 한다.
        /// </para>
        /// </summary>
        public static PreparationFacilityMap Resolve(MatchSceneKey key, out bool isRegistered)
        {
            isRegistered = TryCreate(key, out PreparationFacilityMap map);
            return isRegistered ? map : BettingRoyale.Data.Balance.BalanceCatalogs.Current.Facilities.DefaultMap;
        }

        /// <summary>맵 키를 모르는 조립(Local Play · 훈련 세션)이 쓰는 기본 배치.</summary>
        public static PreparationFacilityMap CreateDefault() =>
            BettingRoyale.Data.Balance.BalanceCatalogs.Current.Facilities.DefaultMap;
    }
}
