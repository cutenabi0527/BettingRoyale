using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// Jira S15P21A606-31 "준비 골드·증강·라운드 정산 코어" 의 <b>완료 조건별</b> 검증.
    ///
    /// 테스트 이름을 완료 조건 문장에 맞춰 붙였다 — 티켓의 체크박스와 1:1 로 대조할 수 있어야
    /// "검증 증거" 를 따로 만들지 않아도 되기 때문이다.
    /// </summary>
    [TestFixture]
    public class MatchEconomyStateTests
    {
        private const int Alice = 1;
        private const int Bob = 2;
        private const int Seed = 0x31;

        /// <summary>
        /// 등급마다 무기별로 충분한 축복을 만든다.
        /// 후보가 항상 3개여야 하므로(S-GD-S46S28) 등급당 공용 4 + 검 2 + 활 2 를 둔다.
        /// </summary>
        private static IReadOnlyList<BlessingDefinition> BuildPool()
        {
            var pool = new List<BlessingDefinition>();
            foreach (BlessingTier tier in Enum.GetValues(typeof(BlessingTier)))
            {
                for (var i = 0; i < 4; i++)
                {
                    pool.Add(new BlessingDefinition($"{tier}-common-{i}", $"{tier} 공용 {i}", WeaponTag.Common, tier));
                }

                for (var i = 0; i < 2; i++)
                {
                    pool.Add(new BlessingDefinition($"{tier}-sword-{i}", $"{tier} 검 {i}", WeaponTag.Sword, tier));
                    pool.Add(new BlessingDefinition($"{tier}-bow-{i}", $"{tier} 활 {i}", WeaponTag.Bow, tier));
                }
            }

            return pool;
        }

        private static MatchEconomyState NewMatch(params int[] playerIds)
        {
            var state = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, BuildPool());
            var participants = playerIds
                .Select(id => new MatchParticipant(id, WeaponTag.Sword))
                .ToArray();
            state.BeginMatch(participants, Seed);
            return state;
        }

        /// <summary>준비를 열고 아무도 안 고른 채 전투로 넘긴 뒤 결과까지 반영한다.</summary>
        private static void PlayRound(MatchEconomyState state, params RoundOutcome[] outcomes)
        {
            var round = state.BeginPreparation();
            state.EndPreparation();
            state.TryApplyRoundResults(round, outcomes);
        }

        // ── 완료 조건 1 — 1·2·3라운드의 초기화와 이월이 정해진 순서로 동작 ──────

        [Test]
        public void 완료조건1_첫_라운드는_기본_골드만_지급된다()
        {
            var state = NewMatch(Alice);

            state.BeginPreparation();

            Assert.That(state.GetGold(Alice), Is.EqualTo(EconomyConfig.DefaultRoundGrantGold));
        }

        [Test]
        public void 완료조건1_다음_라운드는_이월_정산_뒤에_지급된다()
        {
            // 순서가 뒤바뀌면 이번 라운드에 받은 골드까지 한도에 걸려 소멸한다.
            var state = NewMatch(Alice);
            state.BeginPreparation();      // 1,000
            state.EndPreparation();

            state.BeginPreparation();      // 1,000 이월 + 1,000 지급

            Assert.That(state.GetGold(Alice), Is.EqualTo(EconomyConfig.DefaultRoundGrantGold * 2));
        }

        [Test]
        public void 완료조건1_이월_한도를_넘은_골드는_소멸하고_스냅샷에_남는다()
        {
            var config = EconomyConfig.Default.WithCarryOverLimitSetting(1_500);
            var state = new MatchEconomyState(config, BlessingConfig.Default, BuildPool());
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);

            state.BeginPreparation();      // 1,000
            state.Earn(Alice, 5_000);      // 6,000
            state.EndPreparation();

            state.BeginPreparation();      // 1,500 까지만 이월 → 4,500 소멸, +1,000 지급

            var snapshot = state.BuildSnapshot(Alice);
            Assert.That(snapshot.CarriedGold, Is.EqualTo(1_500));
            Assert.That(snapshot.ForfeitedGold, Is.EqualTo(4_500));
            Assert.That(snapshot.Gold, Is.EqualTo(2_500));
        }

        [Test]
        public void 완료조건1_정해진_라운드_수를_넘겨_열_수_없다()
        {
            var state = NewMatch(Alice);
            for (var i = 0; i < state.TotalRounds; i++)
            {
                state.BeginPreparation();
                state.EndPreparation();
            }

            Assert.Throws<InvalidOperationException>(() => state.BeginPreparation());
        }

        // ── 완료 조건 2 — 후보 3개·잠금·재추첨·선택을 라운드마다 수행 가능 ───────

        [Test]
        public void 완료조건2_라운드마다_후보가_3개씩_제시된다()
        {
            var state = NewMatch(Alice);

            for (var round = 1; round <= state.TotalRounds; round++)
            {
                state.BeginPreparation();

                Assert.That(
                    state.GetCandidates(Alice).Count,
                    Is.EqualTo(BlessingConfig.DefaultCandidateCount),
                    $"라운드 {round}");

                state.EndPreparation();
            }
        }

        [Test]
        public void 완료조건2_잠금은_최대_2개까지만_켜진다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();

            Assert.That(state.TryToggleLock(Alice, 0), Is.True);
            Assert.That(state.TryToggleLock(Alice, 1), Is.True);
            Assert.That(state.TryToggleLock(Alice, 2), Is.False, "3개째는 상한에 걸린다");
            Assert.That(state.BuildSnapshot(Alice).RemainingLockCount, Is.Zero);
        }

        [Test]
        public void 완료조건2_재추첨은_골드를_쓰고_횟수가_늘어난다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();
            var before = state.GetGold(Alice);

            Assert.That(state.TryReroll(Alice), Is.EqualTo(RerollResult.Success));

            Assert.That(state.GetGold(Alice), Is.LessThan(before));
            Assert.That(state.BuildSnapshot(Alice).RerollCount, Is.EqualTo(1));
        }

        // ── 완료 조건 3 — 잔액 부족·이미 선택·잘못된 후보에 명확한 거부 사유 ────

        [Test]
        public void 완료조건3_골드가_모자라면_사유가_NotEnoughGold_다()
        {
            var config = EconomyConfig.Default.WithRoundGrantGold(0);
            var state = new MatchEconomyState(config, BlessingConfig.Default, BuildPool());
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);
            state.BeginPreparation();

            Assert.That(state.GetRerollAvailability(Alice), Is.EqualTo(RerollResult.NotEnoughGold));
            Assert.That(state.BuildSnapshot(Alice).CanReroll, Is.False);
        }

        [Test]
        public void 완료조건3_이미_선택했으면_사유가_AlreadySelected_다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();
            state.TrySelect(Alice, 0);

            Assert.That(state.GetRerollAvailability(Alice), Is.EqualTo(RerollResult.AlreadySelected));
        }

        [Test]
        public void 완료조건3_잘못된_후보_번호는_예외가_아니라_거부다()
        {
            // 화면과 도메인의 목록이 어긋났을 때 예외를 던지면 EventBus 가 로그로 삼킨다.
            var state = NewMatch(Alice);
            state.BeginPreparation();

            Assert.That(state.TryToggleLock(Alice, 99), Is.False);
            Assert.That(state.TryToggleLock(Alice, -1), Is.False);
            Assert.That(state.TrySelect(Alice, 99), Is.Null);
        }

        [Test]
        public void 완료조건3_두_번째_선택은_거부된다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();

            Assert.That(state.TrySelect(Alice, 0), Is.Not.Null);
            Assert.That(state.TrySelect(Alice, 1), Is.Null, "버튼 연타로 두 개를 얻을 수 없다");
            Assert.That(state.GetAcquired(Alice).Count, Is.EqualTo(1));
        }

        // ── 완료 조건 4 — 무기 비호환 전용 증강이 나오지 않음 ──────────────────

        [Test]
        public void 완료조건4_활을_들면_검_전용_축복이_후보에_없다()
        {
            var state = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, BuildPool());
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Bow) }, Seed);

            for (var round = 1; round <= state.TotalRounds; round++)
            {
                state.BeginPreparation();

                foreach (var candidate in state.GetCandidates(Alice))
                {
                    Assert.That(
                        candidate.Blessing.Tag,
                        Is.Not.EqualTo(WeaponTag.Sword),
                        $"라운드 {round} 에 검 전용이 떴다: {candidate.Blessing.Id}");
                }

                state.EndPreparation();
            }
        }

        // ── 완료 조건 5 — 최대 3개 누적, 새 매치에서 0개 초기화 ────────────────

        [Test]
        public void 완료조건5_매치_동안_라운드마다_하나씩_누적된다()
        {
            var state = NewMatch(Alice);

            for (var round = 1; round <= state.TotalRounds; round++)
            {
                state.BeginPreparation();
                state.TrySelect(Alice, 0);
                state.EndPreparation();

                Assert.That(state.GetAcquired(Alice).Count, Is.EqualTo(round));
            }

            Assert.That(state.GetAcquired(Alice).Count, Is.EqualTo(state.TotalRounds));
        }

        [Test]
        public void 완료조건5_새_매치를_시작하면_골드와_축복이_0_이_된다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();
            state.TrySelect(Alice, 0);
            state.EndPreparation();
            state.TryApplyRoundResults(1, new[] { new RoundOutcome(Alice, 3, 1, true) });

            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);

            Assert.That(state.GetGold(Alice), Is.Zero);
            Assert.That(state.GetAcquired(Alice).Count, Is.Zero);
            Assert.That(state.Tally.GetEntry(Alice).Kills, Is.Zero, "누적 전투 기록도 비워진다");
            Assert.That(state.Round, Is.Zero);
        }

        [Test]
        public void 완료조건5_같은_축복이_두_번_나오지_않는다()
        {
            var state = NewMatch(Alice);
            var acquiredIds = new List<string>();

            for (var round = 1; round <= state.TotalRounds; round++)
            {
                state.BeginPreparation();

                foreach (var candidate in state.GetCandidates(Alice))
                {
                    Assert.That(
                        acquiredIds, Does.Not.Contain(candidate.Blessing.Id),
                        "이미 획득한 축복이 다시 후보로 떴다 (S-GD-VF67AG)");
                }

                acquiredIds.Add(state.TrySelect(Alice, 0).Id);
                state.EndPreparation();
            }
        }

        // ── 완료 조건 6 — 라운드 결과 중복 합산 금지 + 최종 순서 입력 ───────────

        [Test]
        public void 완료조건6_같은_라운드_결과가_두_번_와도_한_번만_합산된다()
        {
            // Host 재전송·재접속에서 실제로 두 번 온다. 그냥 더하면 킬 수가 조용히 부푼다.
            var state = NewMatch(Alice, Bob);
            var round = state.BeginPreparation();
            state.EndPreparation();

            var results = new[]
            {
                new RoundOutcome(Alice, 3, 1, true),
                new RoundOutcome(Bob, 1, 2, false),
            };

            Assert.That(state.TryApplyRoundResults(round, results), Is.True);
            Assert.That(state.TryApplyRoundResults(round, results), Is.False, "두 번째는 무시된다");

            var alice = state.Tally.GetEntry(Alice);
            Assert.That(alice.Kills, Is.EqualTo(3));
            Assert.That(alice.Assists, Is.EqualTo(1));
            Assert.That(alice.CountedRounds, Is.EqualTo(1));
        }

        [Test]
        public void 완료조건6_3라운드_결과가_전부_누적된다()
        {
            var state = NewMatch(Alice, Bob);

            for (var round = 1; round <= state.TotalRounds; round++)
            {
                PlayRound(
                    state,
                    new RoundOutcome(Alice, 2, 0, true),
                    new RoundOutcome(Bob, 1, 1, false));
            }

            var alice = state.Tally.GetEntry(Alice);
            Assert.That(alice.Kills, Is.EqualTo(2 * state.TotalRounds));
            Assert.That(alice.SurvivedRounds, Is.EqualTo(state.TotalRounds));
            Assert.That(alice.CountedRounds, Is.EqualTo(state.TotalRounds));

            var bob = state.Tally.GetEntry(Bob);
            Assert.That(bob.Assists, Is.EqualTo(state.TotalRounds));
            Assert.That(bob.SurvivedRounds, Is.Zero);
        }

        [Test]
        public void 완료조건6_최종_순서_입력이_전_참가자분_나온다()
        {
            var state = NewMatch(Alice, Bob);
            PlayRound(state, new RoundOutcome(Alice, 5, 0, true), new RoundOutcome(Bob, 0, 3, false));

            var inputs = state.Tally.BuildStandingInputs();

            Assert.That(inputs.Length, Is.EqualTo(2));
            Assert.That(inputs.Select(e => e.PlayerId), Is.EquivalentTo(new[] { Alice, Bob }));
        }

        [Test]
        public void 완료조건6_마지막_라운드_결과가_들어오면_매치가_끝난_상태가_된다()
        {
            var state = NewMatch(Alice);

            for (var round = 1; round <= state.TotalRounds; round++)
            {
                PlayRound(state, new RoundOutcome(Alice, 1, 0, true));
            }

            Assert.That(state.Phase, Is.EqualTo(MatchEconomyPhase.Finished));
        }

        // ── 국면 경계 ─────────────────────────────────────────────────────

        [Test]
        public void 강화는_플레이어별_지갑과_주무기_상태에만_적용된다()
        {
            var state = NewMatch(Alice, Bob);
            state.BeginPreparation();

            var result = state.TryUpgradeWeapon(Alice);

            Assert.That(result, Is.EqualTo(WeaponUpgradeAttemptResult.Success));
            Assert.That(state.GetGold(Alice), Is.EqualTo(900));
            Assert.That(state.GetGold(Bob), Is.EqualTo(1_000));
            Assert.That(state.BuildWeaponUpgradeState(Alice, false).Level, Is.EqualTo(1));
            Assert.That(state.BuildWeaponUpgradeState(Bob, false).Level, Is.EqualTo(0));
        }

        [Test]
        public void 다음_준비_라운드가_열리면_직전_전투용_강화가_초기화된다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();
            Assert.That(state.TryUpgradeWeapon(Alice), Is.EqualTo(WeaponUpgradeAttemptResult.Success));
            state.EndPreparation();
            Assert.That(
                state.TryApplyRoundResults(1, new[] { new RoundOutcome(Alice, 0, 0, true) }),
                Is.True);

            state.BeginPreparation();

            var upgrade = state.BuildWeaponUpgradeState(Alice, false);
            Assert.That(upgrade.Level, Is.EqualTo(0));
            Assert.That(upgrade.AttackPowerBonusPercent, Is.EqualTo(0));
        }

        [Test]
        public void 전투_중에는_준비_단계_조작이_거부된다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();
            state.EndPreparation();

            Assert.Throws<InvalidOperationException>(() => state.TryReroll(Alice));
            Assert.Throws<InvalidOperationException>(() => state.TryToggleLock(Alice, 0));
            Assert.Throws<InvalidOperationException>(() => state.TrySelect(Alice, 0));
            Assert.Throws<InvalidOperationException>(() => state.TryUpgradeWeapon(Alice));
        }

        [Test]
        public void 준비_시간이_끝나면_안_고른_사람은_자동_선택된다()
        {
            var state = NewMatch(Alice, Bob);
            state.BeginPreparation();
            state.TrySelect(Alice, 0);

            var autoSelected = state.EndPreparation();

            Assert.That(autoSelected, Is.EquivalentTo(new[] { Bob }));
            Assert.That(state.GetAcquired(Bob).Count, Is.EqualTo(1), "축복 없이 전투에 들어가지 않는다");
        }

        [Test]
        public void 참가자가_아니면_조회가_거부된다()
        {
            var state = NewMatch(Alice);
            Assert.Throws<ArgumentOutOfRangeException>(() => state.GetGold(999));
        }

        [Test]
        public void 플레이어_id_가_중복되면_매치를_시작할_수_없다()
        {
            var state = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, BuildPool());

            Assert.Throws<ArgumentException>(() => state.BeginMatch(
                new[]
                {
                    new MatchParticipant(Alice, WeaponTag.Sword),
                    new MatchParticipant(Alice, WeaponTag.Bow),
                },
                Seed));
        }

        // ── 결정성 (2PC 요구사항) ─────────────────────────────────────────

        [Test]
        public void 같은_시드는_같은_후보를_만든다()
        {
            static string[] Run()
            {
                var state = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, BuildPool());
                state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);
                state.BeginPreparation();
                return state.GetCandidates(Alice).Select(c => c.Blessing.Id).ToArray();
            }

            Assert.That(Run(), Is.EqualTo(Run()));
        }

        [Test]
        public void 같은_시드라도_플레이어끼리는_후보가_갈린다()
        {
            // 전원이 같은 후보를 받으면 선택에 의미가 없다.
            var state = NewMatch(Alice, Bob);
            state.BeginPreparation();

            var aliceIds = state.GetCandidates(Alice).Select(c => c.Blessing.Id).ToArray();
            var bobIds = state.GetCandidates(Bob).Select(c => c.Blessing.Id).ToArray();

            Assert.That(aliceIds, Is.Not.EqualTo(bobIds));
        }

        // ── S15P21A606-98 — 강화 효과의 라운드 수명 (S-GD-988BNV) ──────────────
        //
        // ⚠️ 이 셋은 2PC 로 재려던 시나리오 S5·S6-a 를 대신한다. 온라인 라운드 정산이
        //    샌드박스 전용 LocalRoundDirector 에 묶여 전투→다음 라운드 전이가 막혀 있어
        //    실측이 불가능했다(docs/client/weapon-upgrade-2pc-test.md §6-5).
        //    그 결함이 풀리면 2PC 로도 같은 것을 확인한다 — 이 테스트가 그것을 대신하지 않는다.

        [Test]
        public void 완료조건_전투_라운드_결과를_반영하면_전원의_강화가_0강으로_초기화된다()
        {
            var state = NewMatch(Alice, Bob);
            var round = state.BeginPreparation();

            // 1강은 성공률 100% 다 (S-GD-3PGNS5). 확률에 기대지 않고 단계를 올린다.
            Assert.That(state.TryUpgradeWeapon(Alice), Is.EqualTo(WeaponUpgradeAttemptResult.Success));
            Assert.That(state.TryUpgradeWeapon(Bob), Is.EqualTo(WeaponUpgradeAttemptResult.Success));

            // S6-a 의 도메인 부분 — 전투로 넘어가도 그 라운드 동안은 유지된다.
            state.EndPreparation();
            Assert.That(LevelOf(state, Alice), Is.EqualTo(1));
            Assert.That(BonusOf(state, Alice), Is.EqualTo(10));

            Assert.That(state.TryApplyRoundResults(round, Array.Empty<RoundOutcome>()), Is.True);

            // 라운드가 끝나면 그 라운드용 효과를 제거한다 — 한 명이 아니라 전원.
            Assert.That(LevelOf(state, Alice), Is.EqualTo(0));
            Assert.That(BonusOf(state, Alice), Is.EqualTo(0));
            Assert.That(LevelOf(state, Bob), Is.EqualTo(0));
            Assert.That(BonusOf(state, Bob), Is.EqualTo(0));
        }

        [Test]
        public void 완료조건_다음_준비_라운드는_0강에서_시작한다()
        {
            var state = NewMatch(Alice, Bob);
            var round = state.BeginPreparation();
            state.TryUpgradeWeapon(Alice);
            state.EndPreparation();
            state.TryApplyRoundResults(round, Array.Empty<RoundOutcome>());

            state.BeginPreparation();

            // 매치 영구도 계정 영구도 아니다 (S-GD-988BNV). 이월되면 다음 라운드가 세진다.
            Assert.That(LevelOf(state, Alice), Is.EqualTo(0));
            Assert.That(BonusOf(state, Alice), Is.EqualTo(0));
        }

        [Test]
        public void 완료조건_초기화_뒤에도_같은_라운드에서_다시_강화할_수_있다()
        {
            // 초기화가 상태를 망가뜨리지 않는지 본다 — 0강으로 되돌린 뒤 1강이 다시 붙어야 한다.
            var state = NewMatch(Alice, Bob);
            var round = state.BeginPreparation();
            state.TryUpgradeWeapon(Alice);
            state.EndPreparation();
            state.TryApplyRoundResults(round, Array.Empty<RoundOutcome>());

            state.BeginPreparation();

            Assert.That(state.TryUpgradeWeapon(Alice), Is.EqualTo(WeaponUpgradeAttemptResult.Success));
            Assert.That(LevelOf(state, Alice), Is.EqualTo(1));
            Assert.That(BonusOf(state, Alice), Is.EqualTo(10));
        }

        private static int LevelOf(MatchEconomyState state, int playerId) =>
            state.BuildWeaponUpgradeState(playerId, isMine: false).Level;

        private static int BonusOf(MatchEconomyState state, int playerId) =>
            state.BuildWeaponUpgradeState(playerId, isMine: false).AttackPowerBonusPercent;
    }
}
