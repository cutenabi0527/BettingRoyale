using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using WeaponType = BettingRoyale.Data.Combat.WeaponType;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Economy
{
    /// <summary>매치가 지금 어느 국면인가. 조작이 언제 허용되는지를 이 값이 정한다.</summary>
    public enum MatchEconomyPhase
    {
        /// <summary>아직 <see cref="MatchEconomyState.BeginMatch"/> 를 부르지 않았다.</summary>
        NotStarted = 0,

        /// <summary>준비 단계. 잠금·재추첨·선택이 이때만 된다.</summary>
        Preparation = 1,

        /// <summary>전투 중. 준비 조작은 전부 거부된다.</summary>
        Combat = 2,

        /// <summary>마지막 라운드까지 정산이 끝났다.</summary>
        Finished = 3,
    }

    /// <summary>
    /// 한 매치의 <b>경제·축복 상태 전체</b>를 들고 3라운드를 진행한다
    /// (Jira S15P21A606-31 "준비 골드·증강·라운드 정산 코어").
    ///
    /// <b>왜 플레이어 한 명이 아니라 방 전체인가</b> — 완료 조건이 "전 참가자의 결정 가능한
    /// 최종 순서 데이터" 를 요구한다. 1인분씩 흩어져 있으면 순위를 만들 주체가 없어지고,
    /// 결국 Network 나 UI 가 모아서 계산하게 된다. 그건 티켓이 금지하는 "계산 복제" 다.
    ///
    /// <b>여기에 없는 것</b>:
    /// <list type="bullet">
    ///   <item><b>Unity·Fusion</b> — 순수 C# 이다 (D-009, D-060). 어댑터가 경계에서 잇는다.</item>
    ///   <item><b>보물상자</b> — <c>-32</c> 범위다. 티켓의 "수요일 제외" 에 명시돼 있다.</item>
    ///   <item><b>최종 rank 산정</b> — 무엇이 1등인지가 스펙에 없다. <see cref="MatchRoundTally"/> 참고.</item>
    ///   <item><b>이벤트 발행</b> — <see cref="BuildSnapshot"/> 로 상태를 주고, 발행은 어댑터가 한다.</item>
    /// </list>
    ///
    /// 참고 스펙: S-GD-2CFK2Z(라운드 지급) · S-GD-P5RFZM(이월) · S-GD-6H2Q5Q(3라운드) ·
    /// S-GD-S46S28(후보 3개·잠금 2개) · S-GD-SHJGNB(재추첨 비용) · S-GD-RFXF8X(매치 초기화) ·
    /// S-GD-HE87JY(등급은 라운드 순서) · S-GD-VF67AG(획득 축복 재등장 금지).
    /// </summary>
    public sealed class MatchEconomyState
    {
        /// <summary>플레이어 한 명의 매치 내 경제·축복 상태.</summary>
        private sealed class PlayerEconomy
        {
            public PlayerEconomy(
                int playerId,
                WeaponTag equippedWeapon,
                int seed,
                IWeaponUpgradeDataProvider upgradeData,
                UpgradeCatalog upgradeCatalog,
                IUpgradeSuccessRoll upgradeSuccessRoll)
            {
                PlayerId = playerId;
                EquippedWeapon = equippedWeapon;
                Wallet = new Wallet();
                Loadout = new BlessingLoadout();
                Random = new DeterministicRandom(seed);
                Upgrade = new WeaponUpgradeState(upgradeData, new DeterministicRandom(seed ^ 0x5F3759DF));

                // ⚠️ 같은 Wallet 을 넘긴다. 강화 전용 지갑을 만들면 상점·룰렛과 잔액이 갈려
                //    "강화 화면에서는 살 수 있는데 상점에서는 없는" 골드가 생긴다.
                // ⚠️ 성공 판정 난수는 축복 추첨(Random)과 따로 쥔다. 같은 수열을 나눠 쓰면 강화를 한 번
                //    눌렀는지에 따라 다음 준비 단계의 축복 후보가 바뀐다 (S15P21A606-304).
                Slots = new UpgradeState(
                    upgradeCatalog, UpgradeWeaponResolver.ToWeaponType(equippedWeapon), upgradeSuccessRoll);

                // 경제형 축복(이자 폭발·저점 매수)의 준비 단계 상태. 지갑과 마찬가지로
                // 플레이어 한 명당 하나이고, 매치를 넘지 않는다.
                Blessing = new BlessingEconomyState();
            }

            public int PlayerId { get; }
            public WeaponTag EquippedWeapon { get; set; }
            public Wallet Wallet { get; }
            public BlessingLoadout Loadout { get; }

            /// <summary>
            /// 이 매치에서 후보로 뜬 축복 id — 재추첨·다음 준비 단계에서 다시 나오지 않는다 (S-GD-H78GRS).
            /// 매치 동안 유지되고 <c>BeginMatch</c> 에서 새로 만들어진다.
            /// </summary>
            public HashSet<string> ExposedBlessingIds { get; } = new HashSet<string>(StringComparer.Ordinal);
            public DeterministicRandom Random { get; }

            /// <summary>예치·블라인드 베팅·저점 매수의 소유자. 판정은 전부 이 상태가 한다.</summary>
            public BlessingEconomyState Blessing { get; }

            /// <summary>기존 무기 전용 강화. 온라인 어댑터는 후속 작업에서 <see cref="Slots"/> 로 연결한다.</summary>
            public WeaponUpgradeState Upgrade { get; }

            /// <summary>무기·방어구 두 부위의 강화 (S-GD-A2YDDJ). <b>지갑은 위와 같은 것이다.</b></summary>
            public UpgradeState Slots { get; }

            /// <summary>
            /// 지금 장착한 장신구 (S-GD-YMKKDJ). 없으면 0 이다.
            /// </summary>
            /// <remarks>
            /// ⚠️ <b><see cref="Slots"/> 안에 두지 않는다.</b> 강화는 라운드마다 0 으로 돌아가지만
            /// 장신구는 <b>매치가 끝날 때까지 유지</b>된다 — 같은 자리에 두면
            /// <c>ResetForNextRound</c> 에 같이 쓸려 나간다.
            /// </remarks>
            public int AccessoryItemId { get; set; }

            /// <summary>이번 준비 단계의 드래프트. 전투 중에는 null 이다.</summary>
            public BlessingDraft Draft { get; set; }

            /// <summary>직전 정산에서 소멸한 골드. 화면이 "N G 소멸" 을 띄우는 데 쓴다.</summary>
            public int LastForfeitedGold { get; set; }

            /// <summary>직전 정산에서 이월된 골드.</summary>
            public int LastCarriedGold { get; set; }
        }

        private readonly EconomyConfig economyConfig;
        private readonly BlessingConfig blessingConfig;
        private readonly IReadOnlyList<BlessingDefinition> blessingPool;
        private readonly IWeaponUpgradeDataProvider upgradeData;
        private readonly UpgradeCatalog upgradeCatalog;
        private readonly Func<int, IUpgradeSuccessRoll> upgradeSuccessRollFactory;
        private readonly Dictionary<int, PlayerEconomy> players = new();
        private readonly List<int> playerOrder = new();
        private readonly MatchRoundTally tally = new();

        /// <summary>
        /// <b>직전 라운드까지 정산된</b> 누적 순위 점수. 저점 매수의 하위권 판정
        /// (<see cref="BlessingEconomyState.IsLowerHalf"/>) 이 이것만 본다.
        ///
        /// <para>
        /// 준비 단계를 열 때 <see cref="BuildFinalStandings()"/> 로 한 번 찍고 그 라운드 동안 고정한다 —
        /// 준비 중에 골드가 오르내려도 자격이 흔들리면 "누르는 순간에 따라 되는" 버튼이 된다.
        /// </para>
        /// </summary>
        private readonly List<BlessingScore> blessingStandings = new();

        /// <summary>
        /// 매치 하나를 만든다. 설정과 축복 풀은 매치 동안 바뀌지 않는다.
        /// </summary>
        /// <param name="blessingPool">
        /// 뽑을 수 있는 축복 전체. ⚠️ <b>등급마다 무기별로 최소 <see cref="BlessingConfig.CandidateCount"/> 개</b>가
        /// 없으면 <see cref="BeginPreparation"/> 이 예외를 던진다 — 후보를 채울 수 없기 때문이다.
        /// 목록 자체는 아직 기획 미정이다 (S-GD-S46S28 의 미정 항목).
        /// </param>
        /// <param name="upgradeCatalog">
        /// 무기·방어구 두 부위의 강화 표 (S-GD-A2YDDJ). <b>기본값이 임시 표라 인자를 늘려도
        /// 기존 호출부가 그대로 컴파일된다</b> — 확정 데이터가 들어오면(D-041) 여기로 넣는다.
        /// </param>
        /// <param name="upgradeSuccessRollFactory">
        /// 플레이어별 강화 판정 시드 → 판정기 (S15P21A606-304). <c>null</c> 이면 <see cref="UpgradeSuccessRoll"/>
        /// (시드 고정 난수)다. <b>테스트만 다른 값을 넣는다</b> — 예: <c>_ =&gt; UpgradeSuccessRoll.AlwaysSucceeds</c>.
        /// ⚠️ 이 객체를 만드는 쪽이 곧 판정 권위다 — Host·Local Play·훈련 로컬 세션만 만든다.
        /// </param>
        public MatchEconomyState(
            EconomyConfig economyConfig,
            BlessingConfig blessingConfig,
            IReadOnlyList<BlessingDefinition> blessingPool,
            IWeaponUpgradeDataProvider upgradeData = null,
            UpgradeCatalog upgradeCatalog = null,
            Func<int, IUpgradeSuccessRoll> upgradeSuccessRollFactory = null)
        {
            this.economyConfig = economyConfig ?? throw new ArgumentNullException(nameof(economyConfig));
            this.blessingConfig = blessingConfig ?? throw new ArgumentNullException(nameof(blessingConfig));
            this.blessingPool = blessingPool ?? throw new ArgumentNullException(nameof(blessingPool));
            this.upgradeData = upgradeData ?? new MockWeaponUpgradeDataProvider();
            // 기획 엑셀 upgrades·upgrade_sets 가 정본이다 (S-CLI-QXRTWX). 데이터가 없으면 코드 대역 표다.
            this.upgradeCatalog = upgradeCatalog ?? BettingRoyale.Data.Balance.BalanceCatalogs.Current.Upgrades;
            this.upgradeSuccessRollFactory = upgradeSuccessRollFactory ?? (rollSeed => new UpgradeSuccessRoll(rollSeed));
        }

        /// <summary>총 라운드 수 (S-GD-6H2Q5Q). 설정의 등급 표 길이가 곧 라운드 수다.</summary>
        public int TotalRounds => blessingConfig.DefinedRoundCount;

        /// <summary>1부터 시작하는 현재 라운드. 시작 전에는 0 이다.</summary>
        public int Round { get; private set; }

        public MatchEconomyPhase Phase { get; private set; } = MatchEconomyPhase.NotStarted;

        /// <summary>참가자 수.</summary>
        public int PlayerCount => players.Count;

        /// <summary>라운드 결과 누적 (완료 조건 6).</summary>
        public MatchRoundTally Tally => tally;

        /// <summary>
        /// 이 라운드의 전투가 <b>아직 결과 반영 전</b>인가 — 상자처럼 전투 중에 주는 보상을 받을 수 있는 때다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>단계만 보면 안 된다.</b> 마지막이 아닌 라운드는 결과를 반영한 뒤에도 다음
        /// <see cref="BeginPreparation"/> 까지 <see cref="Phase"/> 가 <c>Combat</c> 으로 남는다. 그 틈에 들어온
        /// 수령에 골드를 주면 라운드 종료가 이긴다는 규칙(S-GD-DFG3VR)이 깨진다.
        /// </remarks>
        public bool IsCombatRoundOpen(int round)
        {
            if (Phase != MatchEconomyPhase.Combat || round <= 0 || round != Round)
            {
                return false;
            }

            foreach (int counted in tally.CountedRounds)
            {
                if (counted == round)
                {
                    return false;
                }
            }

            return true;
        }

        // ── 매치 경계 ─────────────────────────────────────────────────────

        /// <summary>
        /// 새 매치를 시작한다 (S-GD-RFXF8X). 골드와 축복이 <b>전부 0 에서</b> 다시 출발한다.
        ///
        /// <b>같은 인스턴스를 다시 부를 수 있다</b> — 같은 방에서 재대결할 때 상태가 새는지
        /// 확인해야 하고, 새 인스턴스를 만들게 하면 "초기화를 빠뜨렸다" 를 잡을 방법이 없어진다.
        /// </summary>
        /// <param name="participants">참가자별 (플레이어 id, 장착 무기).</param>
        /// <param name="seed">
        /// 축복 추첨 시드. 같은 시드 + 같은 순서 = 같은 결과다 (2PC 검증에 필요).
        /// 플레이어마다 시드를 어긋나게 해서 전원이 같은 후보를 받지 않게 한다.
        /// </param>
        /// <remarks>
        /// 강화 판정 시드는 <paramref name="seed"/> 에서 파생한다 — Local Play·훈련처럼 권위와 화면이 한 기기인
        /// 경로용이다. <b>온라인 Host 는 <see cref="BeginMatch(IReadOnlyList{MatchParticipant}, int, int)"/> 로
        /// 클라이언트가 모르는 시드를 따로 넣는다.</b>
        /// </remarks>
        public void BeginMatch(IReadOnlyList<MatchParticipant> participants, int seed) =>
            BeginMatch(participants, seed, unchecked(seed ^ 0x2545F491));

        /// <summary>
        /// 새 매치를 시작한다. 강화 성공 판정 시드(<paramref name="upgradeRollSeed"/>)를 축복 추첨 시드와 따로 받는다.
        /// </summary>
        /// <param name="upgradeRollSeed">
        /// 강화 성공 판정의 시드 (S15P21A606-304). 온라인 Host 는 <see cref="UpgradeSuccessRoll.CreateHostSeed"/> 를
        /// 넣는다 — 모두가 아는 MatchId 에서 뽑으면 개조 클라이언트가 다음 판정을 미리 계산할 수 있다.
        /// </param>
        public void BeginMatch(IReadOnlyList<MatchParticipant> participants, int seed, int upgradeRollSeed)
        {
            if (participants == null)
            {
                throw new ArgumentNullException(nameof(participants));
            }

            if (participants.Count == 0)
            {
                throw new ArgumentException("참가자가 없습니다.", nameof(participants));
            }

            players.Clear();
            playerOrder.Clear();
            tally.ResetForNewMatch();

            // 지난 매치의 순위표가 남으면 새 매치 1라운드에 하위권 자격이 생긴다 (S-GD-RFXF8X).
            blessingStandings.Clear();

            for (var i = 0; i < participants.Count; i++)
            {
                var participant = participants[i];
                if (players.ContainsKey(participant.PlayerId))
                {
                    throw new ArgumentException(
                        $"플레이어 id {participant.PlayerId} 가 중복됩니다.", nameof(participants));
                }

                // 시드를 플레이어마다 어긋나게 한다. 같은 시드면 전원이 같은 후보를 받는다.
                players[participant.PlayerId] =
                    new PlayerEconomy(
                        participant.PlayerId,
                        participant.EquippedWeapon,
                        seed + (i * 7919),
                        upgradeData,
                        upgradeCatalog,
                        upgradeSuccessRollFactory(unchecked(upgradeRollSeed + (i * 104729))));
                playerOrder.Add(participant.PlayerId);
            }

            Round = 0;
            Phase = MatchEconomyPhase.Combat; // BeginPreparation 이 라운드 1 을 연다
        }

        // ── 라운드 경계 ───────────────────────────────────────────────────

        /// <summary>
        /// 다음 준비 단계를 연다 (완료 조건 1·2). 순서는 <b>이월 정산 → 기본 지급 → 후보 추첨</b> 이다.
        ///
        /// <b>순서가 중요하다</b>: 지급을 먼저 하면 이번 라운드에 받은 골드까지 이월 한도에 걸려
        /// 소멸한다. 1라운드는 정산할 이전 라운드가 없으므로 지급부터다.
        /// </summary>
        /// <returns>열린 라운드 번호.</returns>
        public int BeginPreparation()
        {
            if (Phase == MatchEconomyPhase.NotStarted)
            {
                throw new InvalidOperationException("BeginMatch 를 먼저 불러야 합니다.");
            }

            if (Phase == MatchEconomyPhase.Preparation)
            {
                throw new InvalidOperationException($"이미 라운드 {Round} 준비 단계입니다.");
            }

            if (Round >= TotalRounds)
            {
                throw new InvalidOperationException(
                    $"매치는 {TotalRounds} 라운드입니다 (S-GD-6H2Q5Q). 라운드 {Round + 1} 을 열 수 없습니다.");
            }

            // 정산되지 않은 예치금이 남아 있으면 다음 라운드를 열지 않는다 — 생사를 모르는 채로
            // 넘어가면 예치금이 조용히 사라지거나 조용히 돌아온다. 지갑을 건드리기 <b>전</b>에 막아
            // 절반만 진행된 라운드가 생기지 않게 한다.
            foreach (var playerId in playerOrder)
            {
                var blessing = players[playerId].Blessing;

                if (blessing.InterestDeposited && !blessing.RoundSettled)
                {
                    throw new InvalidOperationException(
                        $"플레이어 {playerId} 의 {blessing.Round}라운드 예치금 {blessing.DepositAmount}G 가 " +
                        "정산되지 않았습니다. 라운드 결과를 먼저 반영합니다.");
                }
            }

            // ⚠️ 이월·지급보다 먼저 찍는다. 순위 점수 자체는 골드를 쓰지 않지만(S-GD-P7SC3Q),
            //    동점 단계가 잔액과 누계를 보므로 지갑을 바꾼 뒤에 찍으면 "직전 라운드가 끝난 시점" 이
            //    아니라 "이번 라운드 지급이 끝난 시점" 의 순위표가 된다.
            CaptureBlessingStandings();

            Round++;

            foreach (var playerId in playerOrder)
            {
                var player = players[playerId];

                if (Round > 1)
                {
                    // 이전 라운드 잔액을 한도까지만 넘긴다 (S-GD-P5RFZM).
                    // 이월 한도는 축복이 바꿀 수 있다 — 탐욕은 없애고, 쇼 미 더 머니는 올린다.
                    var settled = player.Wallet.SettleRound(player.Blessing.CarryOverLimitFor(economyConfig.CarryOverLimit));
                    player.LastCarriedGold = settled.CarriedGold;
                    player.LastForfeitedGold = settled.ForfeitedGold;
                }
                else
                {
                    player.LastCarriedGold = 0;
                    player.LastForfeitedGold = 0;
                }

                // ⚠️ 강화는 라운드마다 0강으로 돌아가고 골드는 환급되지 않는다 (S-GD-A2YDDJ).
                //    준비 시작 경계에서도 초기화해 다음 라운드에 이전 단계가 남지 않게 한다.
                player.Slots.ResetForNextRound();

                player.Wallet.GrantRound(economyConfig);

                // 저점 매수는 누적 순위 하위권에게만 후보로 나온다(S-GD-TMPBEF). 순위표는 위에서 찍었다.
                var standings = blessingStandings;
                var draftPlayerId = playerId;
                player.Draft = BlessingDraft.Begin(
                    blessingConfig,
                    blessingPool,
                    player.EquippedWeapon,
                    Round,
                    player.Loadout,
                    player.Random,
                    offerable: definition => BlessingEconomyState.IsOfferable(definition, draftPlayerId, standings),
                    exposedIds: player.ExposedBlessingIds);

                // 보유 목록은 직전 준비 단계의 선택에서 이미 반영돼 있다(TrySelect·EndPreparation).
                player.Blessing.BeginPreparation(Round, player.Loadout.Acquired);
                player.Blessing.GrantPreparationBonuses(player.Wallet);
            }

            Phase = MatchEconomyPhase.Preparation;
            return Round;
        }

        /// <summary>
        /// 준비 단계를 닫고 전투로 넘어간다.
        ///
        /// <b>아직 고르지 않은 사람은 자동으로 선택된다</b> (S-GD-NK8ZJ7). 준비 시간이 끝나도 화면이
        /// 비어 있으면 그 사람만 축복 없이 전투에 들어가 매치가 기울기 때문이다.
        ///
        /// 무엇을 고르는지는 <see cref="BlessingDraft.SelectAutomatically"/> 가 정한다 —
        /// <b>잠근 후보가 있으면 그 중에서, 없으면 전체에서 무작위</b>다.
        /// </summary>
        /// <returns>자동 선택이 적용된 플레이어 id 목록.</returns>
        public IReadOnlyList<int> EndPreparation()
        {
            RequirePreparation();

            var autoSelected = new List<int>();

            foreach (var playerId in playerOrder)
            {
                var player = players[playerId];
                if (player.Draft is { IsSelected: false })
                {
                    player.Loadout.Add(player.Draft.SelectAutomatically());
                    autoSelected.Add(playerId);
                    RefreshBlessingLoadout(player);
                }

                player.Draft = null;

                // 준비가 닫히면 예치·블라인드 베팅을 받지 않는다.
                player.Blessing.EndPreparation();
            }

            Phase = MatchEconomyPhase.Combat;
            return autoSelected;
        }

        /// <summary>제품 전원 정산. 동일 재시도는 성공하되 강화 초기화는 처음에만 수행한다.</summary>
        public bool TryApplyCompleteRoundResults(int round, IReadOnlyList<RoundOutcome> results)
        {
            if (round != Round || round <= 0 || round > TotalRounds)
            {
                return false;
            }

            bool alreadyCounted = false;
            foreach (int counted in tally.CountedRounds)
            {
                if (counted == round) alreadyCounted = true;
            }

            // ⚠️ 전투가 끝나기 전에 온 결과는 <b>누계를 건드리기 전에</b> 거절한다. 통과시키면
            //    라운드가 집계된 채로 예치금 정산만 빠져(SettleBlessingDeposits 는 전투 중에만 한다)
            //    같은 배치를 다시 보내도 영원히 정산되지 않는다.
            //    이미 집계된 라운드의 재시도는 예외다 — 마지막 라운드는 첫 반영에서 Finished 가 되므로
            //    여기서 막으면 같은 배치의 재전송이 false 가 된다.
            if (!alreadyCounted && Phase != MatchEconomyPhase.Combat)
            {
                return false;
            }

            ApplyKillScoreMultipliers(round);
            if (!tally.TryApplyCompleteRound(round, results, playerOrder))
            {
                return false;
            }

            if (!alreadyCounted)
            {
                // ⚠️ 예치금 정산은 <b>검증을 통과한 이 배치로만</b>, 이월이 일어나는 다음
                //    BeginPreparation 보다 먼저 끝낸다. 거절된 배치(위에서 false)로는 정산하지 않는다 —
                //    생사가 확정되지 않은 채 환급하면 탈락자에게도 돈이 돌아간다.
                SettleBlessingDeposits(round, results);

                foreach (int playerId in playerOrder)
                {
                    players[playerId].Upgrade.ResetAfterCombatRound();
                }

                if (round >= TotalRounds) Phase = MatchEconomyPhase.Finished;
            }

            return true;
        }

        /// <summary>
        /// 라운드 전투 결과를 반영한다 (완료 조건 6). <b>같은 라운드가 두 번 오면 무시한다.</b>
        /// </summary>
        /// <returns>실제로 반영했으면 true. 이미 반영된 라운드면 false.</returns>
        public bool TryApplyRoundResults(int round, IReadOnlyList<RoundOutcome> results)
        {
            // 현재 준비 라운드의 결과는 예치 전에도 거절한다. 먼저 집계하면 이후 선택한 예치가 정산되지 않는다.
            // 라운드를 열기 전(Round 0)의 오프라인 집계와 지난 라운드의 부분 명부 계약은 유지한다.
            // 실제 경기가 시작된 뒤에는 아직 열지 않은 미래 라운드도 먼저 집계할 수 없다.
            if (Round > 0 && (round > Round || (round == Round && Phase != MatchEconomyPhase.Combat)))
            {
                return false;
            }

            ApplyKillScoreMultipliers(round);
            var applied = tally.TryApplyRound(round, results);

            if (applied)
            {
                // 옛 경로는 명부의 일부만 담긴 배치도 받는다. 그래서 <b>실제로 들어온 줄만</b>
                // 정산한다 — 오지 않은 사람의 생사는 여기서 알 수 없다.
                SettleBlessingDeposits(round, results);

                foreach (var playerId in playerOrder)
                {
                    players[playerId].Upgrade.ResetAfterCombatRound();
                    players[playerId].Slots.ResetForNextRound();
                }
            }

            if (applied && round >= TotalRounds)
            {
                Phase = MatchEconomyPhase.Finished;
            }

            return applied;
        }

        /// <summary>
        /// 최종 순위를 매긴다 (Jira S15P21A606-31 완료 조건 6).
        /// 배점은 <c>S-GD-P7SC3Q</c>, 동점 9단계는 <c>S-GD-MNFBV7</c> 다.
        ///
        /// <para>
        /// <b>어댑터는 이것만 부르면 된다</b> — 전투 누적(<see cref="Tally"/>)과 지갑(잔액·누계)을
        /// 여기서 합쳐 <see cref="FinalStandings"/> 에 넘긴다. 결과의 <c>Rank</c> 가 서버에 제출하는
        /// 값이다(<c>docs/server/api.md</c>).
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b>가장 큰 성분인 순위 점수는 전투 경계가 <c>RoundOutcome.Placement</c> 를 채워야 나온다.</b>
        /// 채우지 않으면 그 라운드는 꼴등 점수로 계산된다 (O-025).
        /// </para>
        /// </summary>
        /// <param name="config"><c>null</c> 이면 <see cref="ScoringConfig.Default"/>.</param>
        public FinalStanding[] BuildFinalStandings(ScoringConfig config = null) =>
            BuildFinalStandings(config, null);

        /// <summary>
        /// 최종 순위를 매기되 <b>실제 대기실 입장 순서</b>를 함께 넘긴다 (S-GD-7G8ZTA · S-GD-MQJPFK).
        /// </summary>
        /// <param name="lobbyJoinOrders">
        /// <c>playerId → 1부터의 입장 순서</c>. Host 가 참가 순간에 확정한 값이며
        /// <b><c>PlayerId</c> 정렬로 대신하지 않는다</b>. <c>null</c> 이거나 값이 없으면
        /// 그 사람은 미확정(<see cref="PlayerMatchFacts.UnassignedJoinOrder"/>)으로 들어간다.
        /// </param>
        public FinalStanding[] BuildFinalStandings(
            ScoringConfig config, IReadOnlyDictionary<int, int> lobbyJoinOrders)
        {
            var facts = new PlayerMatchFacts[playerOrder.Count];
            for (var i = 0; i < playerOrder.Count; i++)
            {
                var playerId = playerOrder[i];
                var player = RequirePlayer(playerId);
                var joinOrder = PlayerMatchFacts.UnassignedJoinOrder;
                if (lobbyJoinOrders != null
                    && lobbyJoinOrders.TryGetValue(playerId, out var resolved)
                    && resolved > 0)
                {
                    joinOrder = resolved;
                }

                facts[i] = new PlayerMatchFacts(
                    playerId,
                    tally.GetEntry(playerId),
                    player.Wallet.Gold,
                    player.Wallet.TotalInflow,
                    joinOrder);
            }

            // 인원 N 은 매치 시작 계정 수다 (S-GD-QJA6NY). 이탈해도 playerOrder 에서 빠지지 않는다.
            return FinalStandings.Resolve(facts, config, playerOrder.Count);
        }

        // ── 준비 단계 조작 ────────────────────────────────────────────────

        /// <summary>후보 잠금을 켜고 끈다 (S-GD-S46S28). 무료지만 최대 2개다.</summary>
        public bool TryToggleLock(int playerId, int candidateIndex)
        {
            RequirePreparation();
            var draft = RequireDraft(playerId);
            return candidateIndex >= 0
                   && candidateIndex < draft.Candidates.Count
                   && draft.TryToggleLock(candidateIndex);
        }

        /// <summary>잠그지 않은 후보를 다시 뽑는다 (S-GD-SHJGNB). 유료다.</summary>
        public RerollResult TryReroll(int playerId)
        {
            RequirePreparation();
            var player = RequirePlayer(playerId);
            return RequireDraft(playerId).TryReroll(player.Wallet);
        }

        /// <summary>준비 단계에서 블라인드 베팅을 수락한다. 보너스 지급과 수락 기록은 원자적으로 처리한다.</summary>
        public bool TryAcceptBlessingBlindBet(int playerId)
        {
            RequirePreparation();
            var player = RequirePlayer(playerId);
            return player.Blessing.TryAcceptBlindBet(player.Wallet);
        }

        /// <summary>이자 폭발 예치를 거절한다 (S-GD-XBP3VA). 돈이 오가지 않으므로 지갑을 건드리지 않는다.</summary>
        public bool TryDeclineBlessingDeposit(int playerId)
        {
            RequirePreparation();
            return RequirePlayer(playerId).Blessing.DeclineDeposit();
        }

        /// <summary>블라인드 베팅을 거절한다.</summary>
        public bool TryDeclineBlessingBlindBet(int playerId)
        {
            RequirePreparation();
            return RequirePlayer(playerId).Blessing.DeclineBlindBet();
        }

        /// <summary>
        /// 현재 장착 주무기의 다음 단계를 판정한다. 비용 차감과 상태 변경은 원자적으로 일어난다.
        ///
        /// <para>
        /// <b>옛 로컬 호출을 위한 호환 경로다.</b> 온라인 연결은 <see cref="TryUpgradeSlot"/> 를 사용한다 —
        /// 두 상태는 지갑만 공유하고 단계는 따로 센다. 옛 테스트와 로컬 플레이가 이것을 부르고 있어
        /// 남겨 둔다.
        /// </para>
        /// </summary>
        public WeaponUpgradeAttemptResult TryUpgradeWeapon(int playerId)
        {
            RequirePreparation();
            var player = RequirePlayer(playerId);
            return player.Upgrade.TryUpgrade(player.Wallet);
        }

        // ── 두 부위 강화 (S-GD-A2YDDJ · S-GD-Z9CJ4H) ──────────────────────

        /// <summary>
        /// 한 부위를 한 단계 올린다. <b>골드 확인·차감과 단계 변경이 한 덩어리다.</b>
        ///
        /// <para>
        /// ⚠️ <b>이 메서드는 단계(Phase)도 위치도 보지 않는다.</b> 그 판정은
        /// <c>UpgradeRequestGate</c> 가 이미 하고, 여기서 또 하면 판정이 두 곳으로 갈린다 —
        /// <see cref="TrySpend"/> 와 같은 규칙이다. <b>게이트를 거치지 않고 직접 부르지 않는다.</b>
        /// </para>
        ///
        /// <para>모르는 부위는 <see cref="UpgradeState.TryUpgrade"/> 가 던진다 — 게이트가 먼저 거른다.</para>
        ///
        /// <para>
        /// <see cref="UpgradeAttemptResult.Failed"/> 는 <b>확률 실패</b>다 — 골드는 이미 나갔고 단계는 그대로다
        /// (S15P21A606-304). 판정 난수는 이 객체가 쥐고 있으므로 <b>이 객체를 돌리는 쪽이 곧 판정 권위</b>다.
        /// </para>
        /// </summary>
        public UpgradeAttemptResult TryUpgradeSlot(int playerId, UpgradeSlot slot)
        {
            var player = RequirePlayer(playerId);
            return player.Slots.TryUpgrade(slot, player.Wallet);
        }

        /// <summary>그 부위의 지금 단계.</summary>
        public int GetUpgradeLevel(int playerId, UpgradeSlot slot) =>
            RequirePlayer(playerId).Slots.LevelOf(slot);

        /// <summary>세트 단계 — <b>두 부위 중 낮은 쪽</b>이다.</summary>
        public int GetUpgradeSetLevel(int playerId) => RequirePlayer(playerId).Slots.SetLevel;

        /// <summary>강화 표를 고르는 근거가 되는 무기. 스폰 전에는 축복 태그에서 옮긴 값이다.</summary>
        public WeaponType GetEquippedWeaponType(int playerId) =>
            RequirePlayer(playerId).Slots.EquippedWeapon;

        /// <summary>
        /// <b>실제로 들고 있는 무기를 알려준다</b> — 전투원이 스폰된 뒤의 정답이다
        /// (<c>ICombatActionDriver.EquippedWeapon</c>).
        ///
        /// <para>
        /// 참가자가 아니면 <c>false</c> 다. <b>던지지 않는다</b> — 부르는 쪽이 전투 경계라
        /// 방을 떠난 전투원이 한 프레임 늦게 알려오는 것이 정상이고, 그때 예외가 나면
        /// 그 프레임에 방 전체가 멈춘다.
        /// </para>
        /// </summary>
        public bool TrySetEquippedWeaponType(int playerId, WeaponType weapon)
        {
            if (!players.TryGetValue(playerId, out var player))
            {
                return false;
            }

            player.Slots.EquippedWeapon = weapon;
            return true;
        }

        /// <summary>
        /// 지금 강화 단계의 스탯 기여를 시트에 얹는다 (S-GD-A2YDDJ · S-GD-SH74SC).
        /// <b>축복과 같은 시트·같은 합산 규칙</b>이고, 출처별로 갈아 끼우므로 여러 번 불러도 안전하다.
        /// </summary>
        public void ApplyUpgradeToSheet(int playerId, StatSheet sheet)
        {
            PlayerEconomy player = RequirePlayer(playerId);
            player.Slots.ApplyTo(sheet);
            ApplyItemsToSheet(player, sheet);
        }

        /// <summary>
        /// 상점 아이템의 스탯 기여를 시트에 얹는다 (S-GD-YMKKDJ).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>강화와 같은 자리에서 같이 얹는 이유.</b> 축복 시트를 다시 만드는 곳이
        /// <c>BlessingRuntime.Rebuild</c> 하나뿐이고, 그 <c>compose</c> 콜백이 여기로 온다.
        /// 따로 부르는 경로를 만들면 시트를 다시 만들 때마다 한쪽을 빠뜨린다.
        /// </para>
        /// <para>
        /// <b>소모품은 여기 오지 않는다.</b> 신속 물약은 마신 본인의 이동 컴포넌트에 직접 걸린다
        /// (<c>EquipmentInventoryBehaviour</c>) — 체력 물약이 그렇듯이. 경제를 거치지 않으므로
        /// 경제가 시간을 들고 있을 이유도 없다.
        /// </para>
        /// </remarks>
        private static void ApplyItemsToSheet(PlayerEconomy player, StatSheet sheet)
        {
            // 힘의 반지는 근접 +5 / 원거리 +3 이라 지금 든 무기를 봐야 한다.
            bool ranged = player.Slots.EquippedWeapon == WeaponType.Bow;
            ShopItemEffectCatalog.ApplyAccessoryToSheet(sheet, player.AccessoryItemId, ranged);
        }

        /// <summary>장신구를 장착한다. 한 개만 지니므로 이전 것은 사라진다 (S-GD-B7Q44N).</summary>
        /// <returns>밀려난 장신구의 itemId. 없었으면 0.</returns>
        public int SetEquippedAccessory(int playerId, int accessoryItemId)
        {
            PlayerEconomy player = RequirePlayer(playerId);
            int displaced = player.AccessoryItemId;
            player.AccessoryItemId = accessoryItemId;
            return displaced == accessoryItemId ? 0 : displaced;
        }

        /// <summary>지금 장착한 장신구. 없으면 0.</summary>
        public int GetEquippedAccessory(int playerId) => RequirePlayer(playerId).AccessoryItemId;

        /// <summary>지금 재추첨이 가능한가. 부작용 없이 사유까지 돌려준다.</summary>
        public RerollResult GetRerollAvailability(int playerId)
        {
            RequirePreparation();
            var player = RequirePlayer(playerId);
            return RequireDraft(playerId).GetRerollAvailability(player.Wallet);
        }

        /// <summary>축복 하나를 선택해 이번 준비 단계를 끝낸다 (S-GD-S46S28).</summary>
        /// <returns>선택한 축복. 이미 선택했거나 번호가 잘못됐으면 null.</returns>
        public BlessingDefinition TrySelect(int playerId, int candidateIndex)
        {
            RequirePreparation();
            var draft = RequireDraft(playerId);

            if (draft.IsSelected || candidateIndex < 0 || candidateIndex >= draft.Candidates.Count)
            {
                return null;
            }

            var player = RequirePlayer(playerId);
            var selected = draft.Select(candidateIndex);
            player.Loadout.Add(selected);
            RefreshBlessingLoadout(player);
            return selected;
        }

        /// <summary>선택 제한시간 만료 시 한 명만 확정한다. 준비 활동 시간은 계속된다.</summary>
        public BlessingDefinition TrySelectAutomatically(int playerId)
        {
            RequirePreparation();
            var player = RequirePlayer(playerId);
            if (player.Draft == null || player.Draft.IsSelected)
            {
                return null;
            }

            var selected = player.Draft.SelectAutomatically();
            player.Loadout.Add(selected);
            RefreshBlessingLoadout(player);
            return selected;
        }

        // ── 경제형 축복 (이자 폭발 · 저점 매수) ───────────────────────────

        /// <summary>
        /// 한 플레이어의 경제형 축복 상태. 화면·어댑터는 <b>읽기만</b> 한다 —
        /// 조작은 아래 <c>Try~</c> 들이 단계와 참가자를 확인한 뒤에만 통과시킨다.
        /// </summary>
        /// <remarks>참가자가 아니면 던진다 — <see cref="GetGold"/> 와 같은 조회 계약이다.</remarks>
        public BlessingEconomyState GetBlessingEconomy(int playerId) => RequirePlayer(playerId).Blessing;

        /// <summary>
        /// 이자 폭발의 예치를 접수한다. 금액과 환급 조건은 카탈로그가 정하고 차감은 지갑이 한다.
        ///
        /// <para>
        /// <b>거부할 때 던지지 않는다.</b> 준비 화면의 버튼이 그대로 들어오는 경로라 단계가 바뀐 뒤
        /// 늦게 도착한 클릭이나 이미 방을 떠난 id 가 정상적으로 올 수 있다.
        /// </para>
        /// </summary>
        /// <returns>
        /// 예치했으면 <c>true</c>. 준비 단계가 아니거나 참가자가 아니거나 <b>아직 축복을 고르지 않았거나</b>
        /// 이자 폭발이 없거나 이미 예치했거나 골드가 모자라면 <c>false</c>.
        /// </returns>
        public bool TryDepositBlessing(int playerId) =>
            TryGetChoosingDonePlayer(playerId, out var player)
            && player.Blessing.TryDeposit(player.Wallet);

        /// <summary>상자 보상 등으로 골드를 얻는다. 전투 중에도 된다.</summary>
        public void Earn(int playerId, int amount) => RequirePlayer(playerId).Wallet.Earn(amount);

        /// <summary>
        /// 상점 구매처럼 <b>재추첨이 아닌 지출</b>로 골드를 쓴다 (S-CLI-GRZGBK).
        /// 잔액이 모자라면 <b>아무것도 바꾸지 않고</b> <c>false</c> 를 돌려준다.
        ///
        /// <para>
        /// ⚠️ <b>이 메서드는 단계를 보지 않는다.</b> 전투 중에 불러도 통과한다 — 단계 판정은
        /// 부르는 쪽(<c>ShopRequestGate</c>)이 이미 하고, 여기서 또 하면 판정이 두 곳으로 갈린다.
        /// 게이트를 거치지 않고 직접 부르지 않는다.
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b>환불용으로 <see cref="Earn"/> 을 쓰면 안 된다.</b> 그쪽은
        /// <c>Wallet.TotalInflow</c>(매치 동안 모은 골드 누계)를 함께 올리는데, 그 값이
        /// 최종 순위 동점 처리 5단계다 (S-GD-MNFBV7). 실제로 벌지 않은 골드가 순위 근거에 섞이고
        /// 테스트도 컴파일러도 못 잡는다. 그래서 상점은 <b>환불이 필요 없는 순서</b>로 짜여 있다 —
        /// 지급이 성공한 뒤에만 차감한다.
        /// </para>
        /// </summary>
        public bool TrySpend(int playerId, int amount) => RequirePlayer(playerId).Wallet.TrySpend(amount);

        /// <summary>
        /// <b>낸 돈을 그대로 되돌린다</b> — 돌지 않은 룰렛 판의 베팅 환불 (S-GD-7VKZA5).
        ///
        /// <para>
        /// <see cref="Earn"/> 과 달리 <c>Wallet.TotalInflow</c> 를 올리지 않는다. 이유는 바로 위
        /// <see cref="TrySpend"/> 주석과 <c>Wallet.Refund</c> 에 있다 — 요약하면 <b>환불은 번 것이
        /// 아니고</b>, 누계에 섞으면 최종 순위 동점 처리가 조용히 틀린다.
        /// </para>
        ///
        /// <para>
        /// <b>지출을 정확히 되돌릴 때만 쓴다.</b> 새로 생긴 골드(적중 상금의 초과분)는
        /// <see cref="Earn"/> 이다.
        /// </para>
        /// </summary>
        public void Refund(int playerId, int amount) => RequirePlayer(playerId).Wallet.Refund(amount);

        // ── 조회 ─────────────────────────────────────────────────────────

        public int GetGold(int playerId) => RequirePlayer(playerId).Wallet.Gold;

        /// <summary>
        /// 참가자의 <b>진짜 지갑</b>. 제품 타입(<see cref="BlessingEconomyState.TryDeposit"/> ·
        /// <see cref="BlessingDraft.TryReroll"/>)에 <b>같은 잔액</b>을 넘겨야 하는 자리에서만 쓴다.
        ///
        /// <para>
        /// ⚠️ <b><c>internal</c> 인 것이 계약이다.</b> 지갑을 공개하면 게이트를 거치지 않은 차감이
        /// 생기고, Host 와 클라이언트의 잔액이 갈렸을 때 어디서 갈렸는지 추적할 수 없다
        /// (<see cref="Wallet"/> 이 setter 를 두지 않는 것과 같은 이유다). 잔액을 <b>대입하는</b>
        /// 경로는 여기에도 없다 — 돌려주는 것은 조작 메서드만 가진 그 지갑 자체다.
        /// </para>
        /// </summary>
        internal Wallet GetParticipantWallet(int playerId) => RequirePlayer(playerId).Wallet;

        /// <summary>
        /// 이 매치 동안 지갑에 <b>들어온 골드의 누계</b>. <b>쓰거나 이월로 소멸해도 줄지 않는다.</b>
        ///
        /// <para>
        /// 최종 순위 동점 처리 5단계가 이 값을 본다 (S-GD-MNFBV7). 남은 잔액인
        /// <see cref="GetGold"/> 와 <b>방향이 반대인 것은 의도다</b> — 정본 설명은
        /// <c>Wallet.TotalInflow</c> 에 있다.
        /// </para>
        /// </summary>
        public int GetTotalInflow(int playerId) => RequirePlayer(playerId).Wallet.TotalInflow;
        public Contracts.WeaponUpgradeStateChanged BuildWeaponUpgradeState(int playerId, bool isMine)
        {
            var player = RequirePlayer(playerId);
            bool hasNext = player.Upgrade.TryGetNextLevel(out WeaponUpgradeLevelData next);
            Contracts.WeaponUpgradeRejectReason blockedBy;
            if (Phase != MatchEconomyPhase.Preparation)
            {
                blockedBy = Phase == MatchEconomyPhase.Combat
                    ? Contracts.WeaponUpgradeRejectReason.PreparationClosed
                    : Contracts.WeaponUpgradeRejectReason.NotPreparationRound;
            }
            else if (!hasNext)
            {
                blockedBy = player.Upgrade.Level >= upgradeData.MaxLevel
                    ? Contracts.WeaponUpgradeRejectReason.MaxLevelReached
                    : Contracts.WeaponUpgradeRejectReason.DataUndefined;
            }
            else
            {
                blockedBy = player.Wallet.Gold < next.CostGold
                    ? Contracts.WeaponUpgradeRejectReason.NotEnoughGold
                    : Contracts.WeaponUpgradeRejectReason.None;
            }

            return new Contracts.WeaponUpgradeStateChanged(
                playerId,
                isMine,
                Round,
                player.Upgrade.Level,
                upgradeData.MaxLevel,
                player.Upgrade.AttackPowerBonusPercent,
                player.Wallet.Gold,
                hasNext,
                hasNext ? next.CostGold : 0,
                hasNext ? next.SuccessRatePercent : 0,
                blockedBy);
        }

        /// <summary>
        /// 두 부위의 강화 상태 전부 (S-GD-A2YDDJ). 화면은 이 하나로 다시 그려진다.
        ///
        /// <para>
        /// ⚠️ <b>구역 판정(<c>NotInRange</c>)은 여기에 없다.</b> 도메인은 위치를 모른다 —
        /// <c>UpgradeRequestGate.BuildState</c> 가 <see cref="Contracts.UpgradeStateChanged.WithBlockedIfOpen"/>
        /// 로 덧씌운다. <b>그래서 이 값을 그대로 화면에 보내면 구역 밖에서도 버튼이 켜진다.</b>
        /// </para>
        /// </summary>
        public Contracts.UpgradeStateChanged BuildUpgradeState(int playerId, bool isMine)
        {
            var player = RequirePlayer(playerId);
            var slots = player.Slots;

            ResolveSlot(player, UpgradeSlot.Weapon, out bool weaponHasNext, out int weaponCost, out var weaponBlocked);
            ResolveSlot(player, UpgradeSlot.Armor, out bool armorHasNext, out int armorCost, out var armorBlocked);

            return new Contracts.UpgradeStateChanged(
                playerId,
                isMine,
                Round,
                slots.EquippedWeapon,
                slots.WeaponLevel,
                slots.MaxLevelOf(UpgradeSlot.Weapon),
                slots.ArmorLevel,
                slots.MaxLevelOf(UpgradeSlot.Armor),
                slots.SetLevel,
                player.Wallet.Gold,
                weaponHasNext,
                weaponCost,
                weaponBlocked,
                armorHasNext,
                armorCost,
                armorBlocked,
                slots.NextSuccessRatePercent(UpgradeSlot.Weapon),
                slots.NextSuccessRatePercent(UpgradeSlot.Armor));
        }

        /// <summary>
        /// 한 부위가 지금 올릴 수 있는 상태인지와 그 비용.
        ///
        /// <b>여기의 판정과 <c>UpgradeRequestGate.HandleUpgrade</c> 의 판정은 같은 순서를 본다.</b>
        /// 어긋나면 "밝게 켜져 있는데 눌리지 않는 버튼" 이 생긴다.
        /// </summary>
        private void ResolveSlot(
            PlayerEconomy player,
            UpgradeSlot slot,
            out bool hasNext,
            out int costGold,
            out Contracts.UpgradeRejectReason blockedBy)
        {
            hasNext = player.Slots.TryGetNextCost(slot, out costGold);

            if (Phase != MatchEconomyPhase.Preparation)
            {
                blockedBy = Contracts.UpgradeRejectReason.NotPreparation;
                return;
            }

            if (!player.Slots.HasTrack(slot))
            {
                // 표가 없는 무기다 — 데이터가 없는 것이지 조작이 잘못된 것이 아니다.
                blockedBy = Contracts.UpgradeRejectReason.NoTrack;
                return;
            }

            if (!hasNext)
            {
                blockedBy = Contracts.UpgradeRejectReason.MaxLevelReached;
                return;
            }

            blockedBy = player.Wallet.Gold < costGold
                ? Contracts.UpgradeRejectReason.NotEnoughGold
                : Contracts.UpgradeRejectReason.None;
        }

        public IReadOnlyList<BlessingDefinition> GetAcquired(int playerId) =>
            RequirePlayer(playerId).Loadout.Acquired;

        /// <summary>
        /// 이번 준비 단계에서 이미 축복을 골랐는가. 고르지 않았거나 준비 단계가 아니면 <c>false</c>.
        ///
        /// <b>거부 사유를 가르는 데 쓴다</b> — 잠금·선택이 실패했을 때 "이미 골랐다" 와
        /// "그런 후보가 없다" 를 구분하려면 이 값이 필요하다. 없으면 부르는 쪽이 사유를 추측하게 되고,
        /// 화면에 틀린 안내가 뜬다.
        /// </summary>
        public bool IsSelectionDone(int playerId) =>
            RequirePlayer(playerId).Draft?.IsSelected ?? false;

        /// <summary>이번 준비 단계의 후보. 준비 단계가 아니면 빈 목록이다.</summary>
        public IReadOnlyList<BlessingCandidate> GetCandidates(int playerId) =>
            RequirePlayer(playerId).Draft?.Candidates ?? Array.Empty<BlessingCandidate>();

        /// <summary>
        /// 화면이 <b>계산 없이</b> 그릴 수 있는 한 플레이어의 현재 상태 (완료 조건: 계약 단일화).
        /// 어댑터가 이걸 그대로 이벤트에 실어 보낸다.
        /// </summary>
        public PlayerEconomySnapshot BuildSnapshot(int playerId)
        {
            var player = RequirePlayer(playerId);
            var draft = player.Draft;

            return new PlayerEconomySnapshot(
                playerId: playerId,
                round: Round,
                phase: Phase,
                gold: player.Wallet.Gold,
                carriedGold: player.LastCarriedGold,
                forfeitedGold: player.LastForfeitedGold,
                acquiredCount: player.Loadout.Count,
                remainingLockCount: draft?.RemainingLockCount ?? 0,
                rerollCount: draft?.RerollCount ?? 0,
                rerollBlockedBy: draft == null
                    ? RerollResult.AlreadySelected
                    : draft.GetRerollAvailability(player.Wallet));
        }

        /// <summary>참가자 id 목록. 입장 순서를 유지한다.</summary>
        public IReadOnlyList<int> PlayerIds => playerOrder;

        // ── 계약 이벤트 조립 (Jira S15P21A606-31 "명령·이벤트·스냅샷의 단일 계약 유지") ──
        //
        // 어댑터는 아래 메서드가 만든 것을 EventBus 로 그대로 흘리기만 한다.
        // 조립을 어댑터에 맡기면 "후보를 뷰로 바꾸는 규칙" 과 "재추첨 비용을 읽는 순서" 가
        // Network 와 Fake 양쪽에 생기고, 티켓이 금지한 계산 복제가 된다.
        // 실제로 이 프로젝트는 이미 그 대가를 치렀다 — 같은 이름의 이벤트가 두 곳에 생겼고
        // 발행과 구독이 영원히 만나지 않았다 (docs/client/ui-domain-contract.md §10).

        /// <summary>
        /// 준비 화면이 통째로 그려지는 데 필요한 전부를 담아 만든다 (축5 -37).
        /// <paramref name="isMine"/> 은 어댑터만 알 수 있으므로 받는다 (규약 §7).
        /// </summary>
        /// <remarks>준비 단계가 아니면 후보가 빈 배열이다 — 화면은 그걸로 "준비 중 아님" 을 안다.</remarks>
        public Contracts.BlessingOfferChanged BuildBlessingOffer(int playerId, bool isMine)
        {
            var player = RequirePlayer(playerId);
            var draft = player.Draft;

            var candidates = draft == null
                ? Array.Empty<Contracts.BlessingCandidateView>()
                : BuildCandidateViews(draft);

            var hasCost = draft != null && draft.TryGetCurrentRerollCost(out _);
            var currentCost = 0;
            var previewCost = 0;
            if (draft != null)
            {
                draft.TryGetCurrentRerollCost(out currentCost);
                draft.TryGetRerollCostPreview(out previewCost);
            }

            return new Contracts.BlessingOfferChanged(
                playerId: playerId,
                isMine: isMine,
                round: Round,
                candidates: candidates,
                rerollCount: draft?.RerollCount ?? 0,
                remainingLockCount: draft?.RemainingLockCount ?? 0,
                currentRerollCost: currentCost,
                nextRerollCostPreview: previewCost,
                hasRerollCost: hasCost,
                rerollBlockedBy: draft == null
                    ? RerollResult.AlreadySelected
                    : draft.GetRerollAvailability(player.Wallet),
                acquired: BuildAcquiredViews(player));
        }

        /// <summary>골드 변화를 알린다. 잔액과 이월 한도를 함께 실어 한 이벤트로 화면이 완성되게 한다.</summary>
        public Contracts.GoldChanged BuildGoldChanged(
            int playerId, bool isMine, int delta, Contracts.GoldChangeReason reason)
        {
            var player = RequirePlayer(playerId);
            var limit = player.Blessing.CarryOverLimitFor(economyConfig.CarryOverLimit);

            return new Contracts.GoldChanged(
                playerId: playerId,
                isMine: isMine,
                gold: player.Wallet.Gold,
                delta: delta,
                reason: reason,
                // ⚠️ limit.Gold 는 무제한일 때 예외를 던진다. 반드시 IsUnlimited 를 먼저 본다.
                carryOverCap: limit.IsUnlimited ? 0 : limit.Gold,
                isCarryOverUnlimited: limit.IsUnlimited);
        }

        /// <summary>
        /// 직전 라운드 정산 결과를 알린다 (S-GD-P5RFZM). 이월액과 소멸액을 함께 싣는다.
        ///
        /// <para>
        /// <b>1라운드 준비에서는 <c>false</c> 를 돌려준다.</b> 정산할 이전 라운드가 없기 때문이다.
        /// 그래서 <c>Build~</c> 가 아니라 <c>Try~</c> 다 — <b>"안 오는 경우가 있다" 를 타입에 드러낸다.</b>
        /// 항상 이벤트를 내면 화면이 "0G 이월" 을 정산 결과로 그리게 된다.
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b><see cref="Contracts.RoundGoldSettled.Round"/> 는 <c>Round - 1</c> 이다.</b>
        /// 정산은 <see cref="BeginPreparation"/> 안에서 <see cref="Round"/> 를 올린 <b>뒤</b>에 일어나므로,
        /// 그대로 실으면 2라운드 준비에서 "2라운드 정산" 이 되는데 그 돈은 <b>1라운드에서 남은 것</b>이다.
        /// 계약이 약속한 것은 "<b>방금 끝난</b> 라운드" 다.
        /// </para>
        /// </summary>
        /// <param name="settled">이번 준비에서 정산이 일어났을 때만 채워진다.</param>
        /// <returns>정산이 있었으면 <c>true</c>.</returns>
        public bool TryBuildRoundGoldSettled(int playerId, bool isMine, out Contracts.RoundGoldSettled settled)
        {
            var player = RequirePlayer(playerId);

            if (Round <= 1)
            {
                settled = default;
                return false;
            }

            settled = new Contracts.RoundGoldSettled(
                playerId: playerId,
                isMine: isMine,
                round: Round - 1,
                carriedGold: player.LastCarriedGold,
                forfeitedGold: player.LastForfeitedGold);

            return true;
        }

        /// <summary>축복 선택 결과를 알린다.</summary>
        public Contracts.BlessingSelected BuildBlessingSelected(
            int playerId, bool isMine, BlessingDefinition selected)
        {
            if (selected == null)
            {
                throw new ArgumentNullException(nameof(selected));
            }

            return new Contracts.BlessingSelected(
                playerId: playerId,
                isMine: isMine,
                round: Round,
                blessingId: selected.Id,
                acquiredCount: RequirePlayer(playerId).Loadout.Count);
        }

        private static Contracts.BlessingCandidateView[] BuildCandidateViews(BlessingDraft draft)
        {
            var candidates = draft.Candidates;
            var views = new Contracts.BlessingCandidateView[candidates.Count];

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                views[i] = new Contracts.BlessingCandidateView(
                    candidateIndex: i,
                    blessingId: candidate.Blessing.Id,
                    displayName: candidate.Blessing.DisplayName,
                    description: candidate.Blessing.Description,
                    tag: candidate.Blessing.Tag,
                    tier: candidate.Blessing.Tier,
                    isLocked: candidate.IsLocked);
            }

            return views;
        }

        private static Contracts.BlessingCandidateView[] BuildAcquiredViews(PlayerEconomy player)
        {
            var acquired = player.Loadout.Acquired;
            var views = new Contracts.BlessingCandidateView[acquired.Count];

            for (var i = 0; i < acquired.Count; i++)
            {
                var blessing = acquired[i];
                views[i] = new Contracts.BlessingCandidateView(
                    // 획득 목록은 후보가 아니므로 선택할 수 없다. -1 이 "이 항목에는 명령을 보내지 않는다" 는 표시다.
                    candidateIndex: -1,
                    blessingId: blessing.Id,
                    displayName: blessing.DisplayName,
                    description: blessing.Description,
                    tag: blessing.Tag,
                    tier: blessing.Tier,
                    isLocked: false);
            }

            return views;
        }

        // ── 내부 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 그 라운드의 킬 점수 배율(저점 매수)을 누적기에 알린다(S-GD-TMPBEF). 누적기가 그 라운드를 처음 반영할 때만
        /// 쓰이므로 재전송마다 불러도 점수가 두 번 붙지 않는다.
        /// </summary>
        private void ApplyKillScoreMultipliers(int round)
        {
            foreach (var playerId in playerOrder)
            {
                tally.SetKillScoreMultiplier(round, playerId, players[playerId].Blessing.KillScoreMultiplierFor(round));
            }
        }

        /// <summary>
        /// 직전까지 정산된 라운드의 누적 순위 점수를 찍는다. 1라운드를 열 때는 비워 둔다 —
        /// <b>시작하자마자 전원이 하위권이 되는 것</b>을 막는다.
        /// </summary>
        /// <remarks>
        /// 결과가 아직 반영되지 않은 라운드는 점수에 들어가지 않는다. 그러면 전원이 동점이 되고
        /// 동점자는 같은 답을 받으므로(<see cref="BlessingEconomyState.IsLowerHalf"/>) 아무도 자격을 얻지 않는다.
        /// </remarks>
        private void CaptureBlessingStandings()
        {
            blessingStandings.Clear();

            if (Round < 1)
            {
                return;
            }

            var count = playerOrder.Count;

            if (count < ScoringConfig.MinPlayerCount || count > ScoringConfig.Default.MaxPlayerCount)
            {
                // 배점표가 감당하지 못하는 인원이다. 순위 점수를 만들 수 없으므로 자격도 없다 —
                // 1인 매치(테스트·검증용)가 여기로 온다.
                return;
            }

            var standings = BuildFinalStandings();

            for (var i = 0; i < standings.Length; i++)
            {
                blessingStandings.Add(new BlessingScore(standings[i].PlayerId, standings[i].Score));
            }
        }

        /// <summary>
        /// 새로 고른 축복을 경제형 축복 상태에 반영한다. <b>이번 라운드의 예치·블라인드 베팅 기록은 남는다.</b>
        /// </summary>
        private void RefreshBlessingLoadout(PlayerEconomy player)
        {
            player.Blessing.UpdateLoadout(player.Loadout.Acquired);
            // 고른 순간 받는 골드(쇼 미 더 머니·탐욕·저점 매수). 같은 축복으로는 한 번뿐이다.
            player.Blessing.GrantAcquisitionBonuses(player.Wallet);
        }

        /// <summary>
        /// 전투 중에 축복이 번 골드를 지갑에 넣는다 — 사냥꾼 표식 처치, 이자 폭발의 이자·처치·1등 보너스.
        /// </summary>
        /// <returns>넣었으면 true. 모르는 플레이어거나 금액이 0 이하면 false.</returns>
        public bool TryGrantBlessingGold(int playerId, int amount)
        {
            if (amount <= 0 || !players.TryGetValue(playerId, out var player))
            {
                return false;
            }

            player.Wallet.Earn(amount);
            return true;
        }

        /// <summary>
        /// 룰렛에서 잃은 금액의 일부를 돌려준다(룰렛 환급). 번 돈이 아니라 되돌려 받는 돈이라 환불로 넣는다.
        /// </summary>
        /// <returns>돌려준 골드. 축복이 없거나 잃지 않았으면 0.</returns>
        public int RefundRouletteLoss(int playerId, int loss)
        {
            if (loss <= 0 || !players.TryGetValue(playerId, out var player))
            {
                return 0;
            }

            int refund = (int)Math.Floor(loss * (double)player.Blessing.RouletteRefundFraction);
            if (refund <= 0)
            {
                return 0;
            }

            player.Wallet.Refund(refund);
            return refund;
        }

        /// <summary>
        /// 예치금을 정산한다 — <b>들어온 줄에 적힌 생사 그대로</b>다. 추측하지 않는다.
        /// </summary>
        /// <remarks>
        /// 정산은 전투가 끝난 뒤(준비가 닫힌 뒤) 현재 라운드에 대해서만 한다. 그 밖의 호출은
        /// 순서가 어긋났다는 뜻이라 상태를 건드리지 않고 넘긴다 — 도메인 상태를 예외로 터뜨리면
        /// 재전송 한 번에 방 전체가 멈춘다.
        /// </remarks>
        private void SettleBlessingDeposits(int round, IReadOnlyList<RoundOutcome> results)
        {
            if (results == null || round != Round || Phase != MatchEconomyPhase.Combat)
            {
                return;
            }

            for (var i = 0; i < results.Count; i++)
            {
                var outcome = results[i];

                if (!players.TryGetValue(outcome.PlayerId, out var player))
                {
                    continue;
                }

                // 두 번째 호출은 0 을 돌려주고 아무것도 바꾸지 않는다 (BlessingEconomyState.RoundSettled).
                player.Blessing.SettleRound(round, outcome.Survived, player.Wallet);
            }
        }

        /// <summary>
        /// 준비 단계의 계약 조작(예치)을 받을 수 있는 플레이어인가.
        ///
        /// <para>
        /// <b>축복 선택을 마쳐야 한다.</b> 준비 화면은 선택이 끝난 뒤에 열리므로
        /// (<see cref="IsSelectionDone"/>) 선택 전에 들어온 명령은 화면 상태와 어긋난 것이다.
        /// </para>
        /// </summary>
        private bool TryGetChoosingDonePlayer(int playerId, out PlayerEconomy player)
        {
            player = null;

            if (Phase != MatchEconomyPhase.Preparation || !players.TryGetValue(playerId, out var found))
            {
                return false;
            }

            if (found.Draft is not { IsSelected: true })
            {
                return false;
            }

            player = found;
            return true;
        }

        private void RequirePreparation()
        {
            if (Phase != MatchEconomyPhase.Preparation)
            {
                throw new InvalidOperationException(
                    $"준비 단계에서만 할 수 있습니다. 지금은 {Phase} 입니다.");
            }
        }

        private PlayerEconomy RequirePlayer(int playerId) =>
            players.TryGetValue(playerId, out var player)
                ? player
                : throw new ArgumentOutOfRangeException(
                    nameof(playerId), playerId, "이 매치의 참가자가 아닙니다.");

        private BlessingDraft RequireDraft(int playerId) =>
            RequirePlayer(playerId).Draft
            ?? throw new InvalidOperationException($"플레이어 {playerId} 의 준비 단계 드래프트가 없습니다.");

        public override string ToString() =>
            $"{Phase} 라운드 {Round}/{TotalRounds} · {players.Count}명";
    }

    /// <summary>매치 참가자 한 명. 장착 무기가 축복 후보 필터에 쓰인다 (S-GD-SB84DQ).</summary>
    public readonly struct MatchParticipant
    {
        public readonly int PlayerId;
        public readonly WeaponTag EquippedWeapon;

        public MatchParticipant(int playerId, WeaponTag equippedWeapon)
        {
            PlayerId = playerId;
            EquippedWeapon = equippedWeapon;
        }
    }

    /// <summary>
    /// 화면이 그대로 그릴 수 있는 한 플레이어의 경제 상태.
    /// <b>화면은 이 값들로 계산하지 않는다</b> — 판정은 전부 도메인이 끝냈다.
    /// </summary>
    public readonly struct PlayerEconomySnapshot
    {
        public readonly int PlayerId;
        public readonly int Round;
        public readonly MatchEconomyPhase Phase;

        /// <summary>현재 확정 잔액.</summary>
        public readonly int Gold;

        /// <summary>직전 정산에서 넘어온 골드.</summary>
        public readonly int CarriedGold;

        /// <summary>직전 정산에서 한도를 넘어 사라진 골드 (S-GD-P5RFZM).</summary>
        public readonly int ForfeitedGold;

        /// <summary>이번 매치에서 지금까지 획득한 축복 수 (S-GD-RFXF8X).</summary>
        public readonly int AcquiredCount;

        /// <summary>남은 무료 잠금 수.</summary>
        public readonly int RemainingLockCount;

        /// <summary>이번 준비 단계에서 재추첨한 횟수.</summary>
        public readonly int RerollCount;

        /// <summary>재추첨이 막혀 있으면 그 사유. <c>Success</c> 면 가능하다.</summary>
        public readonly RerollResult RerollBlockedBy;

        public bool CanReroll => RerollBlockedBy == RerollResult.Success;

        public PlayerEconomySnapshot(
            int playerId,
            int round,
            MatchEconomyPhase phase,
            int gold,
            int carriedGold,
            int forfeitedGold,
            int acquiredCount,
            int remainingLockCount,
            int rerollCount,
            RerollResult rerollBlockedBy)
        {
            PlayerId = playerId;
            Round = round;
            Phase = phase;
            Gold = gold;
            CarriedGold = carriedGold;
            ForfeitedGold = forfeitedGold;
            AcquiredCount = acquiredCount;
            RemainingLockCount = remainingLockCount;
            RerollCount = rerollCount;
            RerollBlockedBy = rerollBlockedBy;
        }
    }
}
