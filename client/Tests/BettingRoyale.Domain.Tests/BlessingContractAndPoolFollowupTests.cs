using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 09.21 후속 2차 — 준비 계약 거절·이자 지급 종료(S-GD-XBP3VA)와 축복 풀 규칙(S-GD-H78GRS).
    /// </summary>
    [TestFixture]
    public sealed class BlessingContractAndPoolFollowupTests
    {
        private const int Seed = 20260921;

        // ── 준비 계약 거절 (S-GD-XBP3VA) ─────────────────────────────────────
        /// <summary>코드 카탈로그의 이자 폭발 — 효과 이름으로 찾으므로 카탈로그에 있는 정의를 그대로 쓴다.</summary>
        private static BlessingDefinition Interest
        {
            get
            {
                Assert.That(BlessingMvpCatalog.TryGet("interest-burst-t3", out BlessingMvpDefinition entry), Is.True);
                return entry.Definition;
            }
        }

        private static BlessingEconomyState WithInterest()
        {
            var state = new BlessingEconomyState(new[] { Interest });
            state.BeginPreparation(1, new[] { Interest });
            return state;
        }

        [Test]
        public void 예치를_거절하면_이번_라운드에는_다시_예치할_수_없고_돈이_오가지_않는다()
        {
            BlessingEconomyState state = WithInterest();
            var wallet = new Wallet(1000);
            Assert.That(state.CanDeposit, Is.True);
            Assert.That(state.DepositDeclined, Is.False);

            Assert.That(state.DeclineDeposit(), Is.True);

            Assert.That(state.DepositDeclined, Is.True);
            Assert.That(state.CanDeposit, Is.False, "거절한 라운드에는 예치가 닫힌다");
            Assert.That(state.TryDeposit(wallet), Is.False);
            Assert.That(wallet.Gold, Is.EqualTo(1000), "거절·거절 후 시도는 지갑을 건드리지 않는다");
            Assert.That(state.DeclineDeposit(), Is.False, "두 번 거절하지 않는다");
        }

        [Test]
        public void 잔액이_없어도_거절할_수_있고_이미_예치했으면_거절할_수_없다()
        {
            BlessingEconomyState broke = WithInterest();
            Assert.That(broke.DeclineDeposit(), Is.True, "거절은 비용이 없으므로 잔액과 무관하다");

            BlessingEconomyState deposited = WithInterest();
            Assert.That(deposited.TryDeposit(new Wallet(1000)), Is.True);
            Assert.That(deposited.DeclineDeposit(), Is.False, "이미 낸 돈을 거절로 되돌리지 않는다");
        }

        [Test]
        public void 거절은_그_라운드_한정이고_다음_준비에서_다시_고를_수_있다()
        {
            BlessingEconomyState state = WithInterest();
            state.DeclineDeposit();
            state.EndPreparation();
            state.SettleRound(1, survived: true, new Wallet(0));

            state.BeginPreparation(2, new[] { Interest });

            Assert.That(state.DepositDeclined, Is.False);
            Assert.That(state.CanDeposit, Is.True);
            var wallet = new Wallet(1000);
            Assert.That(state.TryDeposit(wallet), Is.True);
            Assert.That(wallet.Gold, Is.LessThan(1000));
        }

        [Test]
        public void 거절한_계약은_준비_창에_다시_뜨지_않는다()
        {
            BlessingEconomyState state = WithInterest();
            var open = BlessingContractStateChanged.From(1, true, preparation: true, state, gold: 1000);
            Assert.That(open.HasInterest, Is.True);
            Assert.That(open.DepositDeclined, Is.False);
            Assert.That(open.CanDeposit, Is.True);

            state.DeclineDeposit();
            var declined = BlessingContractStateChanged.From(1, true, preparation: true, state, gold: 1000);

            Assert.That(declined.DepositDeclined, Is.True);
            Assert.That(declined.CanDeposit, Is.False);
            // 화면 조건은 UI 레이어(BlessingContractEffects.HasPendingChoice)가 같은 필드로 판단한다.
            Assert.That(declined.HasInterest && !declined.Deposited && !declined.DepositDeclined, Is.False);
            Assert.That(declined.ForViewer(false).DepositDeclined, Is.True, "관전 시점으로 바꿔도 거절 상태가 남는다");
        }

        // ── 라운드가 끝나면 이자가 멈춘다 (S-GD-XBP3VA) ──────────────────────
        private static BlessingCombatState InterestState()
        {
            const string id = "interest-burst-t1";
            var definition = new BlessingDefinition(id, "이자 폭발", WeaponTag.Common, BlessingTier.Silver);
            var behavior = new BlessingMvpDefinition(definition, "interest_income", BlessingMvpDefinition.MissingIconIndex,
                BlessingStackRule.Unique, BlessingDurationKind.Permanent,
                new[] { "deposit", "start_seconds", "interval", "interest", "kill_bonus", "first_place_bonus" },
                new[] { 300f, 20f, 5f, 100f, 300f, 500f });
            GameContentProvider.Set(new GameContent(3, null, new[] { definition }, null, null, null,
                new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal) { [id] = behavior }));
            var state = new BlessingCombatState(new[] { definition });
            state.Tick(1f, 0f, combatActive: true, moving: false);
            state.SetInterestDeposited(true);
            return state;
        }

        [TearDown]
        public void ResetContent() => GameContentProvider.Reset();

        [Test]
        public void 라운드가_끝나면_시간이_지나도_이자가_더_들어오지_않는다()
        {
            BlessingCombatState state = InterestState();
            Assert.That(state.IsCombatRoundRunning, Is.True, "국면을 밀어 넣지 않는 경로는 예전처럼 돈다");
            state.Tick(20f, 20f, true, false);
            state.Tick(30f, 30f, true, false);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(300), "20·25·30 초 이자");

            // 정산·다음 준비에서도 combatActive 는 참이다(준비 단계에도 공격이 허용된다).
            // 예전에는 그 때문에 라운드가 끝난 뒤에도 이자가 계속 들어왔다.
            state.SetCombatRoundRunning(false);
            Assert.That(state.InterestIncomeActive, Is.False, "효과 칸에서도 내려간다");
            state.Tick(60f, 60f, true, false);
            state.Tick(120f, 120f, true, false);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(0), "라운드가 끝나면 멈춘다");

            state.SetCombatRoundRunning(true);
            state.Tick(125f, 125f, true, false);
            Assert.That(state.ConsumeEarnedGold(), Is.GreaterThan(0), "다음 라운드 전투가 열리면 다시 지급된다");
        }

        [Test]
        public void 라운드가_멈춰도_처치_보너스는_그_사건의_지급이라_따로다()
        {
            BlessingCombatState state = InterestState();
            state.SetCombatRoundRunning(false);
            state.NotifyKill(5f, 9);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(300), "처치 보너스는 주기 이자가 아니다");
        }

        // ── 노출된 축복 재노출 금지 (S-GD-H78GRS) ────────────────────────────
        private static IReadOnlyList<BlessingDefinition> Pool(int commonPerTier = 12, bool activesInSilver = false)
        {
            var pool = new List<BlessingDefinition>();
            foreach (BlessingTier tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
                for (int i = 0; i < commonPerTier; i++)
                    pool.Add(new BlessingDefinition($"common_{tier}_{i}", $"공용{tier}{i}", WeaponTag.Common, tier));
            if (activesInSilver)
            {
                pool.Add(new BlessingDefinition("active_a", "액티브A", WeaponTag.Common, BlessingTier.Silver, isActive: true));
                pool.Add(new BlessingDefinition("active_b", "액티브B", WeaponTag.Common, BlessingTier.Silver, isActive: true));
                pool.Add(new BlessingDefinition("active_c", "액티브C", WeaponTag.Common, BlessingTier.Silver, isActive: true));
            }

            return pool;
        }

        private static BlessingDraft Begin(IReadOnlyList<BlessingDefinition> pool, int round,
            BlessingLoadout acquired, ICollection<string> exposed, int seed = Seed) =>
            BlessingDraft.Begin(BlessingConfig.Default, pool, WeaponTag.Sword, round,
                acquired, new DeterministicRandom(seed), exposedIds: exposed);

        [Test]
        public void 한_번_뜬_축복은_재추첨에서_다시_뜨지_않는다()
        {
            var exposed = new HashSet<string>(StringComparer.Ordinal);
            BlessingDraft draft = Begin(Pool(), 1, new BlessingLoadout(), exposed);
            var seen = new List<string>(draft.Candidates.Select(c => c.Blessing.Id));
            Assert.That(seen.Count, Is.EqualTo(3));
            Assert.That(exposed, Is.EquivalentTo(seen), "뽑는 즉시 노출 기록에 남는다");

            var wallet = new Wallet(100000);
            for (int reroll = 0; reroll < 3; reroll++)
            {
                Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success));
                foreach (BlessingCandidate candidate in draft.Candidates)
                {
                    Assert.That(seen.Contains(candidate.Blessing.Id), Is.False,
                        $"재추첨 {reroll + 1}회에서 이미 본 '{candidate.Blessing.Id}' 가 다시 떴다");
                    seen.Add(candidate.Blessing.Id);
                }
            }

            Assert.That(seen.Count, Is.EqualTo(seen.Distinct().Count()));
        }

        [Test]
        public void 잠근_후보는_재추첨에서_자리를_지키고_노출_기록이_그것을_밀어내지_않는다()
        {
            var exposed = new HashSet<string>(StringComparer.Ordinal);
            BlessingDraft draft = Begin(Pool(), 1, new BlessingLoadout(), exposed);
            string locked = draft.Candidates[0].Blessing.Id;
            Assert.That(draft.TryToggleLock(0), Is.True);

            Assert.That(draft.TryReroll(new Wallet(100000)), Is.EqualTo(RerollResult.Success));

            Assert.That(draft.Candidates[0].Blessing.Id, Is.EqualTo(locked));
            Assert.That(draft.Candidates.Skip(1).Any(c => c.Blessing.Id == locked), Is.False);
        }

        [Test]
        public void 노출_기록은_다음_준비_단계에서도_이어진다()
        {
            var exposed = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<BlessingDefinition> pool = Pool();
            var acquired = new BlessingLoadout();

            BlessingDraft first = Begin(pool, 1, acquired, exposed);
            first.TryReroll(new Wallet(100000));
            var silverSeen = new HashSet<string>(exposed, StringComparer.Ordinal);
            acquired.Add(first.Select(0));

            // 같은 등급을 다시 제시하는 경로(등급 승급 축복·연습)에서도 이미 본 것은 빠진다.
            BlessingDraft again = Begin(pool, 1, acquired, exposed, seed: Seed + 1);

            foreach (BlessingCandidate candidate in again.Candidates)
                Assert.That(silverSeen.Contains(candidate.Blessing.Id), Is.False, candidate.Blessing.Id);
        }

        [Test]
        public void 등급_풀을_다_소진하면_예외를_던지지_않고_이미_본_것으로_채운다()
        {
            // 실버 공용 4개뿐인 풀에서 계속 재추첨한다 — 노출 기록이 풀을 넘어선다.
            var exposed = new HashSet<string>(StringComparer.Ordinal);
            BlessingDraft draft = Begin(Pool(commonPerTier: 4), 1, new BlessingLoadout(), exposed);
            var wallet = new Wallet(1000000);

            for (int i = 0; i < 6; i++)
            {
                Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success), $"{i + 1}회 재추첨");
                Assert.That(draft.Candidates.Count(c => c.Blessing != null), Is.EqualTo(3), "후보 3개는 늘 채운다");
            }

            Assert.That(exposed.Count, Is.EqualTo(4), "풀 전체가 노출 기록에 들어간다");
        }

        // ── 액티브 단일 슬롯 배타 (S-GD-H78GRS · S-GD-NJQ5NM) ────────────────
        [Test]
        public void 액티브를_이미_가졌으면_다른_액티브가_후보에_뜨지_않는다()
        {
            IReadOnlyList<BlessingDefinition> pool = Pool(commonPerTier: 6, activesInSilver: true);
            var acquired = new BlessingLoadout();
            acquired.Add(pool.First(p => p.Id == "active_a"));

            for (int seed = 0; seed < 40; seed++)
            {
                BlessingDraft draft = Begin(pool, 1, acquired, null, Seed + seed);
                Assert.That(draft.Candidates.Any(c => c.Blessing.IsActive), Is.False,
                    "액티브를 들고 있으면 후보에 액티브가 없다");
            }
        }

        [Test]
        public void 액티브가_없어도_한_판에_액티브는_최대_하나만_뜬다()
        {
            IReadOnlyList<BlessingDefinition> pool = Pool(commonPerTier: 3, activesInSilver: true);
            var wallet = new Wallet(1000000);

            for (int seed = 0; seed < 40; seed++)
            {
                BlessingDraft draft = Begin(pool, 1, new BlessingLoadout(), null, Seed + seed);
                Assert.That(draft.Candidates.Count(c => c.Blessing.IsActive), Is.LessThanOrEqualTo(1),
                    "게임패드 액티브 버튼이 하나뿐이라 후보도 하나여야 한다 (S-GD-NJQ5NM §2)");
                for (int reroll = 0; reroll < 2; reroll++)
                {
                    if (draft.TryReroll(wallet) != RerollResult.Success) break;
                    Assert.That(draft.Candidates.Count(c => c.Blessing.IsActive), Is.LessThanOrEqualTo(1));
                }
            }
        }

        [Test]
        public void 액티브를_고른_다음_준비_단계에는_액티브가_남아_있지_않다()
        {
            IReadOnlyList<BlessingDefinition> pool = Pool(commonPerTier: 6, activesInSilver: true);
            var exposed = new HashSet<string>(StringComparer.Ordinal);
            var acquired = new BlessingLoadout();
            BlessingDraft first = Begin(pool, 1, acquired, exposed);
            int activeIndex = -1;
            for (int i = 0; i < first.Candidates.Count; i++) if (first.Candidates[i].Blessing.IsActive) activeIndex = i;
            if (activeIndex < 0)
            {
                // 이 시드에서 액티브가 안 떴으면 규칙 자체는 위 테스트가 덮는다. 보유만 만들어 계속 본다.
                acquired.Add(pool.First(p => p.Id == "active_a"));
            }
            else
            {
                acquired.Add(first.Select(activeIndex));
            }

            Assert.That(acquired.HasActive, Is.True);
            BlessingDraft second = Begin(pool, 1, acquired, exposed, Seed + 7);
            Assert.That(second.Candidates.Any(c => c.Blessing.IsActive), Is.False);
        }
    }
}
