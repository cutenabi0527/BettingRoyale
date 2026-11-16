using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Roulette;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// 참가자 한 명의 세션 한정 거래 상태. 상점/강화는 같은 공용 지갑과 게이트를 사용한다.
    /// 제품 라운드 조정·정산·계정 API와 연결하지 않는다. 중도 참가자가 다른 지갑을 초기화하지 않는다.
    /// </summary>
    public sealed class TrainingPracticeState : IPreparationAccessPolicy
    {
        private readonly MatchEconomyState economy;
        private readonly EconomyRequestLedger ledger = new();
        private readonly BlessingLoadout blessings = new();
        private readonly IReadOnlyList<BlessingDefinition> catalog;
        private readonly ShopRequestGate shop;
        /// <summary>상점과 같은 인벤토리 제공자. 축복을 고른 순간 받는 물건(저점 매수의 물약)도 여기로 넣는다.</summary>
        private readonly IShopInventoryProvider inventories;
        private readonly UpgradeRequestGate upgrade;

        /// <summary>
        /// 룰렛. <b>제품과 같은 게이트·같은 원장·같은 지갑</b>을 쓴다 (S-GD-7VKZA5 · S-QA-HQABXR).
        ///
        /// <para>
        /// 훈련 세션은 준비 시간이 없으므로 <see cref="TickRoulette"/> 에
        /// <see cref="UnboundedPreparationTicks"/> 를 넘겨 판이 계속 이어지게 한다.
        /// 표시 전용 모형을 따로 두지 않는다 — 두면 훈련에서 확인한 것이 제품과 다른 규칙이 된다.
        /// </para>
        /// </summary>
        private readonly RouletteRequestGate roulette;

        /// <summary>훈련 세션의 룰렛 회전 난수. 세션 시드에서 갈라 재현 가능하게 둔다.</summary>
        private readonly DeterministicRandom rouletteRandom;
        private readonly BlessingConfig blessingConfig = BlessingConfig.Default;
        private readonly TrainingBlessingTrialState trial = new();
        private readonly int seed;

        /// <summary>지금까지 연 시험 주기 수. <b>개인 초기화로 되돌아가지 않는다.</b></summary>
        private int trialCycleCounter;

        public string SessionId { get; }
        public int PlayerId { get; }
        public bool IsActive { get; private set; } = true;
        public int Gold => economy.GetGold(PlayerId);
        public IReadOnlyList<BlessingDefinition> Acquired => blessings.Acquired;
        public WeaponType Weapon => economy.GetEquippedWeaponType(PlayerId);
        public const int GrantGoldAmount = EconomyConfig.DefaultRoundGrantGold;

        /// <summary>경제형 축복 시험 주기. 항상 존재하고, 주기를 열지 않았으면 <c>Idle</c> 이다.</summary>
        public TrainingBlessingTrialState Trial => trial;

        /// <summary>
        /// 리롤 캐시백을 갖고 있는가 — <b>화면 표시용</b>이다.
        ///
        /// <para>
        /// 실제 환급은 <see cref="BlessingDraft"/> 가 <b>준비가 열리는 순간의 보유 목록</b>으로
        /// 비율을 찍어 계산한다. 그 계산을 여기서 흉내 내지 않는다 — 두 곳에 생기면 갈린다.
        /// </para>
        /// </summary>
        public bool HasCashbackBlessing => HasCashback(blessings.Acquired);

        /// <summary>리롤 캐시백 축복 id 의 접두사. 등급 접미사(<c>-t1</c>)를 뺀 효과 키와 같다.</summary>
        private const string CashbackIdPrefix = "reroll-cashback";

        /// <summary>
        /// 시험용 <see cref="BlessingEconomyState"/> 가 쓰는 라운드 번호. <b>항상 1 이다.</b>
        ///
        /// <para>
        /// 제품의 라운드와 다른 값이다. 주기마다 상태를 새로 만들기 때문에 1 로 충분하고,
        /// 사용자가 고른 등급 라운드(<see cref="TrainingBlessingTrialState.Round"/>)를 그대로 넣으면
        /// 3라운드 주기 다음에 1라운드 주기를 여는 순간 "라운드는 뒤로 가지 않습니다" 로 터진다.
        /// </para>
        /// </summary>
        private const int TrialEconomyRound = 1;

        /// <summary>조작된 순위표에 세우는 상대의 id. 훈련 참가자 id 는 1 이상이라 절대 겹치지 않는다.</summary>
        private const int SyntheticOpponentId = 0;

        /// <summary>지갑은 캐시하지 않는다 — 개인 초기화가 <c>BeginMatch</c> 로 지갑을 갈아끼운다.</summary>
        private Wallet TrialWallet => economy.GetParticipantWallet(PlayerId);

        /// <summary>시험 전투 중에 축복이 번 골드(사냥꾼 처치·이자 폭발)를 받는다.</summary>
        public bool TryGrantTrialBlessingGold(int amount)
        {
            if (amount <= 0) return false;
            TrialWallet.Earn(amount);
            return true;
        }

        public TrainingPracticeState(string sessionId, int playerId, WeaponType weapon,
            ShopCatalog shopCatalog, IShopInventoryProvider inventories, IEquipmentItemCatalog items,
            IReadOnlyList<BlessingDefinition> blessingCatalog, int seed = 97,
            Func<int, IUpgradeSuccessRoll> upgradeSuccessRollFactory = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("세션 ID가 필요합니다.", nameof(sessionId));
            if (playerId < 1) throw new ArgumentOutOfRangeException(nameof(playerId));
            SessionId = sessionId;
            PlayerId = playerId;
            this.seed = seed;
            catalog = blessingCatalog ?? throw new ArgumentNullException(nameof(blessingCatalog));
            this.inventories = inventories;
            // 강화 성공 판정은 제품과 같은 표·같은 판정기를 쓴다 (S15P21A606-304 · S-QA-HQABXR).
            // upgradeSuccessRollFactory 는 스탯 합산만 보는 QA 도구가 확률을 끄는 자리다 — null 이면 제품 판정기.
            economy = new MatchEconomyState(
                EconomyConfig.Default, BlessingConfig.Default, catalog,
                upgradeSuccessRollFactory: upgradeSuccessRollFactory);
            ResetPersonalState(weapon);
            ledger.OpenPreparation();
            // 96_TrainingLab에는 제품 축복 후보 확정 단계가 없다. 연습 축복은 자체 목록에 직접
            // 들어가므로 제품용 SelectionRequired를 그대로 걸면 상점·강화가 영원히 닫힌다.
            shop = new ShopRequestGate(
                economy, ledger, shopCatalog, inventories, items, this, requiresBlessingSelection: false);
            upgrade = new UpgradeRequestGate(economy, ledger, this, AlwaysConfirmedSelectionPolicy.Instance);

            // ⚠️ 상점·강화와 **같은 원장(ledger)과 같은 접근 정책(this)** 을 넘긴다. 갈리면
            //    재전송 판정이 어긋나고 "상점은 되는데 룰렛만 안 되는" 자리가 생긴다.
            roulette = new RouletteRequestGate(
                economy,
                ledger,
                new RouletteTable(
                    BettingRoyale.Data.Balance.BalanceCatalogs.Current.Roulette.CreateWheel(),
                    BettingRoyale.Data.Balance.BalanceCatalogs.Current.Roulette.CreateConfig(RouletteTicksPerSecond)),
                this);
            rouletteRandom = new DeterministicRandom(seed ^ 0x524F554C);
            roulette.BeginPreparation(PlayerId, false);
        }

        /// <summary>
        /// 훈련 룰렛의 틱 레이트. ⚠️ 임의값이 아니라 <b>제품 Host 와 같은 값</b>을 쓴다 —
        /// 시간 계약(<see cref="PlaceholderRouletteConfig"/>)이 초를 틱으로 바꾸는 유일한 자리다.
        /// </summary>
        public const int RouletteTicksPerSecond = 20;

        /// <summary>
        /// 훈련 세션에는 <b>준비 시간이 없다</b>. 그래서 "판 하나를 더 열 시간이 남았는가" 검사에
        /// 항상 통과하는 값을 넘긴다 — 제품의 마감 리드타임 규칙 자체는 그대로 둔다.
        /// </summary>
        public const int UnboundedPreparationTicks = int.MaxValue;

        public bool CanUse(int playerId, PreparationFacility facility) => IsActive && playerId == PlayerId
            && (facility == PreparationFacility.Shop || facility == PreparationFacility.Upgrade
                || facility == PreparationFacility.Roulette);

        public ShopOffersChanged BuildOffers(bool isMine = false) => shop.BuildOffers(PlayerId, isMine);

        /// <summary>
        /// 마지막으로 끝난 내 판의 순손익. 화면 표시용이며 <b>지갑의 근거가 아니다</b> —
        /// 돈은 이미 게이트가 옮겼다. 개인 초기화에서 0으로 돌아간다.
        /// </summary>
        public int LastRouletteNet { get; private set; }

        /// <summary>바퀴 생김새. 화면이 칸·배당을 그릴 때 한 번 읽는다.</summary>
        public RouletteWheelAnnounced BuildWheel() => roulette.BuildWheel();

        /// <summary>화면이 그대로 그릴 수 있는 룰렛 스냅샷.</summary>
        public TrainingRouletteView BuildRouletteView(bool isMine = false) =>
            TrainingRouletteView.Capture(BuildRouletteState(isMine), BuildWheel(), LastRouletteNet);

        /// <summary>지금 바퀴 상태. 진열과 같은 판정을 쓰므로 화면이 다시 계산하지 않는다.</summary>
        public RouletteStateChanged BuildRouletteState(bool isMine = false) =>
            roulette.BuildState(PlayerId, isMine);

        /// <summary>
        /// 바퀴를 <paramref name="ticks"/> 만큼 돌리고 <b>정산까지 같은 지갑에 반영</b>한다.
        /// Host 만 부른다.
        /// </summary>
        /// <returns>
        /// 이번 호출에서 일어난 일 전부. <b>정산은 모아서 돌려준다</b> — 한 프레임에 여러 틱을
        /// 접었을 때 중간 판의 결과를 잃으면 "돈은 움직였는데 화면은 모르는" 상태가 된다.
        /// </returns>
        public TrainingRouletteTick TickRoulette(int ticks, bool isMine = false)
        {
            if (!IsActive || ticks < 1)
            {
                return TrainingRouletteTick.None;
            }

            var settled = new List<RouletteSettled>();
            var golds = new List<GoldChanged>();
            bool changed = false;

            for (int i = 0; i < ticks; i++)
            {
                RouletteTickOutcome step = roulette.Tick(
                    UnboundedPreparationTicks, rouletteRandom, PlayerId, isMine);

                changed |= step.HasStateChange;
                if (step.Settlements != null)
                {
                    settled.AddRange(step.Settlements);
                    for (int j = 0; j < step.Settlements.Length; j++)
                    {
                        if (step.Settlements[j].PlayerId == PlayerId)
                        {
                            LastRouletteNet = step.Settlements[j].Net;
                        }
                    }
                }
                if (step.Golds != null) golds.AddRange(step.Golds);
            }

            return new TrainingRouletteTick(
                changed,
                BuildRouletteState(isMine),
                settled.ToArray(),
                golds.ToArray());
        }
        public UpgradeStateChanged BuildUpgrade(bool isMine = false) => upgrade.BuildState(PlayerId, isMine);
        public void ApplyUpgrade(StatSheet sheet) => economy.ApplyUpgradeToSheet(PlayerId, sheet);

        /// <summary>
        /// 연습 무기를 바꾼다. <b>시험 주기가 살아 있는 동안에는 거부한다</b> —
        /// 후보는 시작 시점의 무기로 걸러졌으므로 도중에 무기가 바뀌면 호환되지 않는 축복을
        /// 확정하게 된다.
        /// </summary>
        public bool SetWeapon(WeaponType weapon) =>
            IsActive && !trial.IsActive && economy.TrySetEquippedWeaponType(PlayerId, weapon);

        public TrainingActionResult Handle(int senderPlayerId, in TrainingActionRequest request)
        {
            if (!IsActive || senderPlayerId != PlayerId || request.SessionId != SessionId)
                return Reject("이 훈련 세션의 참가자가 아닙니다.");
            if (request.Sequence < 1 || !Enum.IsDefined(typeof(TrainingActionKind), request.Kind))
                return Reject("올바르지 않은 요청입니다.");

            var ticket = new EconomyRequestTicket(ledger.Epoch, request.Sequence);
            // 각 공용 게이트가 원장을 한 번만 판정한다. 앞에서 Accept를 중복 호출하지 않는다.
            if (request.Kind == TrainingActionKind.Purchase)
            {
                ShopPurchaseOutcome result = shop.HandlePurchase(PlayerId, request.Value, ticket, false);
                if (result.Verdict != EconomyRequestVerdict.Fresh) return Repeated(result.Verdict);
                return result.HasRejection ? Reject("구매 거절: " + result.Rejection.Reason)
                    : new TrainingActionResult(result.HasInventoryChange, "구매했습니다.");
            }
            if (request.Kind == TrainingActionKind.Roulette)
            {
                if (!RouletteRequestPayload.TryUnpack(
                        request.Value, out RouletteRequestKind rouletteKind, out int number, out int amount))
                    return Reject("올바르지 않은 룰렛 요청입니다.");

                if (rouletteKind == RouletteRequestKind.Withdraw)
                {
                    RouletteWithdrawOutcome withdraw = roulette.HandleWithdraw(PlayerId, ticket, false);
                    if (withdraw.Verdict != EconomyRequestVerdict.Fresh) return Repeated(withdraw.Verdict);
                    return withdraw.HasRejection
                        ? Reject("물리기 거절: " + withdraw.Rejection.Reason)
                        : new TrainingActionResult(true, "베팅을 물렸습니다.");
                }

                RouletteRequestOutcome bet = roulette.HandleBet(PlayerId, number, amount, ticket, false);
                if (bet.Verdict != EconomyRequestVerdict.Fresh) return Repeated(bet.Verdict);
                return bet.HasRejection
                    ? Reject("베팅 거절: " + bet.Rejection.Reason)
                    : new TrainingActionResult(true, $"{bet.BetPlaced.Number}에 {bet.BetPlaced.Amount}G를 걸었습니다.");
            }
            if (request.Kind == TrainingActionKind.Upgrade)
            {
                UpgradeRequestOutcome result = upgrade.HandleUpgrade(PlayerId, request.Value, ticket, false);
                if (result.Verdict != EconomyRequestVerdict.Fresh) return Repeated(result.Verdict);
                if (result.HasRejection) return Reject("강화 거절: " + result.Rejection.Reason);

                // 확률 실패도 처리된 요청이다(Accepted) — 골드는 나갔고 단계는 그대로다 (S15P21A606-304).
                return result.ChanceFailed
                    ? new TrainingActionResult(true, "강화 실패 — 골드만 소모되었습니다.")
                    : new TrainingActionResult(true, "강화했습니다.");
            }

            EconomyRequestVerdict verdict = ledger.Accept(PlayerId, ticket);
            if (verdict != EconomyRequestVerdict.Fresh) return Repeated(verdict);
            switch (request.Kind)
            {
                case TrainingActionKind.Sync:
                    return new TrainingActionResult(true, "훈련 상태를 불러왔습니다.");
                case TrainingActionKind.GrantGold:
                    if (Gold > int.MaxValue - GrantGoldAmount) return Reject("더 지급할 수 없습니다.");
                    economy.Earn(PlayerId, GrantGoldAmount);
                    return new TrainingActionResult(true, GrantGoldAmount + "G를 지급했습니다.");
                case TrainingActionKind.SelectBlessing:
                    // 시험 중의 직접 선택은 이미 뽑아 둔 후보·보유 목록과 어긋난다 (S-GD-VF67AG 가 뚫린다).
                    if (trial.IsActive) return Reject("시험이 진행 중입니다. 먼저 시험을 끝냅니다.");
                    foreach (BlessingDefinition definition in catalog)
                    {
                        if (definition.Id != request.Selection) continue;
                        if (!definition.IsCompatibleWith(ToBlessingWeapon(Weapon)))
                            return Reject("현재 주무기와 호환되지 않는 축복입니다.");
                        if (blessings.Contains(definition)) return Reject("이미 선택한 축복입니다.");
                        blessings.Add(definition);
                        var ownedEconomy = economy.GetBlessingEconomy(PlayerId);
                        ownedEconomy.UpdateLoadout(blessings.Acquired);
                        ownedEconomy.GrantAcquisitionBonuses(TrialWallet, "instant_gold");
                        return new TrainingActionResult(true, definition.DisplayName + " 선택");
                    }
                    return Reject("등록되지 않은 축복입니다.");
                case TrainingActionKind.Reset:
                    ResetPersonalState(Weapon);
                    return new TrainingActionResult(true, "내 연습 상태를 초기화했습니다.");
                case TrainingActionKind.Heal:
                    return new TrainingActionResult(true, "회복했습니다.");
                case TrainingActionKind.SelectWeapon:
                    if (trial.IsActive) return Reject("시험이 진행 중입니다. 먼저 시험을 끝냅니다.");
                    return request.Value == (int)WeaponType.Sword || request.Value == (int)WeaponType.Bow || request.Value == (int)WeaponType.Mace
                        ? new TrainingActionResult(true, "연습 무기를 선택했습니다.") : Reject("선택할 수 없는 무기입니다.");
                case TrainingActionKind.Travel:
                    return request.Value >= 0 && request.Value <= 4
                        ? new TrainingActionResult(true, "구역 이동을 요청했습니다.")
                        : Reject("등록되지 않은 구역입니다.");
                case TrainingActionKind.SetPlayerDamage:
                    return request.Value == 0 || request.Value == 1
                        ? new TrainingActionResult(true, request.Value == 1 ? "플레이어 간 피해 켜짐" : "플레이어 간 피해 꺼짐")
                        : Reject("올바르지 않은 설정입니다.");
                // 방 전체 설정이라 여기서는 <b>값이 말이 되는지만</b> 본다. 실제로 봇을 세우고 치우는
                // 것은 Host 다 — 개인 상태에 봇 수를 들고 있으면 사람마다 다른 수를 믿게 된다.
                case TrainingActionKind.SetBotZones:
                    return TrainingBotRule.IsValid(request.Value)
                        ? new TrainingActionResult(true,
                            "전투 봇 " + TrainingBotRule.Count(request.Value) + "구역")
                        : Reject("전투 봇을 둘 수 없는 구역이 섞였습니다.");

                // ── 경제형 축복 시험 ──────────────────────────────────────
                // Value 를 보지 않는 명령이 있는 것은 의도다 — 예치 금액도 재추첨 비용도
                // 카탈로그·설정이 정하므로 클라이언트가 고를 수 있으면 안 된다 (GrantGold 와 같은 규칙).
                case TrainingActionKind.TrialBegin:
                    return BeginTrial(request.Value, request.Selection);
                case TrainingActionKind.TrialDeposit:
                    return DepositTrial();
                case TrainingActionKind.TrialBlindBet:
                    return BlindBetTrial();
                case TrainingActionKind.TrialReroll:
                    return RerollTrial();
                case TrainingActionKind.TrialToggleLock:
                    return ToggleTrialLock(request.Value);
                case TrainingActionKind.TrialSelect:
                    return SelectTrialCandidate(request.Value);
                case TrainingActionKind.TrialCombat:
                    return StartTrialCombat();
                case TrainingActionKind.TrialSettle:
                    if (request.Value != 0 && request.Value != 1) return Reject("정산 값은 0(탈락) 또는 1(생존) 입니다.");
                    return SettleTrial(request.Value == 1);

                default:
                    return Reject("지원하지 않는 요청입니다.");
            }
        }

        /// <summary>
        /// 세션을 닫는다. <b>걸려 있던 베팅은 돌려준다</b> — 돌지 않은 판에 돈을 먹지 않는다는
        /// 제품 규칙과 같다 (S-GD-7VKZA5).
        /// </summary>
        public void Close()
        {
            roulette?.Close(PlayerId, false);
            IsActive = false;
            blessings.ResetForNewMatch();
            trial.Clear();
        }

        private void ResetPersonalState(WeaponType weapon)
        {
            // ⚠️ 지갑을 갈아끼우기 **전에** 판을 닫는다. 진행 중이던 베팅을 남기면 그 돈이
            //    새 지갑으로 환급되어 **초기화가 골드를 만들어 낸다.** 닫으면서 나가는 환급은
            //    곧 버려질 지갑으로 들어가므로 보이지 않는다.
            roulette?.Close(PlayerId, false);

            economy.BeginMatch(new[] { new MatchParticipant(PlayerId, ToBlessingWeapon(weapon)) }, seed);
            economy.BeginPreparation();
            economy.TrySetEquippedWeaponType(PlayerId, weapon);
            blessings.ResetForNewMatch();
            // 지갑이 통째로 새로 만들어지므로 주기가 들고 있던 예치·환급 기준은 더 이상 유효하지 않다.
            // 주기 번호(Cycle)는 남긴다 — 초기화 전후가 같은 번호를 쓰면 늦게 온 콜백을 구분할 수 없다.
            trial.Clear();
            // 새 지갑 위에서 새 판을 연다. 빠뜨리면 초기화 뒤에 바퀴가 Closed 로 남아
            // "룰렛이 조용히 안 열리는" 상태가 된다 (RouletteRequestGate.BeginPreparation 주석).
            roulette?.BeginPreparation(PlayerId, false);
            LastRouletteNet = 0;
            // ledger는 유지한다. 초기화 전의 구매 요청을 다시 보내도 실행되지 않는다.
        }

        // ── 경제형 축복 시험 주기 ─────────────────────────────────────────
        //
        // 제품 라운드가 아니다. 라운드 진행·전원 정산·계정 API 와 연결하지 않으며,
        // 판정과 돈은 전부 제품 타입(BlessingEconomyState · BlessingDraft)이 훈련 지갑 위에서 한다.
        //
        // 제품과 <b>일부러 다른</b> 점은 두 가지다.
        //   1. 축복 선택이 필수가 아니다 — 후보가 부족해도 예치만 시험할 수 있어야 한다.
        //      (제품은 EndPreparation 에서 자동 선택한다. 여기서 자동으로 고르면 사용자가
        //       의도하지 않은 축복이 훈련 보유 목록에 쌓인다.)
        //   2. 하위권 자격의 근거가 실제 순위표가 아니라 사용자가 고른 조작된 2인 점수표다.

        /// <summary>새 시험 주기를 연다. <paramref name="round"/> 가 등급과 재추첨 비용을 정한다.</summary>
        private TrainingActionResult BeginTrial(int round, string rankProfile)
        {
            if (round < 1 || round > blessingConfig.DefinedRoundCount)
                return Reject($"시험 등급 라운드는 1 ~ {blessingConfig.DefinedRoundCount} 입니다.");
            if (!TrainingBlessingTrialRank.IsDefined(rankProfile))
                return Reject("등록되지 않은 순위 프로필입니다.");
            if (trial.IsActive)
                // 예치금이 걸린 주기를 조용히 버리면 낸 돈이 사라진 것도 모르게 된다.
                return Reject("진행 중인 시험이 있습니다. 전투를 끝내 정산한 뒤에 새로 엽니다.");

            trial.Clear();
            trialCycleCounter++;

            var trialEconomy = new BlessingEconomyState();
            trialEconomy.BeginPreparation(TrialEconomyRound, blessings.Acquired);
            trialEconomy.GrantPreparationBonuses(TrialWallet);

            trial.Economy = trialEconomy;
            trial.Cycle = trialCycleCounter;
            trial.Round = round;
            trial.RankProfile = rankProfile;
            RefreshTrialEligibility();

            bool drafted = TryBeginTrialDraft(round);
            trial.Phase = TrainingBlessingTrialPhase.Preparation;

            return new TrainingActionResult(true, drafted
                ? $"{round}라운드 시험을 시작했습니다."
                : $"{round}라운드 시험을 시작했습니다. 후보가 모자라 추첨은 열리지 않습니다.");
        }

        /// <summary>
        /// 이번 주기의 추첨을 연다. <b>후보를 채울 수 없으면 열지 않는다</b> —
        /// 모자란 자리를 가짜로 채우면 훈련에서만 나오는 축복이 생긴다.
        /// </summary>
        private bool TryBeginTrialDraft(int round)
        {
            try
            {
                // 저점 매수는 하위권에게만 후보로 나온다 — 제품과 같은 함수에 시험 순위표를 넘긴다(S-GD-TMPBEF).
                IReadOnlyList<BlessingScore> standings = BuildTrialStandings(trial.RankProfile);
                trial.Draft = BlessingDraft.Begin(
                    blessingConfig,
                    catalog,
                    ToBlessingWeapon(Weapon),
                    round,
                    blessings,
                    new DeterministicRandom(seed ^ (trialCycleCounter * 7919)),
                    offerable: definition => BlessingEconomyState.IsOfferable(definition, PlayerId, standings));
                return true;
            }
            catch (InvalidOperationException)
            {
                // 이 무기·등급에서 아직 고르지 않은 축복이 3개가 안 된다. 주기 자체는 계속한다.
                trial.Draft = null;
                return false;
            }
        }

        private TrainingActionResult DepositTrial()
        {
            if (trial.Phase != TrainingBlessingTrialPhase.Preparation) return Reject("시험 준비 단계가 아닙니다.");

            BlessingEconomyState trialEconomy = trial.Economy;
            if (!trialEconomy.HasInterest) return Reject("이자 폭발 축복이 없습니다.");
            if (trialEconomy.InterestDeposited) return Reject("이번 시험에 이미 예치했습니다.");
            if (!trialEconomy.TryDeposit(TrialWallet)) return Reject("예치할 수 없습니다. 골드가 모자랍니다.");

            return new TrainingActionResult(true, trialEconomy.DepositAmount + "G를 예치했습니다.");
        }

        private TrainingActionResult BlindBetTrial()
        {
            if (trial.Phase != TrainingBlessingTrialPhase.Preparation) return Reject("시험 준비 단계가 아닙니다.");

            BlessingEconomyState trialEconomy = trial.Economy;
            if (!trialEconomy.HasBlindBet) return Reject("블라인드 베팅 축복이 없습니다.");
            if (trialEconomy.BlindBetAccepted) return Reject("이번 시험에 이미 블라인드 베팅을 수락했습니다.");
            if (!trialEconomy.TryAcceptBlindBet(TrialWallet)) return Reject("블라인드 베팅을 수락할 수 없습니다.");

            return new TrainingActionResult(true,
                $"블라인드 베팅 {trialEconomy.BlindBetBonusGold}G 수령 · 전투 시작 시 체력 {trialEconomy.BlindBetCostFraction:P0} 비용");
        }

        private TrainingActionResult RerollTrial()
        {
            if (trial.Phase != TrainingBlessingTrialPhase.Preparation) return Reject("시험 준비 단계가 아닙니다.");
            if (trial.Draft == null) return Reject("후보가 없어 재추첨할 수 없습니다.");

            RerollResult result = trial.Draft.TryReroll(TrialWallet);
            return result == RerollResult.Success
                ? new TrainingActionResult(true, "후보를 다시 뽑았습니다.")
                : Reject(RerollRejection(result));
        }

        private TrainingActionResult ToggleTrialLock(int index)
        {
            if (trial.Phase != TrainingBlessingTrialPhase.Preparation) return Reject("시험 준비 단계가 아닙니다.");
            if (trial.Draft == null) return Reject("후보가 없습니다.");
            if (index < 0 || index >= trial.Draft.Candidates.Count) return Reject("등록되지 않은 후보 번호입니다.");

            return trial.Draft.TryToggleLock(index)
                ? new TrainingActionResult(true, "후보 잠금을 바꿨습니다.")
                : Reject($"무료 잠금은 {blessingConfig.MaxFreeLocks}개까지입니다.");
        }

        private TrainingActionResult SelectTrialCandidate(int index)
        {
            if (trial.Phase != TrainingBlessingTrialPhase.Preparation) return Reject("시험 준비 단계가 아닙니다.");
            if (trial.Draft == null) return Reject("후보가 없습니다.");
            if (trial.Draft.IsSelected) return Reject("이미 축복을 선택했습니다.");
            if (index < 0 || index >= trial.Draft.Candidates.Count) return Reject("등록되지 않은 후보 번호입니다.");

            // 리롤 캐시백의 할인 배율은 이 추첨이 열린 순간에 이미 찍혀 있으므로
            // 지금 캐시백을 고르는 것은 이번 주기의 재추첨 비용을 소급해 바꾸지 않는다 (제품과 같은 계약).
            BlessingDefinition selected = trial.Draft.Select(index);
            blessings.Add(selected);
            RefreshTrialEligibility();

            return new TrainingActionResult(true, selected.DisplayName + " 선택");
        }

        private TrainingActionResult StartTrialCombat()
        {
            if (trial.Phase != TrainingBlessingTrialPhase.Preparation) return Reject("시험 준비 단계가 아닙니다.");

            // 고르지 않은 후보는 그대로 버린다 — 훈련에서는 선택이 필수가 아니다.
            trial.Economy.EndPreparation();
            trial.Phase = TrainingBlessingTrialPhase.Combat;
            return new TrainingActionResult(true, "시험 전투를 시작했습니다.");
        }

        private TrainingActionResult SettleTrial(bool survived)
        {
            if (!TryEndTrial(survived)) return Reject("시험 전투 중이 아닙니다.");

            if (!survived) return new TrainingActionResult(true, "탈락 처리했습니다. 예치금은 돌아오지 않습니다.");
            return new TrainingActionResult(true, trial.SettlementRefund > 0
                ? $"생존 처리했습니다. {trial.SettlementRefund}G를 환급했습니다."
                : "생존 처리했습니다.");
        }

        /// <summary>
        /// 실제 사망이 확정됐을 때 호스트가 부른다. <b>전투 중일 때만</b> 정산하고,
        /// 여러 번 불러도 예치금이 두 번 사라지거나 두 번 돌아오지 않는다.
        /// </summary>
        /// <returns>이 호출이 실제로 정산했으면 true.</returns>
        public bool FinishTrialOnDefeat() => TryEndTrial(false);

        private bool TryEndTrial(bool survived)
        {
            if (trial.Phase != TrainingBlessingTrialPhase.Combat) return false;

            BlessingEconomyState trialEconomy = trial.Economy;
            trialEconomy.EndPreparation(); // 여러 번 불러도 같다. 정산은 닫힌 뒤에만 받는다.
            trial.SettlementRefund = trialEconomy.SettleRound(TrialEconomyRound, survived, TrialWallet);
            trial.Phase = TrainingBlessingTrialPhase.Settled;
            return true;
        }

        /// <summary>화면이 계산 없이 그릴 수 있는 시험 주기 스냅샷.</summary>
        public TrainingBlessingTrialView BuildTrialView()
        {
            // 주기 밖에서도 "무엇을 갖고 있고 얼마가 드는가" 는 보여야 한다. 그때는 제품 타입에
            // 현재 보유 목록만 얹어 읽는다 — 수치를 화면이 직접 카탈로그에서 찾지 않게 한다.
            BlessingEconomyState trialEconomy = trial.Economy ?? new BlessingEconomyState(blessings.Acquired);
            BlessingDraft draft = trial.Draft;
            Wallet wallet = TrialWallet;
            bool preparing = IsActive && trial.Phase == TrainingBlessingTrialPhase.Preparation;

            int depositCost = trialEconomy.DepositCost;
            int rerollCost = draft != null && draft.TryGetCurrentRerollCost(out int cost) ? cost : 0;

            IReadOnlyList<BlessingCandidate> candidates = draft?.Candidates;
            int candidateCount = candidates?.Count ?? 0;
            var candidateIds = new string[candidateCount];
            var locked = new bool[candidateCount];
            for (int i = 0; i < candidateCount; i++)
            {
                candidateIds[i] = candidates[i].Blessing?.Id ?? string.Empty;
                locked[i] = candidates[i].IsLocked;
            }

            return new TrainingBlessingTrialView(
                phase: trial.Phase,
                cycle: trial.Cycle,
                round: trial.Round,
                rankProfile: trial.RankProfile,
                lowerHalfEligible: trial.LowerHalfEligible,
                hasInterest: trialEconomy.HasInterest,
                hasLowBuy: trialEconomy.HasLowBuy,
                hasCashback: HasCashback(blessings.Acquired),
                hasBlindBet: trialEconomy.HasBlindBet,
                canBlindBet: preparing && trialEconomy.HasBlindBet && !trialEconomy.BlindBetAccepted,
                blindBetAccepted: trialEconomy.BlindBetAccepted,
                blindBetBonusGold: trialEconomy.BlindBetBonusGold,
                blindBetHealthCostFraction: trialEconomy.BlindBetCostFraction,
                canBegin: IsActive && !trial.IsActive,
                // 잔액까지 본다 — 눌렀다가 거부당하는 대신 애초에 못 누르게 한다.
                canDeposit: preparing && trialEconomy.CanDeposit && depositCost > 0 && wallet.Gold >= depositCost,
                canReroll: preparing && draft != null
                                     && draft.GetRerollAvailability(wallet) == RerollResult.Success,
                canSelect: preparing && draft != null && !draft.IsSelected,
                depositCost: depositCost,
                depositAmount: trialEconomy.DepositAmount,
                rerollCost: rerollCost,
                rerollCount: draft?.RerollCount ?? 0,
                settlementRefund: trial.SettlementRefund,
                selectedId: draft?.Selected?.Id ?? string.Empty,
                candidateIds: candidateIds,
                locked: locked);
        }

        /// <summary>
        /// 보유 목록을 시험 상태에 다시 얹고, 화면에 보일 하위권 자격을 다시 계산한다.
        /// <b>이번 주기의 예치·블라인드 베팅 기록은 그대로 둔다.</b>
        ///
        /// <para>
        /// 목록을 먼저 얹는 것이 의도다 — 방금 고른 것이 저점 매수면 목록을 반영하기 전에는
        /// 하위 비율을 알 수 없다.
        /// </para>
        /// </summary>
        private void RefreshTrialEligibility()
        {
            BlessingEconomyState trialEconomy = trial.Economy;
            if (trialEconomy == null) return;

            trialEconomy.UpdateLoadout(blessings.Acquired);
            var ownedEconomy = economy.GetBlessingEconomy(PlayerId);
            ownedEconomy.UpdateLoadout(blessings.Acquired);
            trialEconomy.CopyAcquisitionHistoryFrom(ownedEconomy, "instant_gold");
            trial.LowerHalfEligible = ResolveTrialLowerHalf(trialEconomy.LowBuyRankFraction);
            // 고른 순간 받는 보상(쇼 미 더 머니·탐욕·저점 매수). 시험마다 새 경제라 시험마다 한 번이다.
            trialEconomy.GrantAcquisitionBonuses(TrialWallet);
            ownedEconomy.CopyAcquisitionHistoryFrom(trialEconomy, "instant_gold");
            // 저점 매수의 물약은 상점과 같은 인벤토리로 넣는다. 자리가 없으면 들어가지 않는다.
            IReadOnlyList<BlessingItemGrant> grants = trialEconomy.ConsumePendingItemGrants();
            if (grants.Count > 0 && inventories != null
                && inventories.TryResolve(PlayerId, out IEquipmentInventoryDriver inventory))
            {
                BlessingEconomyState.DeliverItemGrants(grants, inventory, out _);
            }
        }

        /// <summary>
        /// 조작된 점수표로 하위권 자격을 <b>제품과 같은 함수에</b> 물어본다.
        ///
        /// <para>
        /// 자격을 대입하지 않는 것이 요점이다 — <c>lower</c> 프로필도 저점 매수가 없으면
        /// 비율 자체가 없어 false 이고, 동점 프로필은 "동점자는 같은 답을 받는다" 규칙 때문에
        /// 하위 절반이어도 false 다.
        /// </para>
        ///
        /// <para>
        /// 점수표는 명시적으로 주어진 별도의 과거이므로 <b>흉내 내는 등급 라운드와 무관하다</b> —
        /// 1라운드 등급을 시험하면서도 하위권 자격을 시험할 수 있다.
        /// </para>
        /// </summary>
        private bool ResolveTrialLowerHalf(float rankFraction)
        {
            if (float.IsNaN(rankFraction) || rankFraction <= 0f || rankFraction >= 1f) return false;

            IReadOnlyList<BlessingScore> scores = BuildTrialStandings(trial.RankProfile);
            return BlessingEconomyState.IsLowerHalf(PlayerId, scores, rankFraction);
        }

        /// <summary>프로필이 뜻하는 2인 점수표. <c>none</c> 은 점수표가 없는 상태(제품 1라운드)다.</summary>
        private IReadOnlyList<BlessingScore> BuildTrialStandings(string rankProfile)
        {
            if (string.Equals(rankProfile, TrainingBlessingTrialRank.Lower, StringComparison.Ordinal))
                return new[] { new BlessingScore(PlayerId, 0), new BlessingScore(SyntheticOpponentId, 100) };
            if (string.Equals(rankProfile, TrainingBlessingTrialRank.Upper, StringComparison.Ordinal))
                return new[] { new BlessingScore(PlayerId, 100), new BlessingScore(SyntheticOpponentId, 0) };
            if (string.Equals(rankProfile, TrainingBlessingTrialRank.Tied, StringComparison.Ordinal))
                return new[] { new BlessingScore(PlayerId, 50), new BlessingScore(SyntheticOpponentId, 50) };

            return Array.Empty<BlessingScore>();
        }

        private static bool HasCashback(IReadOnlyList<BlessingDefinition> acquired)
        {
            for (int i = 0; i < acquired.Count; i++)
            {
                string id = acquired[i]?.Id;
                if (id != null && id.StartsWith(CashbackIdPrefix, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static string RerollRejection(RerollResult result) => result switch
        {
            RerollResult.AlreadySelected => "이미 축복을 선택했습니다.",
            RerollResult.NotEnoughGold => "골드가 모자랍니다.",
            RerollResult.NothingToReroll => "다시 뽑을 후보가 없습니다.",
            _ => "이 라운드의 재추첨 비용이 정의돼 있지 않습니다.",
        };

        public static BettingRoyale.Data.Blessing.WeaponTag ToBlessingWeapon(WeaponType weapon) => weapon switch
        {
            WeaponType.Bow => BettingRoyale.Data.Blessing.WeaponTag.Bow,
            WeaponType.Mace => BettingRoyale.Data.Blessing.WeaponTag.Mace,
            _ => BettingRoyale.Data.Blessing.WeaponTag.Sword,
        };

        private static TrainingActionResult Reject(string message) => new(false, message);
        private static TrainingActionResult Repeated(EconomyRequestVerdict verdict) =>
            new(false, verdict == EconomyRequestVerdict.Duplicate ? "이미 처리한 요청입니다." : "요청 순서가 올바르지 않습니다.",
                verdict == EconomyRequestVerdict.Duplicate);
    }
}
