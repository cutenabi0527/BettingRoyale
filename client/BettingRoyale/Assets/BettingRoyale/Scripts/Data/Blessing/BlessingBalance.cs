using BettingRoyale.Data.Remote;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 수치의 <b>정본 공급처</b> — 기획 엑셀(<c>gamedata.bin</c>)이 올라와 있으면 그것을,
    /// 아니면 하드코딩 대역을 준다 (S15P21A606-201 · D-CLI-ETYTBX).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="LocalPlayBlessingCatalog"/> 가 <b>어떤 축복이 있는가</b>를 정본으로 바꾼 것과 짝이다.
    /// 여기는 <b>그 축복이 무엇을 하는가</b>(계수)와 <b>어떤 스탯이 있는가</b>(정의)를 맡는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>스탯과 계수를 따로 고르지 않는다.</b> 둘 중 하나만 정본으로 바꾸면
    /// <see cref="BlessingEffectBinder"/> 가 깨진다 — 계수의 <c>param_key</c> 가 스탯인지 아닌지로
    /// 갈래를 정하기 때문이다. 엑셀 계수(스탯 24종 기준)를 하드코딩 스탯(12종)에 물리면
    /// 없는 스탯을 가리키는 행이 <c>KeyNotFoundException</c> 으로 터지거나, 더 나쁘게는
    /// <b>조용히 행동 파라미터로 빠져 아무 효과가 없어진다.</b> 그래서 <see cref="IsFromGameData"/>
    /// 하나로 둘을 동시에 고른다.
    /// </para>
    /// <para>
    /// <b><see cref="IsFromGameData"/> 가 <c>false</c> 인 경로는 정상이다</b> — 배치모드(CI·EditMode
    /// 테스트)에서는 <c>Bootstrap</c> 이 원격 콘텐츠 준비를 통째로 건너뛴다(D-CLI-CAJZW6).
    /// 제품 경로에서는 데이터를 못 읽으면 <c>Bootstrap</c> 이 진입 자체를 막으므로, 대역을 든 채
    /// 온라인 매치에 들어오는 피어는 없다.
    /// </para>
    /// <para>
    /// <b>대역은 한 번만 만든다.</b> 스탯 정의·계수표는 만들어진 뒤 바뀌지 않고
    /// (<see cref="StatRegistry"/>·<see cref="BlessingEffectCatalog"/> 둘 다 조회 전용),
    /// 매치마다 새로 쌓이는 것은 <see cref="StatSheet"/> 쪽이다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class BlessingBalance
    {
        private static readonly StatRegistry FallbackStats = PlaceholderBlessingBalance.CreateStatRegistry();

        private static readonly BlessingEffectCatalog FallbackEffects =
            PlaceholderBlessingBalance.CreateEffectCatalog();

        /// <summary>
        /// 기획 엑셀에서 온 수치를 쓰고 있는가. <c>false</c> 면 하드코딩 대역이다.
        /// </summary>
        /// <remarks>
        /// 스탯이 비어 있으면 계수가 있어도 쓸 수 없다 — 모든 계수가 "스탯이 아님" 으로 분류되어
        /// 조용히 무효가 된다. 그래서 <b>둘 다</b> 채워져 있을 때만 정본으로 친다.
        /// </remarks>
        public static bool IsFromGameData =>
            GameContentProvider.IsLoaded
            && GameContentProvider.Current.Stats.Count > 0
            && GameContentProvider.Current.Effects.RowCount > 0;

        /// <summary>피해 계산·강화·축복이 읽는 스탯 정의.</summary>
        public static StatRegistry StatRegistry =>
            IsFromGameData ? GameContentProvider.Current.Stats : FallbackStats;

        /// <summary>축복 하나가 어떤 계수를 갖는지.</summary>
        public static BlessingEffectCatalog EffectCatalog =>
            IsFromGameData ? GameContentProvider.Current.Effects : FallbackEffects;

        /// <summary>
        /// 방어 관통(<c>armor-pen</c>)이 피해 계산·표시에 효력을 갖는가. 2026-09-21 사용자 요청으로 기획 데이터가
        /// <c>ARMOR_PEN_ENABLED=FALSE</c> 로 끈다 (S-GD-3DEQ2M). 축복·강화의 ID 와 수치는 그대로 두고 효력만 0 이다 —
        /// 다시 켜려면 상수만 바꾼다. 코드 대역(데이터 없음)은 켜져 있다.
        /// </summary>
        public static bool ArmorPenetrationEnabled => BettingRoyale.Data.Balance.BalanceCatalogs.Current.ArmorPenetrationEnabled;

        /// <summary>엑셀 <c>constants</c> 의 최소 피해 하한 키.</summary>
        public const string MinDamageFloorKey = "MIN_DAMAGE_FLOOR";

        /// <summary>
        /// 최소 피해 하한 (S-GD-56NVEJ). <b>정본은 기획 엑셀 <c>constants</c> 시트</b>다.
        /// </summary>
        /// <remarks>
        /// 엑셀이 없는 배치모드(CI·EditMode 테스트)에서는 <see cref="DamagePipeline.DefaultMinDamageFloor"/> 로
        /// 떨어진다 — 그 경로는 정상이다(D-CLI-CAJZW6). <b>수치를 바꾸려면 엑셀을 고친다.</b>
        /// </remarks>
        public static float MinDamageFloor => IsFromGameData
            ? GameDataLoader.ConstantOrDefault(GameContentProvider.Current.Constants, MinDamageFloorKey,
                DamagePipeline.DefaultMinDamageFloor)
            : DamagePipeline.DefaultMinDamageFloor;

        /// <summary>로그·진단용 한 줄.</summary>
        public static string Describe() =>
            $"{(IsFromGameData ? "기획 엑셀" : "하드코딩 대역")} · {StatRegistry} · {EffectCatalog}";
    }
}
