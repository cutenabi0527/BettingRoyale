using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 경쟁과 이월 한도의 <b>경계</b> (Jira S15P21A606-33).
    ///
    /// <para>
    /// -32 는 정상 경로만 봤다 — <c>ChestEconomyIntegrationTests</c> 가 *"이월 한도 0·50,000 같은
    /// 경계값과 동시성은 -33 의 범위다"* 로 넘겼고, <see cref="TreasureChest"/> 와
    /// <c>ChestEvents</c> 도 *"경쟁 처리 규칙은 -33 에서 다룬다"* 고 적어뒀다. 여기서 그 경계를 잠근다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ 이 파일은 <b>현재 동작을 고정</b>한다. 경쟁 규칙이 바뀌면(예: 동시 개봉 허용) 여기가 먼저
    /// 빨개져야 한다 — 조용히 바뀌면 <c>S-GD-6DNSGY</c> 가 예고한 *"권장안이 뒤집히면 상태 기계를
    /// 고쳐야 한다"* 를 아무도 눈치채지 못한다.
    /// </para>
    ///
    /// <para>
    /// <b>-33 의 나머지 절반은 <see cref="ChestTickOrderAndResendTests"/> 에 있다.</b>
    /// 여기가 <b>한 시점</b>의 경쟁·한도를 본다면(두 사람이 같은 상자를 열 때, 한도가 0일 때),
    /// 그쪽은 <b>두 사건이 같은 프레임에 겹칠 때</b>를 본다 — 완료 틱과 취소, 개봉 요청의 재전송,
    /// 보상 지급과 라운드 정산. 둘을 합쳐야 -33 의 완료 조건이 덮인다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ChestCompetitionAndCarryOverTests
    {
        private const int Alice = 1;
        private const int Bob = 2;
        private const int OpenTicks = 5;
        private const int RespawnTicks = 3;
        /// <summary>재시도 지연. ⚠️ 검증용 임의값이다 — 확정값 1.0초는 데이터로 들어온다 (S-GD-5AZ6HN).</summary>
        private const int RetryTicks = 3;
        private const int Reward = 500;

        private static IReadOnlyList<WorldPosition> Anchors() => new[]
        {
            new WorldPosition(-7f, 0f, 8f), new WorldPosition(7f, 0f, 8f),
            new WorldPosition(-7f, 0f, -8f), new WorldPosition(7f, 0f, -8f),
        };

        private static ChestField OpenedField(int chestCount = 2, int seed = 909)
        {
            var field = new ChestField(new ChestFieldConfig(
                new ChestConfig(OpenTicks, RespawnTicks, RetryTicks), Anchors(), chestCount, new ChestReward(Reward)));
            field.BeginRound(new DeterministicRandom(seed));
            return field;
        }

        private static IReadOnlyList<BlessingDefinition> Pool()
        {
            var pool = new List<BlessingDefinition>();
            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (var i = 0; i < 6; i++)
                {
                    pool.Add(new BlessingDefinition($"{tier}-{i}", $"{tier} {i}", WeaponTag.Common, tier));
                }
            }

            return pool;
        }

        // ── 상자 경쟁 (S-GD-6DNSGY "동일 상자 동시 상호작용 불가") ──────────

        /// <summary>
        /// 한 사람은 두 상자를 동시에 열 수 없다 (S-GD-PTVC2X, 조영운 2026-09-04 확정).
        ///
        /// <para>
        /// <b>2026-09-04 이전에는 열렸다.</b> <see cref="TreasureChest"/> 가 <c>OpenerId</c> 를
        /// 하나만 갖는 것은 "한 상자에 한 사람" 을 보장할 뿐 "한 사람에 한 상자" 는 아무도
        /// 막지 않았다. 이 테스트는 그 사실을 고정하고 있었고, 규칙이 정해지면서 뒤집혔다 —
        /// <b>설계된 대로 여기가 먼저 빨개졌다.</b>
        /// </para>
        ///
        /// <para>
        /// 덕분에 <c>ChestRequestGate.HandleOpenCancel</c> 이 <c>cancelled[0]</c> 만 싣는 전제가
        /// <b>비로소 참이 된다.</b> 사람 단위 취소는 이제 최대 1건이다.
        /// (⚠️ <c>EndRound</c> 는 여전히 여러 건이다 — 사람이 여럿이기 때문이다.)
        /// </para>
        /// </summary>
        [Test]
        public void 한_사람은_두_상자를_동시에_열_수_없다()
        {
            var field = OpenedField();

            Assert.That(field.TryBeginOpen(0, Alice), Is.True);
            Assert.That(field.TryBeginOpen(1, Alice), Is.False,
                "이미 0번을 열고 있으므로 1번은 거부된다");

            var placements = field.BuildPlacements();
            Assert.That(placements[0].State, Is.EqualTo(ChestState.Opening));
            Assert.That(placements[1].State, Is.EqualTo(ChestState.Idle),
                "거부된 요청은 대상 상자를 건드리지 않는다");
            Assert.That(placements[1].OpenerId, Is.EqualTo(TreasureChest.NoOpener));

            var cancelled = field.CancelOpenBy(Alice, ChestCancelReason.Moved);
            Assert.That(cancelled.Count, Is.EqualTo(1),
                "한 사람이 한 상자만 열 수 있으므로 취소도 하나다 — 게이트 주석의 전제가 참이 됐다");
        }

        /// <summary>
        /// 가드는 <b>같은 사람</b>만 막는다. 남이 여는 것은 그대로 된다.
        /// </summary>
        [Test]
        public void 내가_열고_있어도_남은_다른_상자를_열_수_있다()
        {
            var field = OpenedField();

            Assert.That(field.TryBeginOpen(0, Alice), Is.True);
            Assert.That(field.TryBeginOpen(1, Bob), Is.True,
                "Bob 은 아무것도 안 열고 있다 — 가드는 사람 단위다");
        }

        [Test]
        public void 취소_직후에_다른_사람이_곧바로_열_수_있다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();
            field.CancelOpenBy(Alice, ChestCancelReason.Damaged);

            Assert.That(field.TryBeginOpen(0, Bob), Is.True,
                "재시도 지연은 취소당한 본인에게만 걸린다 — 남은 즉시 열 수 있다 (S-GD-5AZ6HN)");
        }

        // ── 재시도 지연 (S-GD-5AZ6HN) ────────────────────────────────────────

        /// <summary>
        /// 취소당한 본인은 <b>그 상자를</b> 잠시 다시 열 수 없다 (S-GD-5AZ6HN).
        /// 정확히 <c>RetryTicks</c> 만큼이다 — 그 전에는 거부, 그 뒤에는 허용.
        /// </summary>
        [Test]
        public void 취소당한_본인은_그_상자를_잠시_다시_열_수_없다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();
            field.CancelOpenBy(Alice, ChestCancelReason.Damaged);

            Assert.That(field.TryBeginOpen(0, Alice), Is.False, "취소 직후에는 거부된다");

            for (var i = 0; i < RetryTicks - 1; i++)
            {
                field.Tick();
                Assert.That(field.TryBeginOpen(0, Alice), Is.False, $"{i + 1}틱 뒤에도 아직이다");
            }

            field.Tick();
            Assert.That(field.TryBeginOpen(0, Alice), Is.True, $"{RetryTicks}틱이 지나면 다시 열린다");
        }

        /// <summary>
        /// 지연은 <b>그 상자에만</b> 걸린다 — 본인도 다른 상자는 바로 열 수 있다.
        /// 사용자 확정 근거: *"어차피 1초 이내의 거리에는 안 나올 가능성이 높아."*
        /// </summary>
        [Test]
        public void 취소당해도_다른_상자는_곧바로_열_수_있다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();
            field.CancelOpenBy(Alice, ChestCancelReason.Moved);

            Assert.That(field.TryBeginOpen(0, Alice), Is.False, "0번은 쿨다운 중이다");
            Assert.That(field.TryBeginOpen(1, Alice), Is.True, "1번은 지연 대상이 아니다");
        }

        /// <summary>
        /// ⚠️ <b>라운드 종료로 끊긴 것은 지연을 만들지 않는다.</b> 개봉자의 잘못이 아니라서
        /// 사유를 따로 만들었는데(S-GD-GPXVNR), 지연까지 걸면 <b>다음 라운드 시작 직후 그 사람만</b>
        /// 상자를 못 여는 벌을 받는다.
        /// </summary>
        [Test]
        public void 라운드_종료로_끊긴_것은_다음_라운드에_지연을_남기지_않는다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();

            field.EndRound();
            field.BeginRound(new DeterministicRandom(909));

            Assert.That(field.TryBeginOpen(0, Alice), Is.True,
                "라운드 종료는 개봉자 잘못이 아니므로 벌을 남기지 않는다");
        }

        // ── 라운드 막바지 개봉 금지 (S-GD-DFG3VR) ───────────────────────────

        /// <summary>
        /// 자기장이 맵 전역을 덮는 순간부터 <b>새 개봉을 받지 않는다.</b>
        /// 도메인은 그 시점을 모르므로 어댑터가 알려준다.
        /// </summary>
        [Test]
        public void 새_개봉을_그만_받으면_아무도_열_수_없다()
        {
            var field = OpenedField();
            Assert.That(field.IsAcceptingNewOpens, Is.True, "라운드 시작에는 받는다");

            field.StopAcceptingNewOpens();

            Assert.That(field.IsAcceptingNewOpens, Is.False);
            Assert.That(field.TryBeginOpen(0, Alice), Is.False);
            Assert.That(field.TryBeginOpen(1, Bob), Is.False, "사람을 가리지 않는다");
        }

        /// <summary>
        /// <b>막는 것은 "시작" 뿐이다.</b> 이미 열고 있던 개봉은 계속되고 완료되면 보상을 받는다.
        /// </summary>
        [Test]
        public void 진행_중이던_개봉은_금지_뒤에도_끝나서_보상을_준다()
        {
            var field = OpenedField();
            Assert.That(field.TryBeginOpen(0, Alice), Is.True);
            field.Tick();

            field.StopAcceptingNewOpens();

            var earned = new List<ChestRewardEarned>();
            for (var i = 0; i < OpenTicks; i++)
            {
                earned.AddRange(field.Tick());
            }

            Assert.That(earned.Count, Is.EqualTo(1), "이미 시작한 개봉은 끝까지 간다");
            Assert.That(earned[0].ClaimerId, Is.EqualTo(Alice));
        }

        [Test]
        public void 다음_라운드는_다시_개봉을_받는다()
        {
            var field = OpenedField();
            field.StopAcceptingNewOpens();

            field.EndRound();
            field.BeginRound(new DeterministicRandom(909));

            Assert.That(field.IsAcceptingNewOpens, Is.True);
            Assert.That(field.TryBeginOpen(0, Alice), Is.True);
        }

        /// <summary>이동으로 끊긴 쿨다운도 라운드가 바뀌면 사라진다 — 슬롯이 통째로 새로 생긴다.</summary>
        [Test]
        public void 쿨다운은_라운드_경계를_넘지_않는다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();
            field.CancelOpenBy(Alice, ChestCancelReason.Moved);
            Assert.That(field.TryBeginOpen(0, Alice), Is.False, "전제: 쿨다운이 걸려 있어야 한다");

            field.EndRound();
            field.BeginRound(new DeterministicRandom(909));

            Assert.That(field.TryBeginOpen(0, Alice), Is.True, "새 라운드는 깨끗하게 시작한다");
        }

        /// <summary>취소는 진행도를 버린다 — 다음 사람이 이어받지 않는다 (S-GD-GQNA2B).</summary>
        [Test]
        public void 취소하면_진행도가_0으로_돌아간다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            for (var i = 0; i < OpenTicks - 1; i++)
            {
                field.Tick();
            }

            Assert.That(field.BuildPlacements()[0].Progress, Is.GreaterThan(0f));

            field.CancelOpenBy(Alice, ChestCancelReason.Moved);
            Assert.That(field.BuildPlacements()[0].Progress, Is.Zero);

            // 이어받았다면 한 틱에 완료됐을 것이다.
            field.TryBeginOpen(0, Bob);
            Assert.That(field.Tick(), Is.Empty, "다음 사람이 진행도를 물려받으면 안 된다");
        }

        [Test]
        public void 보상을_받고_재생성되는_동안에는_아무도_못_연다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            for (var i = 0; i < OpenTicks; i++)
            {
                field.Tick();
            }

            Assert.That(field.BuildPlacements()[0].State, Is.EqualTo(ChestState.Respawning));
            Assert.That(field.TryBeginOpen(0, Bob), Is.False, "재생성 중인 상자는 열 수 없다");

            for (var i = 0; i < RespawnTicks; i++)
            {
                field.Tick();
            }

            Assert.That(field.TryBeginOpen(0, Bob), Is.True, "재생성이 끝나면 다시 열린다");
        }

        [Test]
        public void 서로_다른_상자는_두_명이_동시에_열_수_있다()
        {
            var field = OpenedField();

            Assert.That(field.TryBeginOpen(0, Alice), Is.True);
            Assert.That(field.TryBeginOpen(1, Bob), Is.True);

            var earned = new List<ChestRewardEarned>();
            for (var i = 0; i < OpenTicks + 1; i++)
            {
                earned.AddRange(field.Tick());
            }

            Assert.That(earned.Count, Is.EqualTo(2), "둘 다 보상을 받는다");
        }

        /// <summary>먼저 시작한 쪽이 이긴다 — 늦게 온 쪽은 진행도를 못 뺏는다.</summary>
        [Test]
        public void 늦게_온_쪽은_진행도를_뺏지_못한다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();

            Assert.That(field.TryBeginOpen(0, Bob), Is.False);
            Assert.That(field.BuildPlacements()[0].OpenerId, Is.EqualTo(Alice));

            // Bob 이 이동해도 Alice 의 개봉은 끊기지 않는다.
            Assert.That(field.CancelOpenBy(Bob, ChestCancelReason.Moved), Is.Empty);
            Assert.That(field.BuildPlacements()[0].State, Is.EqualTo(ChestState.Opening));
        }

        // ── 이월 한도 경계 (S-GD-P5RFZM) ─────────────────────────────────────

        [Test]
        public void 한도가_0이면_남은_골드가_전부_소멸한다()
        {
            var limit = CarryOverLimit.FromSetting(CarryOverLimit.SettingMinGold);
            var result = limit.Apply(9_999);

            Assert.That(result.CarriedGold, Is.Zero);
            Assert.That(result.ForfeitedGold, Is.EqualTo(9_999));
        }

        [Test]
        public void 설정_최대값_50000은_무한이_아니라_유한_한도다()
        {
            var limit = CarryOverLimit.FromSetting(CarryOverLimit.SettingMaxGold);

            Assert.That(limit.IsUnlimited, Is.False, "50,000 은 경계 안쪽이다");
            Assert.That(limit.Gold, Is.EqualTo(50_000));

            var result = limit.Apply(50_001);
            Assert.That(result.CarriedGold, Is.EqualTo(50_000));
            Assert.That(result.ForfeitedGold, Is.EqualTo(1));
        }

        [Test]
        public void 음수_설정값은_0으로_잘린다()
        {
            var limit = CarryOverLimit.FromSetting(-1);

            Assert.That(limit.IsUnlimited, Is.False);
            Assert.That(limit.Gold, Is.Zero);
            Assert.That(limit.Apply(500).ForfeitedGold, Is.EqualTo(500));
        }

        [TestCase(999, 1_000, 999, 0)]
        [TestCase(1_000, 1_000, 1_000, 0)]
        [TestCase(1_001, 1_000, 1_000, 1)]
        public void 한도_전후_1골드에서_정확히_갈린다(
            int leftover, int limitGold, int expectedCarried, int expectedForfeited)
        {
            var result = CarryOverLimit.FromSetting(limitGold).Apply(leftover);

            Assert.That(result.CarriedGold, Is.EqualTo(expectedCarried));
            Assert.That(result.ForfeitedGold, Is.EqualTo(expectedForfeited));
        }

        // ── 상자 보상이 낀 경계 ──────────────────────────────────────────────

        /// <summary>
        /// 한도가 0이면 상자로 번 골드도 라운드를 못 넘는다.
        /// 상자 골드라고 특별 대우하지 않는다는 것을 <b>극단값에서</b> 확인한다.
        /// </summary>
        [Test]
        public void 한도가_0이면_상자_보상도_다음_라운드로_못_넘어간다()
        {
            var economy = new MatchEconomyState(
                EconomyConfig.Default.WithCarryOverLimitSetting(0), BlessingConfig.Default, Pool());
            economy.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, seed: 55);

            economy.BeginPreparation();
            var roundGrant = economy.GetGold(Alice);
            economy.EndPreparation();

            economy.Earn(Alice, Reward);
            economy.TryApplyRoundResults(1, new[] { new RoundOutcome(Alice, 0, 0, true) });
            economy.BeginPreparation();

            Assert.That(economy.GetGold(Alice), Is.EqualTo(roundGrant),
                "이월이 0이므로 2라운드 잔액은 기본 지급뿐이어야 한다");

            Assert.That(economy.TryBuildRoundGoldSettled(Alice, isMine: true, out var settled), Is.True);
            Assert.That(settled.CarriedGold, Is.Zero);
            Assert.That(settled.ForfeitedGold, Is.EqualTo(roundGrant + Reward));
        }

        /// <summary>무제한이면 상자 보상이 라운드를 거듭해 누적된다.</summary>
        [Test]
        public void 무제한_이월이면_상자_보상이_라운드를_넘어_쌓인다()
        {
            var economy = new MatchEconomyState(
                EconomyConfig.Default.WithCarryOverLimitSetting(CarryOverLimit.SettingMaxGold + 1),
                BlessingConfig.Default, Pool());
            economy.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, seed: 56);

            economy.BeginPreparation();
            var roundGrant = economy.GetGold(Alice);
            economy.EndPreparation();

            economy.Earn(Alice, Reward);
            economy.TryApplyRoundResults(1, new[] { new RoundOutcome(Alice, 0, 0, true) });
            economy.BeginPreparation();

            Assert.That(economy.GetGold(Alice), Is.EqualTo(roundGrant + Reward + roundGrant));

            Assert.That(economy.TryBuildRoundGoldSettled(Alice, isMine: true, out var settled), Is.True);
            Assert.That(settled.ForfeitedGold, Is.Zero, "무제한에서는 소멸이 없다");
        }

        /// <summary>
        /// 이월로 소멸해도 <c>TotalInflow</c> 는 줄지 않는다 — 최종 순위 동점 5단계의 입력이라
        /// "얼마를 벌었나" 를 물어야 하기 때문이다 (S-GD-MNFBV7).
        /// </summary>
        [Test]
        public void 이월_소멸이_모은_골드_누계를_깎지_않는다()
        {
            var wallet = new Wallet();
            wallet.Earn(9_000);
            wallet.SettleRound(CarryOverLimit.FromSetting(0));

            Assert.That(wallet.Gold, Is.Zero);
            Assert.That(wallet.TotalInflow, Is.EqualTo(9_000));
        }
    }
}
