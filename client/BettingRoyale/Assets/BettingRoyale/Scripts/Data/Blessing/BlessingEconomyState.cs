using System;
using System.Collections.Generic;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Shop;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 순위표 한 줄. <b>누적 점수</b>다 — "지금 라운드에서 몇 점" 이 아니라 "지금까지 몇 점" 이다.
    /// </summary>
    public readonly struct BlessingScore
    {
        public readonly int PlayerId;
        public readonly int Score;

        public BlessingScore(int playerId, int score)
        {
            PlayerId = playerId;
            Score = score;
        }
    }

    /// <summary>
    /// 축복을 고른 순간 받는 물건 한 줄 — 저점 매수의 체력·신속 물약. 경제는 인벤토리를 모르므로
    /// 어댑터가 <see cref="BlessingEconomyState.ConsumePendingItemGrants"/> 로 꺼내 지급한다.
    /// </summary>
    public readonly struct BlessingItemGrant
    {
        public readonly int ItemId;
        public readonly int Quantity;

        public BlessingItemGrant(int itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public override string ToString() => $"item {ItemId} x{Quantity}";
    }

    /// <summary>
    /// 경제형 축복의 <b>준비 단계 상태</b> — 이자 폭발(<c>interest-burst</c>)의 예치, 고른 순간 받는 보상
    /// (쇼 미 더 머니·탐욕·저점 매수), 저점 매수의 이번 라운드 킬 점수 배율.
    ///
    /// <para>
    /// ⚠️ <b>저점 매수의 "위험 보급 도전" 은 엑셀 개편으로 없어졌다</b>(S-GD-TMPBEF). 이제 저점 매수는 순위 하위권에게만
    /// 뽑기 후보로 나오고(<see cref="IsOfferable"/>), 고르면 골드·물약을 즉시 받고 그 라운드 킬 점수가 배가 된다.
    /// 도전·보급 상자 코드는 화면·회선 계약까지 함께 걷어냈다.
    /// </para>
    ///
    /// <para>
    /// <b>여기 있는 것은 "약속" 뿐이다.</b> 예치금을 실제로 차감·환급하는 것까지가 이 타입의 일이고,
    /// 전투 중 버프 창(35초 뒤 15초)이나 인벤토리 지급은 <b>여기 없다.</b>
    /// </para>
    ///
    /// <para>
    /// <b>예치는 자동으로 해주지 않는다.</b> 축복을 갖고 있다고 예치되지 않는다(<see cref="TryDeposit"/>) —
    /// 플레이어가 준비 단계에서 <b>명시적으로</b> 눌러야 한다. 예치는 죽으면 잃는 돈이므로, 조용한 참가는 그 자체가 버그다.
    /// </para>
    ///
    /// <para>
    /// <b>라운드 정산은 호출자가 한다.</b> 새 라운드를 열 때 직전 라운드의 예치금이 정산되지 않았으면
    /// 조용히 돌려주는 대신 예외로 막는다 — 죽었는지 살았는지는 이 타입이 알 수 없고,
    /// 모르는 채로 환급하면 <b>탈락자에게도 돈이 돌아간다.</b>
    /// </para>
    ///
    /// <para>UnityEngine 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class BlessingEconomyState
    {
        /// <summary>효과 키. 등급 접미사가 없다 — <c>interest-burst-t3</c> 의 효과는 <c>interest-burst</c> 다.</summary>
        private const string InterestEffect = "interest-burst";

        private const string LowBuyEffect = "low-buy";
        private const string PettyCashEffect = "petty-cash";
        private const string BlindBetEffect = "blind-bet";
        private const string DealerChoiceEffect = "dealer-choice";

        // 엑셀 전용 경제 축복 (엑셀 effect 이름).
        /// <summary>엑셀 <c>interest-burst-t1</c> — 예치하면 전투 중 주기적으로 이자를 받는다. 예치는 코드 이자 폭발과 같다.</summary>
        private const string InterestIncomeEffect = "interest_income";
        private const string RouletteRefundEffect = "roulette_refund";
        private const string InstantGoldEffect = "instant_gold";
        private const string GreedEffect = "greed";

        private const string DepositKey = "deposit";
        private const string RefundFractionKey = "refund_fraction";
        private const string BonusGoldKey = "bonus_gold";
        private const string RankFractionKey = "rank_fraction";
        private const string KillScoreMultiplierKey = "kill_score_multiplier";
        private const string HealthPotionKey = "health_potion";
        private const string HastePotionKey = "haste_potion";

        private BlessingMvpDefinition interest;
        private BlessingMvpDefinition lowBuy;
        private BlessingMvpDefinition pettyCash;
        private BlessingMvpDefinition blindBet;
        private BlessingMvpDefinition dealerChoice;
        private BlessingMvpDefinition rouletteRefund;
        private BlessingMvpDefinition instantGold;
        private BlessingMvpDefinition greed;
        /// <summary>고른 순간 받는 골드를 이미 받은 축복. 같은 축복으로 두 번 받지 않는다.</summary>
        private readonly HashSet<string> acquisitionGranted = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>고른 순간 받은 물건 중 아직 인벤토리로 넘기지 않은 것. 어댑터가 꺼내 지급한다.</summary>
        private readonly List<BlessingItemGrant> pendingItemGrants = new List<BlessingItemGrant>(2);
        private int pettyCashGrantedRound;
        /// <summary>저점 매수를 고른 라운드. 그 라운드의 킬 점수만 배가 된다. 0 이면 고르지 않았다.</summary>
        private int lowBuyRound;
        private float lowBuyKillScoreMultiplier = 1f;

        /// <summary>
        /// 예치 시점에 찍어둔 환급 비율. 정산 때 카탈로그를 다시 보지 않는다 —
        /// 그 사이 보유 목록이 바뀌어도 <b>이미 낸 돈의 조건</b>은 변하지 않아야 한다.
        /// </summary>
        private float depositRefundFraction;

        /// <param name="acquired">이미 획득한 축복. 없으면 비워둔다.</param>
        /// <remarks>
        /// 저점 매수의 하위권 자격은 여기서 받지 않는다 — 뽑기 후보 단계(<see cref="IsOfferable"/>)에서 본다.
        /// </remarks>
        public BlessingEconomyState(IReadOnlyList<BlessingDefinition> acquired = null)
        {
            ApplyLoadout(acquired);
        }

        /// <summary>현재 준비 단계의 라운드. 아직 시작하지 않았으면 0.</summary>
        public int Round { get; private set; }

        /// <summary>이자 폭발을 갖고 있는가.</summary>
        public bool HasInterest => interest != null;

        /// <summary>저점 매수를 갖고 있는가.</summary>
        public bool HasLowBuy => lowBuy != null;
        public bool HasPettyCash => pettyCash != null;
        public bool HasBlindBet => blindBet != null;
        public bool HasDealerChoice => dealerChoice != null;

        /// <summary>룰렛에서 잃은 금액 중 돌려받는 비율(룰렛 환급). 없으면 0.</summary>
        public float RouletteRefundFraction => rouletteRefund != null
            ? Math.Max(0f, Math.Min(1f, rouletteRefund.Number("refund_fraction"))) : 0f;

        /// <summary>
        /// 라운드 사이 이월 한도 — 탐욕은 한도를 없애고, 쇼 미 더 머니는 <c>carryover_cap_bonus</c> 만큼 올린다.
        /// </summary>
        public CarryOverLimit CarryOverLimitFor(CarryOverLimit baseLimit)
        {
            if (greed != null)
            {
                return CarryOverLimit.Unlimited;
            }

            if (instantGold == null || baseLimit.IsUnlimited)
            {
                return baseLimit;
            }

            return CarryOverLimit.FromSetting(
                baseLimit.Gold + Math.Max(0, (int)Math.Floor(instantGold.Number("carryover_cap_bonus"))));
        }

        /// <summary>
        /// 축복을 고른 순간 한 번 받는 보상 — 쇼 미 더 머니·탐욕·저점 매수의 <c>bonus_gold</c>, 저점 매수의 물약.
        /// 같은 축복으로는 매치에 한 번이다.
        /// </summary>
        /// <remarks>
        /// 저점 매수는 여기서 "이번 라운드" 가 정해진다 — 고른 준비 단계의 라운드만 킬 점수가 배가 된다
        /// (<see cref="KillScoreMultiplierFor"/>, S-GD-KH2AJ6). 물약은 인벤토리가 여기 없으므로 쌓아 두고
        /// 어댑터가 <see cref="ConsumePendingItemGrants"/> 로 꺼내 지급한다.
        /// </remarks>
        /// <returns>이번에 받은 골드.</returns>
        public int GrantAcquisitionBonuses(Wallet wallet, string onlyEffect = null)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            int total = 0;
            foreach (BlessingMvpDefinition entry in new[] { instantGold, greed, lowBuy })
            {
                if (entry == null || (onlyEffect != null && entry.Effect != onlyEffect)
                    || !acquisitionGranted.Add(entry.Definition.Id)) continue;
                if (entry == lowBuy) AcquireLowBuy();
                int gold = Math.Max(0, (int)Math.Floor(entry.Number(BonusGoldKey)));
                if (gold <= 0) continue;
                wallet.Earn(gold);
                total += gold;
            }

            return total;
        }

        /// <summary>같은 개인 지갑을 사용하는 시험에 이미 지급한 즉시 보상의 기록을 전달한다.</summary>
        public void CopyAcquisitionHistoryFrom(BlessingEconomyState source, string effect)
        {
            if (source == null) return;
            foreach (string id in source.acquisitionGranted)
                if (BlessingMvpCatalog.TryGet(id, out var entry) && entry.Effect == effect)
                    acquisitionGranted.Add(id);
        }

        /// <summary>저점 매수를 고른 순간 — 이번 라운드 킬 점수 배율과 물약 지급을 정한다(S-GD-TMPBEF).</summary>
        private void AcquireLowBuy()
        {
            lowBuyRound = Round;
            float multiplier = lowBuy.Number(KillScoreMultiplierKey);
            lowBuyKillScoreMultiplier = float.IsNaN(multiplier) || multiplier < 0f ? 1f : multiplier;
            AddItemGrant(PlaceholderShopCatalog.HealthPotionItemId, lowBuy.Number(HealthPotionKey));
            AddItemGrant(PlaceholderShopCatalog.SwiftnessPotionItemId, lowBuy.Number(HastePotionKey));
        }

        private void AddItemGrant(int itemId, float quantity)
        {
            int count = float.IsNaN(quantity) ? 0 : (int)Math.Floor(quantity);
            if (count > 0) pendingItemGrants.Add(new BlessingItemGrant(itemId, count));
        }

        /// <summary>
        /// 고른 순간 받은 물건(저점 매수의 물약)을 꺼낸다. <b>한 번만 준다</b> — 꺼낸 뒤에는 비어 있다.
        /// 인벤토리에 자리가 없어 못 넣은 것은 사라진다(S-GD-KH2AJ6).
        /// </summary>
        public IReadOnlyList<BlessingItemGrant> ConsumePendingItemGrants()
        {
            if (pendingItemGrants.Count == 0) return Array.Empty<BlessingItemGrant>();
            var grants = pendingItemGrants.ToArray();
            pendingItemGrants.Clear();
            return grants;
        }

        /// <summary>
        /// 고른 순간 받은 물건을 인벤토리에 <b>한 개씩</b> 넣는다. 보유 한도(체력 물약 3·신속 물약 2 — S-GD-ZD76H8)나
        /// 칸이 모자라 들어가지 않은 것은 사라진다(S-GD-KH2AJ6). 인벤토리가 한도를 지키므로 여기서 다시 세지 않는다.
        /// </summary>
        /// <param name="snapshot">마지막으로 성공한 지급 뒤의 인벤토리. 화면 갱신 이벤트에 싣는다.</param>
        /// <returns>하나라도 넣었으면 true.</returns>
        public static bool DeliverItemGrants(
            IReadOnlyList<BlessingItemGrant> grants, IEquipmentInventoryDriver inventory,
            out EquipmentInventorySnapshot snapshot)
        {
            snapshot = default;
            if (grants == null || inventory == null) return false;
            bool delivered = false;
            for (int i = 0; i < grants.Count; i++)
            {
                for (int n = 0; n < grants[i].Quantity; n++)
                {
                    EquipmentInventoryOperationResult result = inventory.TryAuthorityPickup(grants[i].ItemId, 1);
                    if (!result.Succeeded) break;
                    snapshot = result.Snapshot;
                    delivered = true;
                }
            }

            return delivered;
        }

        /// <summary>
        /// 이 라운드의 킬 점수 배율 — 저점 매수를 고른 라운드면 <c>kill_score_multiplier</c>, 아니면 1(S-GD-TMPBEF).
        /// 최종 순위 점수의 킬 점수에만 곱한다. 킬 수(동점 처리 단계)는 바뀌지 않는다.
        /// </summary>
        public float KillScoreMultiplierFor(int round) =>
            lowBuyRound > 0 && round == lowBuyRound ? lowBuyKillScoreMultiplier : 1f;

        /// <summary>
        /// 이 플레이어에게 이 축복을 뽑기 후보로 보여도 되는가. 저점 매수는 <b>누적 순위 하위</b>
        /// <c>rank_fraction</c> 안에서만 나온다(엑셀 "순위 하위 N%이면 받을 수 있다", S-GD-TMPBEF). 나머지는 늘 true.
        /// </summary>
        /// <remarks>
        /// 판정은 <see cref="IsLowerHalf"/> 와 같다 — 1라운드처럼 순위표가 없거나 전원 동점이면 아무도 받지 못한다.
        /// </remarks>
        public static bool IsOfferable(
            BlessingDefinition definition, int playerId, IReadOnlyList<BlessingScore> standings)
        {
            if (definition == null || !BlessingMvpCatalog.TryGet(definition.Id, out var entry)
                || !string.Equals(entry.Effect, LowBuyEffect, StringComparison.Ordinal))
            {
                return true;
            }

            float fraction = entry.Number(RankFractionKey);
            if (standings == null || float.IsNaN(fraction) || fraction <= 0f || fraction >= 1f)
            {
                return false;
            }

            return IsLowerHalf(playerId, standings, fraction);
        }

        /// <summary>준비 단계가 열려 있는가. 닫히면 예치·블라인드 베팅을 받지 않는다.</summary>
        public bool IsPreparationOpen { get; private set; }

        /// <summary>
        /// 지금 예치를 <b>시도할 수 있는가</b>. 잔액은 보지 않는다 —
        /// 지갑은 <see cref="TryDeposit"/> 가 받으므로 여기서는 단계·보유·중복만 본다.
        /// </summary>
        public bool CanDeposit =>
            IsPreparationOpen && HasInterest && !InterestDeposited && !DepositDeclined && !RoundSettled;

        /// <summary>
        /// 이 라운드의 예치를 <b>거절했는가</b> (S-GD-XBP3VA). 거절하면 이번 준비 단계에서는 다시 묻지 않고 예치도 못 한다 —
        /// 다음 라운드 준비에서 초기화된다. 예치금·이자와 무관하므로 정산할 것이 없다.
        /// </summary>
        public bool DepositDeclined { get; private set; }

        /// <summary>블라인드 베팅을 이 라운드에 거절했는가.</summary>
        public bool BlindBetDeclined { get; private set; }

        /// <summary>이번 라운드에 예치했는가.</summary>
        public bool InterestDeposited { get; private set; }

        /// <summary><b>실제로 낸 원금.</b> 정산 환급의 기준이고, 예치하지 않았으면 0 이다.</summary>
        public int DepositAmount { get; private set; }

        /// <summary>
        /// <b>지금 예치한다면 낼 금액.</b> 지금 보유한 이자 폭발에서 읽으며(<see cref="TryDeposit"/> 와 같은 내림),
        /// 갖고 있지 않으면 0 이다 — 화면이 특정 등급 id 를 직접 찾아보지 않게 한다.
        ///
        /// <para><b><see cref="DepositAmount"/> 와 다르다.</b> 그쪽은 이미 낸 원금이라 보유가 바뀌어도 변하지 않는다.</para>
        /// </summary>
        public int DepositCost => interest == null ? 0 : (int)Math.Floor(interest.Number(DepositKey));

        /// <summary>이번 준비 단계에서 블라인드 베팅을 받아들였는가.</summary>
        public bool BlindBetAccepted { get; private set; }

        public int PreparationBonusGold => pettyCash == null ? 0 : (int)Math.Floor(pettyCash.Number("bonus_gold"));
        public int BlindBetBonusGold => blindBet == null ? 0 : (int)Math.Floor(blindBet.Number("bonus_gold"));
        public float BlindBetHealthCostFraction => BlindBetAccepted && blindBet != null
            ? blindBet.Number("hp_cost") : 0f;
        /// <summary>아직 수락하지 않은 화면도 표시할 수 있는 블라인드 베팅의 원래 체력 비용.</summary>
        public float BlindBetCostFraction => blindBet == null ? 0f : blindBet.Number("hp_cost");
        public int DealerChoiceFreeRerolls => dealerChoice == null ? 0 : Math.Max(0, (int)dealerChoice.Number("free_rerolls"));

        /// <summary>이번 라운드의 정산이 끝났는가. 끝난 뒤의 재정산은 0 을 돌려주고 아무것도 바꾸지 않는다.</summary>
        public bool RoundSettled { get; private set; }

        /// <summary>
        /// 가진 저점 매수의 하위권 비율 (기본 0.5 = 하위 50%). 저점 매수가 없으면 0 이다.
        /// 훈련장 시험 화면이 "지금 순위 프로필이면 하위권인가" 를 보여 줄 때 <see cref="IsLowerHalf"/> 에 넘긴다.
        /// </summary>
        public float LowBuyRankFraction => lowBuy == null ? 0f : lowBuy.Number(RankFractionKey);

        /// <summary>
        /// 준비 단계를 연다.
        ///
        /// <para>
        /// <b>같은 라운드로 다시 불러도 이번 라운드의 사용 기록은 지워지지 않는다</b> —
        /// 화면이 준비 단계를 다시 그리면서 이것을 부를 수 있고, 그때 예치 기록이 초기화되면
        /// <b>같은 라운드에 두 번 예치</b>할 수 있게 된다.
        /// </para>
        ///
        /// <para>
        /// 라운드는 앞으로만 간다. 지난 라운드로 되돌리는 호출은 정산 순서가 꼬였다는 뜻이므로 예외로 막는다.
        /// </para>
        /// </summary>
        public void BeginPreparation(int round, IReadOnlyList<BlessingDefinition> acquired)
        {
            if (round < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(round), round, "라운드는 1부터 시작합니다.");
            }

            if (acquired == null)
            {
                throw new ArgumentNullException(
                    nameof(acquired), "1라운드에도 빈 목록을 넘깁니다. null 은 허용하지 않습니다.");
            }

            if (round < Round)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(round), round, $"라운드는 뒤로 가지 않습니다. 현재 {Round}라운드입니다.");
            }

            if (round > Round)
            {
                if (InterestDeposited && !RoundSettled)
                {
                    // 생사 여부는 여기서 알 수 없다. 모른 채로 넘어가면 예치금이 조용히 사라지거나
                    // 조용히 돌아온다 — 둘 다 잔액만 틀리고 예외는 나지 않는 종류다.
                    throw new InvalidOperationException(
                        $"{Round}라운드의 예치금 {DepositAmount}G 가 정산되지 않았습니다. " +
                        $"{nameof(SettleRound)} 를 먼저 호출합니다.");
                }

                InterestDeposited = false;
                DepositAmount = 0;
                depositRefundFraction = 0f;
                BlindBetAccepted = false;
                // 거절은 그 라운드 한정이다 — 다음 준비 단계에서는 다시 고를 수 있다 (S-GD-XBP3VA).
                DepositDeclined = false;
                BlindBetDeclined = false;
                RoundSettled = false;
                Round = round;
                IsPreparationOpen = true;
            }

            ApplyLoadout(acquired);
        }

        /// <summary>준비 단계마다 소액 정산을 한 번만 지급한다.</summary>
        public bool GrantPreparationBonuses(Wallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (!IsPreparationOpen || pettyCash == null || pettyCashGrantedRound == Round) return false;
            int amount = PreparationBonusGold;
            pettyCashGrantedRound = Round;
            if (amount <= 0) return false;
            wallet.Earn(amount);
            return true;
        }

        /// <summary>준비 단계에서 블라인드 베팅을 명시적으로 수락하고 보너스를 받는다.</summary>
        public bool TryAcceptBlindBet(Wallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (!IsPreparationOpen || blindBet == null || BlindBetAccepted || BlindBetDeclined) return false;
            BlindBetAccepted = true;
            int amount = BlindBetBonusGold;
            if (amount > 0) wallet.Earn(amount);
            return true;
        }

        /// <summary>
        /// 축복을 새로 선택한 뒤 보유 목록을 갱신한다.
        /// <b>이번 라운드의 사용 기록(예치·블라인드 베팅)은 그대로 둔다.</b>
        /// </summary>
        public void UpdateLoadout(IReadOnlyList<BlessingDefinition> acquired)
        {
            if (acquired == null)
            {
                throw new ArgumentNullException(nameof(acquired));
            }

            ApplyLoadout(acquired);
        }

        /// <summary>
        /// 예치한다. 금액은 카탈로그의 <c>deposit</c> 값이고, <b>잔액이 모자라면 아무것도 차감하지 않는다.</b>
        /// </summary>
        /// <returns>예치했으면 true. 단계가 닫혔거나 이미 예치했거나 골드가 모자라면 false.</returns>
        public bool TryDeposit(Wallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (!CanDeposit)
            {
                return false;
            }

            var principal = DepositCost;

            if (principal <= 0)
            {
                // 0원 예치는 "예치했다" 는 상태만 만들고 아무 위험도 지지 않는다. 상태를 만들지 않는다.
                return false;
            }

            if (!wallet.TrySpend(principal))
            {
                return false;
            }

            InterestDeposited = true;
            DepositAmount = principal;
            // 엑셀 이자 폭발 t1 은 환급이 없다(전투 중 이자로 받는다) — 키가 없으면 0 이다.
            depositRefundFraction = interest.TryNumber(RefundFractionKey, out float refundFraction) ? refundFraction : 0f;
            return true;
        }

        /// <summary>준비 단계를 닫는다. 여러 번 불러도 같다.</summary>
        /// <summary>
        /// 예치를 거절한다 — 이번 준비 단계에서 더 묻지 않는다 (S-GD-XBP3VA). 돈은 오가지 않는다.
        /// 이미 예치했거나 이자 폭발이 없거나 준비 단계가 아니면 <c>false</c> 다.
        /// </summary>
        public bool DeclineDeposit()
        {
            if (!IsPreparationOpen || interest == null || InterestDeposited || DepositDeclined || RoundSettled)
            {
                return false;
            }

            DepositDeclined = true;
            return true;
        }

        /// <summary>블라인드 베팅을 거절한다. 보너스 골드도 체력 비용도 없다.</summary>
        public bool DeclineBlindBet()
        {
            if (!IsPreparationOpen || blindBet == null || BlindBetAccepted || BlindBetDeclined)
            {
                return false;
            }

            BlindBetDeclined = true;
            return true;
        }

        public void EndPreparation() => IsPreparationOpen = false;

        /// <summary>
        /// 라운드를 정산한다 — 살아남았으면 예치 원금의 <c>refund_fraction</c> 만큼 되돌려준다.
        ///
        /// <para>
        /// <b><see cref="Wallet.Refund"/> 다.</b> 예치금 환급은 번 것이 아니라 낸 것을 되돌리는 것이므로
        /// 누적 획득(<see cref="Wallet.TotalInflow"/>)을 늘리면 안 된다 (S-GD-MNFBV7).
        /// </para>
        /// </summary>
        /// <param name="round">정산하려는 라운드. 현재 라운드와 달라야 할 이유가 없다 — 다르면 예외다.</param>
        /// <param name="survived">그 라운드를 살아남았는가. 탈락이면 예치금은 사라진다.</param>
        /// <returns>환급한 골드. 예치가 없었거나 탈락했거나 이미 정산했으면 0.</returns>
        public int SettleRound(int round, bool survived, Wallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (Round < 1)
            {
                throw new InvalidOperationException("준비 단계를 시작하지 않은 상태에서는 정산할 것이 없습니다.");
            }

            if (round != Round)
            {
                // 지난 라운드를 뒤늦게 정산하거나 오지 않은 라운드를 미리 정산하는 것은
                // 호출 순서가 어긋났다는 뜻이다. 조용히 무시하면 예치금이 두 번 돌아간다.
                throw new ArgumentOutOfRangeException(
                    nameof(round), round, $"현재 라운드는 {Round}입니다. 다른 라운드는 정산할 수 없습니다.");
            }

            if (IsPreparationOpen)
            {
                // 준비 단계가 열린 채로 정산하면 정산 뒤에 다시 예치할 수 있게 된다.
                throw new InvalidOperationException(
                    $"{nameof(EndPreparation)} 로 준비 단계를 닫은 뒤에 정산합니다.");
            }

            if (RoundSettled)
            {
                return 0;
            }

            RoundSettled = true;

            if (!InterestDeposited || !survived)
            {
                return 0;
            }

            var refund = (int)Math.Floor(DepositAmount * (double)depositRefundFraction);

            if (refund <= 0)
            {
                return 0;
            }

            wallet.Refund(refund);
            return refund;
        }

        /// <summary>
        /// <b>누적 순위가 하위 <paramref name="rankFraction"/> 안인가</b> (저점 매수의 참가 조건).
        ///
        /// <para>
        /// 판정은 "나보다 <b>엄격히</b> 높은 점수가 몇 명인가" 로만 한다.
        /// 점수가 같은 사람은 같은 답을 받는다 — 경계에 동점자가 몰렸다고 누구는 되고 누구는 안 되는
        /// 자리를 만들지 않는다. 그 결과 하위 50% 라도 전원 동점이면 <b>아무도 자격이 없다.</b>
        /// </para>
        ///
        /// <para>
        /// <b>호출자가 직전 라운드까지의 완결된 누적 순위표를 준다.</b> 1라운드에는 그런 표가 없으므로
        /// 빈 목록이 들어오고 답은 false 다 — 시작하자마자 전원이 하위권이 되는 것을 막는다.
        /// </para>
        /// </summary>
        /// <param name="scores">직전 라운드까지의 누적 점수. 2명 미만이면 순위 자체가 성립하지 않는다.</param>
        /// <param name="rankFraction">0 초과 1 미만. 0.5 면 하위 50%.</param>
        public static bool IsLowerHalf(int playerId, IReadOnlyList<BlessingScore> scores, float rankFraction)
        {
            if (scores == null)
            {
                throw new ArgumentNullException(nameof(scores));
            }

            if (float.IsNaN(rankFraction) || rankFraction <= 0f || rankFraction >= 1f)
            {
                // 0 이면 아무도, 1 이면 전원이 하위권이다. 둘 다 "하위 N%" 라는 규칙이 아니라
                // 규칙이 꺼진 상태이므로 데이터 오류로 보고 상태를 바꾸기 전에 막는다.
                throw new ArgumentOutOfRangeException(
                    nameof(rankFraction), rankFraction, "하위 비율은 0 초과 1 미만이어야 합니다.");
            }

            var count = scores.Count;
            var found = false;
            var myScore = 0;

            for (var i = 0; i < count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (scores[j].PlayerId == scores[i].PlayerId)
                    {
                        // 같은 사람이 두 줄이면 "나보다 높은 사람" 수가 부풀어 판정이 조용히 뒤집힌다.
                        throw new ArgumentException(
                            $"순위표에 플레이어 {scores[i].PlayerId} 가 두 번 있습니다.", nameof(scores));
                    }
                }

                if (scores[i].PlayerId == playerId)
                {
                    found = true;
                    myScore = scores[i].Score;
                }
            }

            if (count < 2 || !found)
            {
                return false;
            }

            var higher = 0;
            for (var i = 0; i < count; i++)
            {
                if (scores[i].Score > myScore)
                {
                    higher++;
                }
            }

            // 하위 비율에 드는 인원은 floor(N × fraction) 명이고, 그 바깥에 있는 인원이 곧 기준선이다.
            // float 를 그대로 곱하면 0.3f 같은 값에서 N × (1 - f) 가 7.0000003 이 되어 올림이 한 칸 밀리므로
            // 아주 작은 여유를 두고 내림한 뒤 뺀다.
            var qualifying = (int)Math.Floor(count * (double)rankFraction + 1e-9);
            return higher >= count - qualifying;
        }

        private void ApplyLoadout(IReadOnlyList<BlessingDefinition> acquired)
        {
            interest = null;
            lowBuy = null;
            pettyCash = null;
            blindBet = null;
            dealerChoice = null;
            rouletteRefund = null;
            instantGold = null;
            greed = null;

            if (acquired == null)
            {
                return;
            }

            for (var i = 0; i < acquired.Count; i++)
            {
                var definition = acquired[i];

                if (definition == null || !BlessingMvpCatalog.TryGet(definition.Id, out var entry))
                {
                    continue;
                }

                if (interest == null && (string.Equals(entry.Effect, InterestEffect, StringComparison.Ordinal)
                    || string.Equals(entry.Effect, InterestIncomeEffect, StringComparison.Ordinal)))
                {
                    interest = entry;
                }
                else if (rouletteRefund == null && string.Equals(entry.Effect, RouletteRefundEffect, StringComparison.Ordinal))
                {
                    rouletteRefund = entry;
                }
                else if (instantGold == null && string.Equals(entry.Effect, InstantGoldEffect, StringComparison.Ordinal))
                {
                    instantGold = entry;
                }
                else if (greed == null && string.Equals(entry.Effect, GreedEffect, StringComparison.Ordinal))
                {
                    greed = entry;
                }
                else if (lowBuy == null && string.Equals(entry.Effect, LowBuyEffect, StringComparison.Ordinal))
                {
                    lowBuy = entry;
                }
                else if (pettyCash == null && string.Equals(entry.Effect, PettyCashEffect, StringComparison.Ordinal))
                {
                    pettyCash = entry;
                }
                else if (blindBet == null && string.Equals(entry.Effect, BlindBetEffect, StringComparison.Ordinal))
                {
                    blindBet = entry;
                }
                else if (dealerChoice == null && string.Equals(entry.Effect, DealerChoiceEffect, StringComparison.Ordinal))
                {
                    dealerChoice = entry;
                }
            }
        }
    }
}
