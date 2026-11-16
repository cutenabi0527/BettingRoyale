using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Online;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Remote;
using BettingRoyale.Data.Roulette;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Balance
{
    /// <summary>
    /// 기획 엑셀에서 만든 <b>불변 밸런스 카탈로그 묶음</b> (S-CLI-QXRTWX, S15P21A606-274).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 게임 데이터(<c>gamedata.bin</c> v3)를 읽은 뒤 <see cref="BalanceCatalogBuilder"/> 가 한 번 만들고
    /// <see cref="GameContent.Balance"/> 에 붙인다. 소비자는 <see cref="Current"/> 로 읽는다 —
    /// 데이터가 없으면(배치모드·EditMode·CombatLab) 코드 대역 <see cref="Fallback"/> 이 나온다 (D-CLI-VMX6AN).
    /// </para>
    /// <para>
    /// ⚠️ <b>static 초기화가 옛 값을 붙잡지 않게 한다.</b> 이전에는 <c>UpgradeSetBonusGuide.DefaultCatalog</c> 같은
    /// static readonly 필드가 프로세스 시작 때 대역을 굳혀 엑셀이 와도 화면이 안 바뀌었다. 여기서는 모든 접근이
    /// <see cref="Current"/> 프로퍼티를 지나므로 데이터를 다시 받은 세션부터 새 값이 보인다.
    /// 실행 중 매치의 수치를 바꾸지는 않는다 — 콘텐츠는 기동 때 한 번 채워진다(<see cref="GameContentProvider"/>).
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public sealed class BalanceCatalogs
    {
        private static readonly Lazy<BalanceCatalogs> fallback = new Lazy<BalanceCatalogs>(CreateFallback);

        public BalanceCatalogs(
            bool isFromGameData,
            UpgradeCatalog upgrades,
            ShopCatalog shop,
            DataItemCatalog items,
            ConsumableEffectTable itemEffects,
            ChestLootTable chestLoot,
            ChestRules chest,
            FacilityBalance facilities,
            RouletteBalance roulette,
            OnlineCoreLoopRules rounds,
            EconomyConfig economy,
            ScoringConfig scoring,
            BlessingConfig blessing,
            CombatZoneSettings zone,
            WeaponBalance weapons,
            CombatantBalance combatant,
            bool armorPenetrationEnabled,
            IReadOnlyCollection<string> disabledBlessingIds)
        {
            IsFromGameData = isFromGameData;
            Upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
            Shop = shop ?? throw new ArgumentNullException(nameof(shop));
            Items = items;
            ItemEffects = itemEffects;
            ChestLoot = chestLoot ?? throw new ArgumentNullException(nameof(chestLoot));
            Chest = chest ?? throw new ArgumentNullException(nameof(chest));
            Facilities = facilities ?? throw new ArgumentNullException(nameof(facilities));
            Roulette = roulette ?? throw new ArgumentNullException(nameof(roulette));
            Rounds = rounds;
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            Scoring = scoring ?? throw new ArgumentNullException(nameof(scoring));
            Blessing = blessing ?? throw new ArgumentNullException(nameof(blessing));
            Zone = zone ?? throw new ArgumentNullException(nameof(zone));
            Weapons = weapons;
            Combatant = combatant;
            ArmorPenetrationEnabled = armorPenetrationEnabled;
            DisabledBlessingIds = disabledBlessingIds ?? Array.Empty<string>();
        }

        /// <summary>기획 데이터에서 만들었으면 <c>true</c>. 코드 대역이면 <c>false</c>.</summary>
        public bool IsFromGameData { get; }

        /// <summary>강화 표 + 세트 (upgrades·upgrade_levels·upgrade_sets).</summary>
        public UpgradeCatalog Upgrades { get; }

        /// <summary>상점 진열 (items 의 purchasable 행).</summary>
        public ShopCatalog Shop { get; }

        /// <summary>아이템 정의·보유 한도·투척물 (items·throwables). 대역이면 <c>null</c> — 소비자는 코드 카탈로그를 쓴다.</summary>
        public DataItemCatalog Items { get; }

        /// <summary>소모품 효과·장신구 스탯 (item_effects). 대역이면 <c>null</c>.</summary>
        public ConsumableEffectTable ItemEffects { get; }

        public ChestLootTable ChestLoot { get; }

        public ChestRules Chest { get; }

        public FacilityBalance Facilities { get; }

        public RouletteBalance Roulette { get; }

        public OnlineCoreLoopRules Rounds { get; }

        public EconomyConfig Economy { get; }

        public ScoringConfig Scoring { get; }

        public BlessingConfig Blessing { get; }

        public CombatZoneSettings Zone { get; }

        /// <summary>무기 수치. 대역이면 <c>null</c> — <c>WeaponStats</c> 코드 값을 쓴다.</summary>
        public WeaponBalance Weapons { get; }

        /// <summary>전투원 기본값(대시 충전·재충전·지속). 대역이면 <c>null</c> — 프리팹 값을 쓴다.</summary>
        public CombatantBalance Combatant { get; }

        /// <summary>방어 관통 효력. 2026-09-21 사용자 요청으로 데이터가 끈다 (S-GD-3DEQ2M). 코드 대역은 켜져 있다.</summary>
        public bool ArmorPenetrationEnabled { get; }

        /// <summary>후보 풀·훈련 목록에서 빼는 축복 id (constants.DISABLED_BLESSING_IDS).</summary>
        public IReadOnlyCollection<string> DisabledBlessingIds { get; }

        public bool IsBlessingDisabled(string blessingId)
        {
            if (string.IsNullOrEmpty(blessingId)) return false;
            foreach (string id in DisabledBlessingIds)
                if (string.Equals(id, blessingId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>지금 세션의 카탈로그. 기획 데이터가 있으면 그것, 없으면 <see cref="Fallback"/>.</summary>
        public static BalanceCatalogs Current
        {
            get
            {
                GameContent content = GameContentProvider.Current;
                return content != null && content.Balance != null ? content.Balance : Fallback;
            }
        }

        /// <summary>코드 대역 — 게임 데이터 없이 도는 경로(테스트·CombatLab)용. 값은 각 Placeholder 클래스에 있다.</summary>
        public static BalanceCatalogs Fallback => fallback.Value;

        private static BalanceCatalogs CreateFallback() => new BalanceCatalogs(
            isFromGameData: false,
            upgrades: PlaceholderUpgradeCatalog.Create(),
            shop: PlaceholderShopCatalog.Create(),
            items: null,
            itemEffects: null,
            chestLoot: PlaceholderChestLootTable.Create(),
            chest: ChestRules.Placeholder,
            facilities: new FacilityBalance(PlaceholderPreparationFacilityMap.Create(), PlaceholderPreparationFacilityMap.AllowOverlap),
            roulette: RouletteBalance.Placeholder,
            rounds: OnlineCoreLoopRules.CreatePlaceholderDefault(),
            economy: new EconomyConfig(EconomyConfig.DefaultRoundGrantGold, CarryOverLimit.Default),
            scoring: ScoringConfig.CreatePlaceholderDefault(),
            blessing: BlessingConfig.CreatePlaceholderDefault(),
            zone: CombatZoneSettings.CreatePlaceholderConfirmed(),
            weapons: null,
            combatant: null,
            armorPenetrationEnabled: true,
            disabledBlessingIds: Array.Empty<string>());
    }

    /// <summary>상자 시간·보상 규칙 (constants.CHEST_*). 초 단위 — 틱 변환은 조립하는 쪽이 한다.</summary>
    public sealed class ChestRules
    {
        public ChestRules(float openSeconds, float respawnDelaySeconds, float retryDelaySeconds, int rewardGold, int chestCount)
        {
            if (openSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(openSeconds));
            if (respawnDelaySeconds < 0f) throw new ArgumentOutOfRangeException(nameof(respawnDelaySeconds));
            if (retryDelaySeconds < 0f) throw new ArgumentOutOfRangeException(nameof(retryDelaySeconds));
            if (rewardGold < 0) throw new ArgumentOutOfRangeException(nameof(rewardGold));
            if (chestCount < 1) throw new ArgumentOutOfRangeException(nameof(chestCount));
            OpenSeconds = openSeconds;
            RespawnDelaySeconds = respawnDelaySeconds;
            RetryDelaySeconds = retryDelaySeconds;
            RewardGold = rewardGold;
            ChestCount = chestCount;
        }

        public float OpenSeconds { get; }
        public float RespawnDelaySeconds { get; }
        public float RetryDelaySeconds { get; }
        public int RewardGold { get; }
        public int ChestCount { get; }

        /// <summary>코드 대역. 개봉 5초·재생성 5초(S-GD-XRNJ7R)·재시도 1초·300G·2개.</summary>
        public static ChestRules Placeholder => new ChestRules(
            PlaceholderChestLootTable.OpenSeconds, PlaceholderRespawnDelaySeconds, 1f, PlaceholderChestLootTable.RewardGold, 2);

        /// <summary>개봉 완료 후 다음 안전지대 재생성까지의 대역값 (S-GD-XRNJ7R).</summary>
        public const int PlaceholderRespawnDelaySeconds = 5;
    }

    /// <summary>시설 구역표 + 겹침 정책 (facilities · constants.FACILITY_ALLOW_OVERLAP).</summary>
    public sealed class FacilityBalance
    {
        public FacilityBalance(PreparationFacilityMap defaultMap, bool allowOverlap)
        {
            DefaultMap = defaultMap ?? throw new ArgumentNullException(nameof(defaultMap));
            AllowOverlap = allowOverlap;
        }

        public PreparationFacilityMap DefaultMap { get; }

        /// <summary>겹치는 구역을 허용하는가. 허용하면 중심이 가까운 시설이 열린다(<see cref="PreparationFacilityMap.Resolve"/>).</summary>
        public bool AllowOverlap { get; }
    }

    /// <summary>룰렛 바퀴 + 시간(초). 틱 변환은 <see cref="CreateConfig"/> 가 한다.</summary>
    public sealed class RouletteBalance
    {
        public RouletteBalance(IReadOnlyList<int> layout, int betSeconds, int spinSeconds, int resultSeconds,
            int minBet, int openMarginSeconds, int startDelaySeconds)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            new RouletteWheel(layout); // 배치 검증 — 잘못된 표는 여기서 던진다
            if (betSeconds < 1 || spinSeconds < 1 || resultSeconds < 1) throw new ArgumentOutOfRangeException(nameof(betSeconds));
            if (minBet < 1) throw new ArgumentOutOfRangeException(nameof(minBet));
            if (openMarginSeconds < 0 || startDelaySeconds < 0) throw new ArgumentOutOfRangeException(nameof(openMarginSeconds));
            BetSeconds = betSeconds;
            SpinSeconds = spinSeconds;
            ResultSeconds = resultSeconds;
            MinBet = minBet;
            OpenMarginSeconds = openMarginSeconds;
            StartDelaySeconds = startDelaySeconds;
        }

        public IReadOnlyList<int> Layout { get; }
        public int BetSeconds { get; }
        public int SpinSeconds { get; }
        public int ResultSeconds { get; }
        public int MinBet { get; }
        public int OpenMarginSeconds { get; }
        public int StartDelaySeconds { get; }

        public RouletteWheel CreateWheel() => new RouletteWheel(Layout);

        public RouletteConfig CreateConfig(int ticksPerSecond) => new RouletteConfig(
            betTicks: checked(BetSeconds * ticksPerSecond),
            spinTicks: checked(SpinSeconds * ticksPerSecond),
            resultTicks: checked(ResultSeconds * ticksPerSecond),
            minBet: MinBet,
            openMarginTicks: checked(OpenMarginSeconds * ticksPerSecond),
            startDelayTicks: checked(StartDelaySeconds * ticksPerSecond));

        public static RouletteBalance Placeholder => new RouletteBalance(
            PlaceholderRouletteWheel.Layout,
            PlaceholderRouletteConfig.BetSeconds, PlaceholderRouletteConfig.SpinSeconds,
            PlaceholderRouletteConfig.ResultSeconds, PlaceholderRouletteConfig.MinBet,
            PlaceholderRouletteConfig.OpenMarginSeconds, PlaceholderRouletteConfig.StartDelaySeconds);
    }

    /// <summary>무기별 수치 (weapons 시트). <c>WeaponStats.TryGet</c> 이 코드 값보다 먼저 본다.</summary>
    public sealed class WeaponBalance
    {
        private readonly Dictionary<WeaponType, WeaponConfig> configs = new Dictionary<WeaponType, WeaponConfig>();
        private readonly Dictionary<WeaponType, WeaponAttackSlow> slows = new Dictionary<WeaponType, WeaponAttackSlow>();

        public void Set(WeaponType weapon, in WeaponConfig config, in WeaponAttackSlow slow)
        {
            configs[weapon] = config;
            slows[weapon] = slow;
        }

        public int Count => configs.Count;

        public bool TryGet(WeaponType weapon, out WeaponConfig config) => configs.TryGetValue(weapon, out config);

        public bool TryGetAttackSlow(WeaponType weapon, out WeaponAttackSlow slow) => slows.TryGetValue(weapon, out slow);
    }

    /// <summary>전투원 기본값 (stats 시트의 dash-charges·dash-recharge·dash-duration). 프리팹 값은 대역이다.</summary>
    public sealed class CombatantBalance
    {
        public CombatantBalance(int dashCharges, float dashRechargeSeconds, float dashDurationSeconds)
        {
            DashCharges = dashCharges;
            DashRechargeSeconds = dashRechargeSeconds;
            DashDurationSeconds = dashDurationSeconds;
        }

        /// <summary>0 이면 "데이터에 없음" — 프리팹 값을 쓴다.</summary>
        public int DashCharges { get; }
        public float DashRechargeSeconds { get; }
        public float DashDurationSeconds { get; }
    }
}
