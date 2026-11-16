using System;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.LocalPlay;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// <see cref="LocalPlaySessionState"/> 가 경제형 축복의 <b>소유자</b>로서 지키는 계약.
    ///
    /// <para>
    /// <b>실제 카탈로그·지갑을 쓴다.</b> Local Play 는 후보 시드를 밖에서 주입받지 않으므로,
    /// 특정 축복이 필요한 검증은 <b>재추첨으로 후보를 더 열어보는</b> 한정된 탐색으로 접근한다 —
    /// 추첨 순서를 가정하지 않고, 뽑히지 않은 경우에도 "없으면 거부한다" 는 같은 계약을 확인한다.
    /// </para>
    ///
    /// <para>
    /// 등급은 라운드가 정한다 (S-GD-HE87JY) — 1R 실버 · 2R 골드 · 3R 플래티넘. 그래서
    /// 저점 매수(골드)는 2라운드, 이자 폭발(플래티넘)은 3라운드에만 후보가 될 수 있다.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class LocalPlayBlessingIntegrationTests
    {
        private const string InterestId = "interest-burst-t3";
        private const string LowBuyId = "low-buy-t2";

        /// <summary>월드가 부여하는 로컬 플레이어 id. 순위표의 어느 줄이 나인지 가리킨다.</summary>
        private const int LocalPlayerId = 0;

        /// <summary>
        /// 노리는 축복이 후보에 뜨게 만들 때 바꿔보는 <b>앞 라운드 재추첨 조합</b>의 상한.
        /// 앞 라운드마다 0~3번씩 재추첨한 조합을 차례로 본다. 넘으면 상황을 만들지 못한 것이므로 실패로 본다.
        /// </summary>
        /// <remarks>
        /// 1라운드만 바꾸던 예전 방식은 저점 매수가 1·2라운드 후보에서 빠지면서(하위권만 받는다 — S-GD-TMPBEF)
        /// 추첨 수열이 달라져 3라운드 탐색이 모자랐다. 라운드마다 따로 바꿔 탐색 폭을 넓힌다.
        /// </remarks>
        private const int WarmupRerollLimit = 63;

        private static int DepositAmount => (int)Math.Floor(Mvp(InterestId).Number("deposit"));

        private static int RefundAmount =>
            (int)Math.Floor(DepositAmount * (double)Mvp(InterestId).Number("refund_fraction"));


        private static BlessingMvpDefinition Mvp(string blessingId)
        {
            Assert.That(BlessingMvpCatalog.TryGet(blessingId, out var entry), Is.True, blessingId);
            return entry;
        }

        private static LocalPlaySessionState NewSession() =>
            new LocalPlaySessionState(LocalPlaySettings.Default);

        /// <summary>로컬 플레이어가 꼴찌인 순위표. 4인 기준 하위 50% 안이다.</summary>
        private static BlessingScore[] LowestStandings() => new[]
        {
            new BlessingScore(LocalPlayerId, 0),
            new BlessingScore(1, 100),
            new BlessingScore(2, 100),
            new BlessingScore(3, 100),
        };

        /// <summary>
        /// 준비 로딩 상태에서 시작해 한 라운드를 끝까지 진행한다 (선택은 첫 후보).
        /// </summary>
        private static void PlayRound(LocalPlaySessionState state)
        {
            var round = state.CurrentRound;
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);
            FinishCombatRound(state, round);
        }

        /// <summary>
        /// 선택이 끝난 준비 단계에서 전투를 지나 다음 라운드의 준비 로딩까지 간다.
        /// 정산은 <b>테스트가 월드 대신</b> 생존 사실로 확정한다 — 상태 기계는 스스로 정산하지 않는다.
        /// </summary>
        private static void FinishCombatRound(LocalPlaySessionState state, int round)
        {
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatStarted));
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));
            Assert.That(state.TrySettleBlessingRound(round, true), Is.True);
            state.SkipCurrentPhase();
        }

        /// <summary>
        /// 노리는 축복을 <paramref name="targetRound"/> 준비 단계에 들고 있는 세션을 만든다.
        ///
        /// <para>
        /// 후보 추첨 시드는 상태 안에 고정돼 있어 밖에서 지정할 수 없다. 그래서 <b>앞 라운드의 재추첨
        /// 횟수를 바꿔가며 같은 흐름을 다시 재생한다</b> — 기존 경제형 축복 테스트가 시드를 바꿔가며
        /// 상황을 만드는 것과 같은 방식이고, 추첨 순서를 가정하지 않는다.
        /// </para>
        /// </summary>
        /// <param name="standings">목표 라운드의 준비가 열리기 <b>전</b>에 넣을 순위표. 없으면 null.</param>
        private static LocalPlaySessionState BuildSessionHolding(
            string blessingId, int targetRound, BlessingScore[] standings)
        {
            for (var warmupRerolls = 0; warmupRerolls <= WarmupRerollLimit; warmupRerolls++)
            {
                var state = NewSession();
                Assert.That(state.TryStartMatch(out _), Is.True);

                for (var round = 1; round < targetRound; round++)
                {
                    Assert.That(state.TryCompleteMapLoading(out _), Is.True);

                    // 재추첨은 추첨 수열을 앞으로 밀어 다음 라운드의 후보를 바꾼다. 라운드마다 0~3번.
                    var rerolls = warmupRerolls / (1 << (2 * (round - 1))) % 4;
                    for (var i = 0; i < rerolls && state.TryRerollAugmentOffers(); i++)
                    {
                    }

                    Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);
                    FinishCombatRound(state, round);
                }

                if (standings != null)
                {
                    state.SetBlessingStandings(LocalPlayerId, standings);
                }

                Assert.That(state.CurrentRound, Is.EqualTo(targetRound));
                Assert.That(state.TryCompleteMapLoading(out _), Is.True);

                if (TryAcquireFromOffers(state, blessingId))
                {
                    return state;
                }
            }

            Assert.Fail(
                $"{WarmupRerollLimit + 1}번 재생해도 {targetRound}라운드 후보에 {blessingId} 가 나오지 않았습니다. " +
                "탐색 상한을 올리거나 카탈로그 구성을 확인하세요.");
            return null;
        }

        /// <summary>전투까지 진행한다 (1라운드, 첫 후보 선택).</summary>
        private static LocalPlaySessionState StartCombat()
        {
            var state = NewSession();
            Assert.That(state.TryStartMatch(out _), Is.True);
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatStarted));
            return state;
        }

        /// <summary>
        /// 이번 준비 단계의 후보에서 <paramref name="blessingId"/> 를 찾아 고른다.
        /// 없으면 <b>골드가 닿는 만큼</b> 재추첨하며 찾고, 끝내 없으면 첫 후보를 고른다.
        /// </summary>
        /// <returns>노린 축복을 획득했으면 true.</returns>
        private static bool TryAcquireFromOffers(LocalPlaySessionState state, string blessingId)
        {
            while (true)
            {
                if (state.AugmentOffers.TryGetIndex(blessingId, out _))
                {
                    Assert.That(state.TrySelectAugment(blessingId, out _), Is.True);
                    return true;
                }

                if (!state.TryRerollAugmentOffers())
                {
                    // 재추첨 비용은 누를수록 두 배가 되므로(S-GD-SHJGNB) 이 반복은 반드시 끝난다.
                    Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);
                    return false;
                }
            }
        }

        // ── 준비 경계 ─────────────────────────────────────────────────────

        [Test]
        public void 준비가_열리면_같은_라운드로_축복_계약도_열린다()
        {
            var state = NewSession();

            Assert.That(state.BlessingEconomy.Round, Is.EqualTo(0));
            Assert.That(state.TryDepositBlessing(), Is.False, "로비에서는 받지 않는다");

            Assert.That(state.TryStartMatch(out _), Is.True);
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);

            Assert.That(state.BlessingEconomy.Round, Is.EqualTo(1));
            Assert.That(state.BlessingEconomy.IsPreparationOpen, Is.True);
            Assert.That(state.Gold, Is.EqualTo(EconomyConfig.DefaultRoundGrantGold), "지급 뒤에 준비가 열린다");

            Assert.That(state.TryDepositBlessing(), Is.False, "축복 선택 전에는 받지 않는다");

            Assert.That(state.TrySelectAugment(state.AugmentOffers.First.Id, out _), Is.True);

            // 1라운드는 실버 등급이라 이자 폭발(플래티넘)도 저점 매수(골드)도 가질 수 없다.
            Assert.That(state.BlessingEconomy.HasInterest, Is.False);
            Assert.That(state.BlessingEconomy.HasLowBuy, Is.False);
            Assert.That(state.TryDepositBlessing(), Is.False, "이자 폭발이 없으면 예치할 것이 없다");

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.BlessingEconomy.IsPreparationOpen, Is.False, "준비를 떠나면 닫힌다");
            Assert.That(state.TryDepositBlessing(), Is.False);
        }

        // ── 정산 경계 ─────────────────────────────────────────────────────

        [Test]
        public void 정산은_월드가_생사를_확정해_부를_때만_한_번_된다()
        {
            var state = StartCombat();

            Assert.That(state.TrySettleBlessingRound(2, true), Is.False, "다른 라운드는 정산하지 않는다");

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));

            var gold = state.Gold;

            Assert.That(state.TrySettleBlessingRound(1, true), Is.True);
            Assert.That(state.BlessingEconomy.RoundSettled, Is.True);
            Assert.That(state.Gold, Is.EqualTo(gold), "예치가 없으면 돌려줄 것도 없다");
            Assert.That(state.TrySettleBlessingRound(1, true), Is.False, "두 번째 정산은 없다");
        }

        [Test]
        public void 로비에서는_정산할_것이_없다()
        {
            var state = NewSession();

            Assert.That(state.TrySettleBlessingRound(1, true), Is.False);
            Assert.That(state.TryStartMatch(out _), Is.True);
            Assert.That(state.TrySettleBlessingRound(1, true), Is.False, "준비를 열기 전에는 정산할 라운드가 없다");
        }

        [Test]
        public void 정산한_라운드는_다음_라운드에서_다시_열린다()
        {
            var state = StartCombat();

            // 탈락 사실도 정산이다 — 예치금을 잃은 것으로 확정된다.
            Assert.That(state.TrySettleBlessingRound(1, false), Is.True);
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.PreparationMapLoadingStarted));
            Assert.That(state.CurrentRound, Is.EqualTo(2));

            Assert.That(state.TryCompleteMapLoading(out _), Is.True);

            Assert.That(state.BlessingEconomy.Round, Is.EqualTo(2));
            Assert.That(state.BlessingEconomy.IsPreparationOpen, Is.True);
            Assert.That(state.BlessingEconomy.RoundSettled, Is.False);
            Assert.That(state.BlessingEconomy.InterestDeposited, Is.False);
        }

        // ── 순위표 ───────────────────────────────────────────────────────

        [Test]
        public void 비었거나_중복이거나_동점인_순위표는_자격을_주지_않는다()
        {
            var state = StartCombat();
            Assert.That(state.TrySettleBlessingRound(1, true), Is.True);

            // 월드가 한 프레임 늦게 채우거나 잘못 채운 표가 그대로 들어올 수 있다. 던지지 않는다.
            state.SetBlessingStandings(LocalPlayerId, null);
            state.SetBlessingStandings(LocalPlayerId, Array.Empty<BlessingScore>());
            state.SetBlessingStandings(
                LocalPlayerId,
                new[] { new BlessingScore(LocalPlayerId, 0), new BlessingScore(LocalPlayerId, 50) });
            state.SetBlessingStandings(
                LocalPlayerId,
                new[] { new BlessingScore(1, 100), new BlessingScore(2, 100) });
            state.SetBlessingStandings(
                LocalPlayerId,
                new[] { new BlessingScore(LocalPlayerId, 100), new BlessingScore(1, 100) });

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.PreparationMapLoadingStarted));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);

            // 자격은 저점 매수가 뽑기 후보로 나오는가로 드러난다(S-GD-TMPBEF). 마지막 표는 동점이라 하위권이 아니다.
            do
            {
                Assert.That(state.AugmentOffers.TryGetIndex(LowBuyId, out _), Is.False,
                    "망가졌거나 동점인 순위표로는 저점 매수가 후보로 나오지 않는다");
            }
            while (state.TryRerollAugmentOffers());
        }

        // ── 매치 경계 ─────────────────────────────────────────────────────

        [Test]
        public void 새_매치와_취소는_축복_계약을_새로_만든다()
        {
            var state = NewSession();
            Assert.That(state.TryStartMatch(out _), Is.True);
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);

            var first = state.BlessingEconomy;
            Assert.That(first.Round, Is.EqualTo(1));

            Assert.That(state.TryAbortMatch(), Is.True);
            Assert.That(state.BlessingEconomy, Is.Not.SameAs(first), "취소한 매치의 예치 기록을 물려받지 않는다");
            Assert.That(state.BlessingEconomy.Round, Is.EqualTo(0));
            Assert.That(state.Gold, Is.EqualTo(0));

            Assert.That(state.TryStartMatch(out _), Is.True);
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.BlessingEconomy, Is.Not.SameAs(first));
            Assert.That(state.BlessingEconomy.Round, Is.EqualTo(1));
        }

        // ── 두 축복의 실제 경로 ───────────────────────────────────────────

        [Test]
        public void 저점_매수를_가진_하위권은_고르는_순간_보상을_받는다()
        {
            // 자격은 월드가 넣어준 직전 순위표로만 정한다 — 2라운드 준비가 열리기 전에 들어간다(S-GD-TMPBEF).
            var state = BuildSessionHolding(LowBuyId, targetRound: 2, standings: LowestStandings());

            Assert.That(state.BlessingEconomy.HasLowBuy, Is.True);
            Assert.That(state.BlessingEconomy.KillScoreMultiplierFor(2),
                Is.EqualTo(Mvp(LowBuyId).Number("kill_score_multiplier")), "고른 라운드의 킬 점수 배율이 정해졌다");
            Assert.That(state.BlessingEconomy.ConsumePendingItemGrants(), Has.Count.EqualTo(2),
                "체력·신속 물약은 월드가 꺼내 인벤토리에 넣는다");
            Assert.That(state.BlessingEconomy.GrantAcquisitionBonuses(new Wallet(0)), Is.Zero, "골드는 이미 받았다");
        }

        [Test]
        public void 하위권이_아니면_저점_매수가_후보에_없다()
        {
            var state = NewSession();
            Assert.That(state.TryStartMatch(out _), Is.True);
            PlayRound(state);
            state.SetBlessingStandings(LocalPlayerId, new[]
            {
                new BlessingScore(LocalPlayerId, 100),
                new BlessingScore(1, 0),
                new BlessingScore(2, 0),
                new BlessingScore(3, 0),
            });

            Assert.That(state.CurrentRound, Is.EqualTo(2));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            do
            {
                Assert.That(state.AugmentOffers.TryGetIndex(LowBuyId, out _), Is.False, "1등에게는 후보로 나오지 않는다");
            }
            while (state.TryRerollAugmentOffers());
        }

        [Test]
        public void 이자_폭발을_가지면_예치하고_생존_정산에서_환급받는다()
        {
            var state = BuildSessionHolding(InterestId, targetRound: 3, standings: null);

            Assert.That(state.BlessingEconomy.HasInterest, Is.True);

            var goldBefore = state.Gold;
            var inflowBefore = state.TotalGoldInflow;

            Assert.That(state.TryDepositBlessing(), Is.True);
            Assert.That(state.Gold, Is.EqualTo(goldBefore - DepositAmount));
            Assert.That(state.TotalGoldInflow, Is.EqualTo(inflowBefore), "예치는 지출이다");
            Assert.That(state.TryDepositBlessing(), Is.False, "같은 라운드에 두 번 예치되지 않는다");

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatStarted));
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));

            Assert.That(state.TrySettleBlessingRound(3, true), Is.True);
            Assert.That(state.Gold, Is.EqualTo(goldBefore - DepositAmount + RefundAmount));
            Assert.That(state.TotalGoldInflow, Is.EqualTo(inflowBefore), "환급은 번 것이 아니다 (S-GD-MNFBV7)");
            Assert.That(state.TrySettleBlessingRound(3, true), Is.False, "환급은 한 번뿐이다");
            Assert.That(state.Gold, Is.EqualTo(goldBefore - DepositAmount + RefundAmount));
        }

        [Test]
        public void 탈락_정산은_예치금을_돌려주지_않는다()
        {
            var state = BuildSessionHolding(InterestId, targetRound: 3, standings: null);

            Assert.That(state.TryDepositBlessing(), Is.True);
            var goldAfterDeposit = state.Gold;

            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatMapLoadingStarted));
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatStarted));
            Assert.That(state.SkipCurrentPhase(), Is.EqualTo(LocalPlayTransition.CombatSummaryStarted));

            Assert.That(state.TrySettleBlessingRound(3, false), Is.True);
            Assert.That(state.Gold, Is.EqualTo(goldAfterDeposit), "탈락하면 예치금은 사라진다");
        }
    }
}
