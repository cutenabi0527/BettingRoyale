using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Roulette;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Data.Shop;

namespace BettingRoyale.Data.LocalPlay
{
    /// <summary>
    /// Local Play(= Run MVP)의 준비 단계 상점·룰렛. <b>제품과 같은 게이트·같은 원장·같은 지갑</b>을 쓴다.
    ///
    /// <para>
    /// <b>왜 필요한가.</b> Local Play 는 축복만 있었다 — 상점도 룰렛도 <b>없었고</b> 준비맵에는
    /// 시설 모형만 서 있었다. 그러면 Local MVP 로 확인한 준비 단계는 제품의 준비 단계와 다른 것이고,
    /// 상점·룰렛의 회귀는 온라인 2PC 를 띄우기 전까지 드러나지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>표시 전용 모형을 만들지 않는다.</b> 여기 있는 것은 전부 제품 타입이다 —
    /// <see cref="ShopRequestGate"/>, <see cref="RouletteRequestGate"/>,
    /// <see cref="EconomyRequestLedger"/>, <see cref="PreparationAccessGate"/>. 이 클래스가 하는 일은
    /// <b>그것들을 한 벌로 묶어 수명을 맞추는 것</b>뿐이다.
    /// </para>
    ///
    /// <para>
    /// <b>월드 어댑터는 나중에 꽂는다.</b> 인벤토리·아이템 카탈로그·좌표는 Unity 쪽에서 오므로
    /// (D-060) 생성 시점에는 없다. 그래서 게이트에는 <b>바뀔 수 있는 어댑터</b>를 한 번 넣고
    /// <see cref="Configure"/> 가 그 안쪽만 갈아끼운다 — 게이트를 다시 만들면 원장이 갈려
    /// 재전송 판정이 무너진다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class LocalPlayPreparationCommerce : IPreparationAccessPolicy
    {
        /// <summary>
        /// 순위·정산이 쓰는 로컬 사람의 번호. <c>LocalPlayMatchRuntime</c> 의 0번 자리와 같은 값이다 —
        /// 두 곳이 다른 번호를 쓰면 골드와 순위가 다른 사람의 것이 된다.
        /// </summary>
        public const int LocalPlayerId = 0;

        /// <summary>Local Play 룰렛의 틱 레이트. 제품 Host 와 같은 값을 쓴다.</summary>
        public const int RouletteTicksPerSecond = 20;

        private readonly MutableInventoryProvider inventories = new();
        private readonly MutableItemCatalog items = new();
        private readonly MutablePresence presence = new();

        private readonly MatchEconomyState economy;
        private readonly EconomyRequestLedger ledger = new();
        private readonly ShopRequestGate shop;
        private readonly RouletteRequestGate roulette;
        private PreparationAccessGate access;

        private DeterministicRandom rouletteRandom = new(0x524F554C);
        private bool preparationOpen;
        private int nextSequence = 1;

        public LocalPlayPreparationCommerce(MatchEconomyState economy)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));

            access = new PreparationAccessGate(
                PreparationFacilityMapCatalog.CreateDefault(), presence);

            shop = new ShopRequestGate(
                economy, ledger, BettingRoyale.Data.Balance.BalanceCatalogs.Current.Shop, inventories, items, this,
                requiresBlessingSelection: false);

            // ⚠️ 상점과 **같은 원장·같은 접근 정책**이다. 갈리면 순번이 두 갈래가 되고
            //    "상점은 되는데 룰렛만 안 되는" 자리가 생긴다 (S-GD-66GTWA · S-CLI-9QYFCT).
            roulette = new RouletteRequestGate(
                economy,
                ledger,
                new RouletteTable(
                    BettingRoyale.Data.Balance.BalanceCatalogs.Current.Roulette.CreateWheel(),
                    BettingRoyale.Data.Balance.BalanceCatalogs.Current.Roulette.CreateConfig(RouletteTicksPerSecond)),
                this);
        }

        /// <summary>
        /// 월드 어댑터를 꽂는다. <b>맵 키까지 함께 받는다</b> — 준비 맵이 늘었을 때 Local Play 만
        /// 옛 배치를 쓰는 일이 없도록 구역표를 같은 카탈로그에서 뽑는다
        /// (<see cref="PreparationFacilityMapCatalog"/>).
        /// </summary>
        public void Configure(
            IShopInventoryProvider inventoryProvider,
            IEquipmentItemCatalog itemCatalog,
            IPreparationPresenceProvider presenceProvider,
            MatchSceneKey preparationMapKey)
        {
            inventories.Inner = inventoryProvider;
            items.Inner = itemCatalog;
            presence.Inner = presenceProvider;
            access = new PreparationAccessGate(
                PreparationFacilityMapCatalog.Resolve(preparationMapKey, out _), presence);
        }

        /// <summary>월드가 아직 없을 때의 조립. 구역표는 기본 준비 맵 배치다.</summary>
        public void Configure(
            IShopInventoryProvider inventoryProvider,
            IEquipmentItemCatalog itemCatalog,
            IPreparationPresenceProvider presenceProvider) =>
            Configure(inventoryProvider, itemCatalog, presenceProvider, MatchSceneCatalog.DefaultPreparation);

        /// <summary>
        /// 시설 접근 판정. <b>좌표로 재는 제품 게이트를 그대로 통과시킨다</b> —
        /// Local Play 라고 "항상 가까이 있다" 로 두면 구역 규칙이 여기서만 사라진다.
        /// 단 준비 단계가 닫혀 있으면 어느 시설도 열리지 않는다.
        /// </summary>
        public bool CanUse(int playerId, PreparationFacility facility) =>
            preparationOpen && playerId == LocalPlayerId && access.CanUse(playerId, facility);

        /// <summary>지금 이 사람이 서 있는 시설. 화면 안내(<c>E 를 눌러 여세요</c>)가 읽는다.</summary>
        public PreparationFacilityInRangeChanged BuildInRange(bool isMine = true) =>
            preparationOpen
                ? access.BuildInRange(LocalPlayerId, isMine)
                : new PreparationFacilityInRangeChanged(LocalPlayerId, isMine, PreparationFacility.None);

        public ShopOffersChanged BuildOffers(bool isMine = true) => shop.BuildOffers(LocalPlayerId, isMine);

        public RouletteWheelAnnounced BuildWheel() => roulette.BuildWheel();

        public RouletteStateChanged BuildRouletteState(bool isMine = true) =>
            roulette.BuildState(LocalPlayerId, isMine);

        /// <summary>
        /// 준비 단계를 연다. <b>라운드마다 부른다</b> — 빠뜨리면 1라운드에만 룰렛이 돌고
        /// 2·3라운드는 조용히 안 열린다 (<see cref="RouletteRequestGate.BeginPreparation"/>).
        /// </summary>
        public void BeginPreparation(int round)
        {
            preparationOpen = true;
            ledger.OpenPreparation();
            rouletteRandom = new DeterministicRandom(unchecked(0x524F554C ^ (round * 7919)));
            roulette.BeginPreparation(LocalPlayerId, true);
        }

        /// <summary>
        /// 준비 단계를 닫는다. <b>진행 중이던 베팅은 전액 돌려준다</b> —
        /// 돌지 않은 판에 돈을 먹지 않는다 (S-GD-7VKZA5).
        /// </summary>
        public RouletteTickOutcome EndPreparation()
        {
            preparationOpen = false;
            return roulette.Close(LocalPlayerId, true);
        }

        /// <summary>바퀴를 <paramref name="ticks"/> 만큼 돌린다. 정산은 같은 지갑에 반영된다.</summary>
        public LocalPlayRouletteTick TickRoulette(int ticks)
        {
            if (!preparationOpen || ticks < 1)
            {
                return LocalPlayRouletteTick.None;
            }

            var settled = new List<RouletteSettled>();
            var golds = new List<GoldChanged>();
            bool changed = false;

            for (int i = 0; i < ticks; i++)
            {
                RouletteTickOutcome step = roulette.Tick(
                    Math.Max(0, RemainingPreparationTicks - i), rouletteRandom, LocalPlayerId, true);

                changed |= step.HasStateChange;
                if (step.Settlements != null) settled.AddRange(step.Settlements);
                if (step.Golds != null) golds.AddRange(step.Golds);
            }

            return new LocalPlayRouletteTick(
                changed, BuildRouletteState(), settled.ToArray(), golds.ToArray());
        }

        /// <summary>
        /// 남은 준비 시간(틱). <b>월드가 넣어준다</b> — 룰렛이 전투 라운드로 넘어가지 않도록
        /// 마감 리드타임을 제품과 같은 규칙으로 계산하게 한다.
        /// </summary>
        public int RemainingPreparationTicks { get; set; } = int.MaxValue;

        /// <summary>구매 한 건. 원장 순번은 여기서 뽑는다 — 화면이 순번을 세지 않는다.</summary>
        public ShopPurchaseOutcome Purchase(int offerId) =>
            shop.HandlePurchase(LocalPlayerId, offerId, NextTicket(), isMine: true);

        /// <summary>베팅 한 건. 가진 것보다 많이 걸면 가진 만큼으로 깎인다 — 거절이 아니다.</summary>
        public RouletteRequestOutcome Bet(int number, int amount) =>
            roulette.HandleBet(LocalPlayerId, number, amount, NextTicket(), isMine: true);

        /// <summary>이번 판에 건 것을 전부 물린다.</summary>
        public RouletteWithdrawOutcome Withdraw() =>
            roulette.HandleWithdraw(LocalPlayerId, NextTicket(), isMine: true);

        /// <summary>새 매치. <b>원장 순번도 처음부터 다시 센다.</b></summary>
        public void ResetForNewMatch()
        {
            preparationOpen = false;
            nextSequence = 1;
            roulette.Close(LocalPlayerId, true);
        }

        private EconomyRequestTicket NextTicket() =>
            new EconomyRequestTicket(ledger.Epoch, nextSequence++);

        // ── 바뀔 수 있는 어댑터 ────────────────────────────────────────────
        //
        // 게이트는 생성 시점에 한 번만 받는다. 월드가 늦게 준비되는 것은 정상이므로
        // "아직 없음" 을 false 로 답하고, 꽂히면 그대로 위임한다.

        private sealed class MutableInventoryProvider : IShopInventoryProvider
        {
            public IShopInventoryProvider Inner { get; set; }

            public bool TryResolve(int playerId, out IEquipmentInventoryDriver inventory)
            {
                if (Inner != null) return Inner.TryResolve(playerId, out inventory);
                inventory = null;
                return false;
            }
        }

        private sealed class MutableItemCatalog : IEquipmentItemCatalog
        {
            public IEquipmentItemCatalog Inner { get; set; }

            public bool TryGet(int itemId, out EquipmentItemDefinition definition)
            {
                if (Inner != null) return Inner.TryGet(itemId, out definition);
                definition = default;
                return false;
            }
        }

        private sealed class MutablePresence : IPreparationPresenceProvider
        {
            public IPreparationPresenceProvider Inner { get; set; }

            public bool TryGetPosition(int playerId, out WorldPosition position)
            {
                if (Inner != null) return Inner.TryGetPosition(playerId, out position);
                position = default;
                return false;
            }
        }
    }

    /// <summary>
    /// 한 프레임에 접은 틱 전부의 결과. 타입을 따로 두는 이유는
    /// <see cref="Training.TrainingRouletteTick"/> 주석과 같다 — 중간 판의 정산을 잃지 않기 위해서다.
    /// </summary>
    public readonly struct LocalPlayRouletteTick
    {
        public static readonly LocalPlayRouletteTick None = new(
            false, default, Array.Empty<RouletteSettled>(), Array.Empty<GoldChanged>());

        public LocalPlayRouletteTick(
            bool hasStateChange,
            RouletteStateChanged state,
            RouletteSettled[] settlements,
            GoldChanged[] golds)
        {
            HasStateChange = hasStateChange;
            State = state;
            Settlements = settlements ?? Array.Empty<RouletteSettled>();
            Golds = golds ?? Array.Empty<GoldChanged>();
        }

        public bool HasStateChange { get; }
        public RouletteStateChanged State { get; }
        public RouletteSettled[] Settlements { get; }
        public GoldChanged[] Golds { get; }
    }
}
