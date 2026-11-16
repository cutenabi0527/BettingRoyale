using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Roulette;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 룰렛 게이트 — 요청 판정과 정산 (Jira S15P21A606-84, S-GD-49WM7S · S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것 둘.</b>
    /// </para>
    ///
    /// <list type="number">
    /// <item><b>골드와 장부가 따로 놀지 않는다</b> — 걸었는데 돈이 안 빠지거나, 돈은 빠졌는데
    /// 안 걸린 상태가 없다.</item>
    /// <item><b>환불이 최종 순위를 오염시키지 않는다</b> — 돌려받은 돈은 "번 돈" 이 아니다.
    /// 이건 잔액만 보면 정상으로 보여서 <b>순위가 틀어질 때까지 아무도 모른다.</b></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class RouletteGateTests
    {
        private const int Alice = 1;
        private const int Bob = 2;
        private const int Stranger = 99;

        private const int Ticks = 10;
        private const int MinBet = 10;

        /// <summary>마감이 한참 남은 상태. 판이 정상으로 돈다.</summary>
        private const int PlentyOfTime = 10_000;

        /// <summary>준비맵 룰렛 위치. 배치는 S-GD-66GTWA 가 정한 실제 좌표다(반경 5m).</summary>
        private static readonly WorldPosition RouletteCenter = new WorldPosition(0f, 0f, 0f);

        /// <summary>어느 시설 원에도 안 걸리는 자리.</summary>
        private static readonly WorldPosition FarAway = new WorldPosition(200f, 0f, 200f);

        private MatchEconomyState economy;
        private EconomyRequestLedger ledger;
        private FakePresence presence;
        private RouletteTable table;
        private RouletteRequestGate gate;
        private DeterministicRandom random;
        private int sequence;

        private static RouletteConfig Config() =>
            new RouletteConfig(betTicks: Ticks, spinTicks: Ticks, resultTicks: 2, minBet: MinBet, openMarginTicks: 5);

        private static IReadOnlyList<BlessingDefinition> Pool()
        {
            var pool = new List<BlessingDefinition>();

            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (int i = 0; i < 6; i++)
                {
                    pool.Add(new BlessingDefinition($"{tier}-{i}", $"{tier} {i}", WeaponTag.Common, tier));
                }
            }

            return pool;
        }

        [SetUp]
        public void SetUp()
        {
            economy = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, Pool());
            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(Alice, WeaponTag.Sword),
                    new MatchParticipant(Bob, WeaponTag.Bow),
                },
                seed: 4242);

            ledger = new EconomyRequestLedger();
            economy.BeginPreparation();   // 라운드 기본 지급 1,000G
            ledger.OpenPreparation();

            // 기본값은 "둘 다 룰렛 앞에 서 있다" — 구역 밖 동작은 그 테스트에서 옮긴다.
            presence = new FakePresence();
            presence.Place(Alice, RouletteCenter);
            presence.Place(Bob, RouletteCenter);

            table = new RouletteTable(PlaceholderRouletteWheel.Create(), Config());
            gate = new RouletteRequestGate(
                economy,
                ledger,
                table,
                new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence));

            random = new DeterministicRandom(20260908);
            sequence = 0;
        }

        private EconomyRequestTicket NextTicket() => new EconomyRequestTicket(ledger.Epoch, ++sequence);

        private RouletteRequestOutcome Bet(int number, int amount, int playerId = Alice) =>
            gate.HandleBet(playerId, number, amount, NextTicket(), isMine: true);

        private RouletteWithdrawOutcome Withdraw(int playerId = Alice) =>
            gate.HandleWithdraw(playerId, NextTicket(), isMine: true);

        private RouletteTickOutcome Tick(int remaining = PlentyOfTime) =>
            gate.Tick(remaining, random, Alice, isMine: true);

        /// <summary>베팅이 열릴 때까지 돌린다.</summary>
        private void OpenBetting()
        {
            Tick();
            Assert.That(table.State, Is.EqualTo(RouletteState.Betting), "베팅이 열려야 나머지 검증이 뜻을 가진다");
        }

        /// <summary>정산이 나올 때까지 돌린다.</summary>
        private RouletteTickOutcome RunUntilSettled()
        {
            for (int i = 0; i < Ticks * 4; i++)
            {
                var outcome = Tick();

                if (outcome.Settlements.Length > 0)
                {
                    return outcome;
                }
            }

            Assert.Fail("정산이 나오지 않았다 — 건 돈이 증발한다");
            return default;
        }

        /// <summary>
        /// 같은 시드로 따로 돌려 <b>이번 판의 적중 숫자</b>를 미리 본다.
        /// 회전 전에는 난수를 쓰지 않으므로 본 게임과 같은 값이 나온다.
        /// </summary>
        private static int PeekWinningNumber(int seed)
        {
            var probe = new RouletteTable(PlaceholderRouletteWheel.Create(), Config());
            var probeRandom = new DeterministicRandom(seed);

            for (int i = 0; i < Ticks * 4; i++)
            {
                probe.Tick(PlentyOfTime, probeRandom);

                if (probe.WinningNumber > 0)
                {
                    return probe.WinningNumber;
                }
            }

            Assert.Fail("회전이 시작되지 않았다");
            return 0;
        }

        // ── 담기 (전송 한 칸) ────────────────────────────────────────────

        [Test]
        public void 숫자와_금액을_한_칸에_담았다_풀면_그대로_돌아온다()
        {
            foreach (var number in PlaceholderRouletteWheel.Numbers)
            {
                foreach (var amount in new[] { 1, 10, 999, 1_000_000, RouletteRequestPayload.MaxAmount })
                {
                    int packed = RouletteRequestPayload.PackBet(number, amount);

                    Assert.That(packed, Is.GreaterThan(0), $"숫자 {number} 금액 {amount} 가 음수로 담겼다");
                    Assert.That(
                        RouletteRequestPayload.TryUnpack(packed, out var kind, out int n, out int a),
                        Is.True);
                    Assert.That(kind, Is.EqualTo(RouletteRequestKind.Bet));
                    Assert.That(n, Is.EqualTo(number));
                    Assert.That(a, Is.EqualTo(amount));
                }
            }
        }

        [Test]
        public void 물리기는_0_이고_어떤_베팅과도_겹치지_않는다()
        {
            int withdraw = RouletteRequestPayload.PackWithdraw();

            Assert.That(RouletteRequestPayload.TryUnpack(withdraw, out var kind, out int n, out int a), Is.True);
            Assert.That(kind, Is.EqualTo(RouletteRequestKind.Withdraw));
            Assert.That(n, Is.Zero);
            Assert.That(a, Is.Zero);

            // 걸 수 있는 숫자는 1 이상이므로 물리기와 같은 값이 나올 수 없다.
            foreach (var number in PlaceholderRouletteWheel.Numbers)
            {
                Assert.That(RouletteRequestPayload.PackBet(number, MinBet), Is.Not.EqualTo(withdraw));
            }
        }

        [Test]
        public void 담을_수_없는_값은_조용히_잘리지_않고_던진다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RouletteRequestPayload.PackBet(0, 100), "0 은 물리기다");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RouletteRequestPayload.PackBet(RouletteRequestPayload.MaxNumber + 1, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => RouletteRequestPayload.PackBet(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RouletteRequestPayload.PackBet(1, RouletteRequestPayload.MaxAmount + 1));
        }

        [Test]
        public void 회선에서_온_이상한_값은_풀리지_않는다()
        {
            // 음수를 통과시키면 금액이 음수가 되어 **거는 순간 골드가 늘어난다.**
            Assert.That(RouletteRequestPayload.TryUnpack(-1, out _, out _, out _), Is.False);
            Assert.That(RouletteRequestPayload.TryUnpack(int.MinValue, out _, out _, out _), Is.False);

            // 숫자 자리가 0(물리기)인데 금액이 실려 있다 — 우리가 담은 값이 아니다.
            Assert.That(
                RouletteRequestPayload.TryUnpack(1 << RouletteRequestPayload.NumberBits, out _, out _, out _),
                Is.False);
        }

        [Test]
        public void 가장_큰_값을_담아도_int_를_넘지_않는다()
        {
            int packed = RouletteRequestPayload.PackBet(
                RouletteRequestPayload.MaxNumber, RouletteRequestPayload.MaxAmount);

            Assert.That(packed, Is.EqualTo(int.MaxValue));
        }

        // ── 베팅 ────────────────────────────────────────────────────────

        [Test]
        public void 걸면_골드가_빠지고_장부에_오른다()
        {
            OpenBetting();

            var r = Bet(number: 3, amount: 100);

            Assert.That(r.HasRejection, Is.False, r.HasRejection ? r.Rejection.Reason.ToString() : string.Empty);
            Assert.That(r.HasBetPlaced, Is.True);
            Assert.That(r.BetPlaced.Amount, Is.EqualTo(100));
            Assert.That(r.BetPlaced.TotalStaked, Is.EqualTo(100));

            Assert.That(economy.GetGold(Alice), Is.EqualTo(900), "거는 순간 빠진다 — 정산 때가 아니다");
            Assert.That(table.StakedOn(Alice, 3), Is.EqualTo(100));
            Assert.That(r.Gold.Gold, Is.EqualTo(900), "잔액은 확정값이 실린다");
            Assert.That(r.State.MyStakeTotal, Is.EqualTo(100));
        }

        [Test]
        public void 가진_것보다_크게_걸면_거절이_아니라_가진_만큼으로_깎인다()
        {
            OpenBetting();

            var r = Bet(number: 1, amount: 5_000);   // 잔액은 1,000

            Assert.That(r.HasRejection, Is.False, "\"전부 걸기\" 가 거절되면 더 이상하다");
            Assert.That(r.BetPlaced.Amount, Is.EqualTo(1_000), "확정 금액이 이벤트에 실려야 화면이 맞게 표시한다");
            Assert.That(economy.GetGold(Alice), Is.Zero);
            Assert.That(table.StakedOn(Alice, 1), Is.EqualTo(1_000));
        }

        [Test]
        public void 같은_숫자에_또_걸면_더해지고_여러_숫자에도_걸_수_있다()
        {
            OpenBetting();

            Bet(number: 1, amount: 100);
            Bet(number: 1, amount: 50);
            var r = Bet(number: 20, amount: 30);

            Assert.That(table.StakedOn(Alice, 1), Is.EqualTo(150));
            Assert.That(table.StakedOn(Alice, 20), Is.EqualTo(30));
            Assert.That(r.State.MyStakeTotal, Is.EqualTo(180));
            Assert.That(r.State.MyBets.Length, Is.EqualTo(2));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(820));
        }

        [Test]
        public void 최소_미만은_거절되고_골드는_손도_안_댄다()
        {
            OpenBetting();

            var r = Bet(number: 1, amount: MinBet - 1);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(RouletteBetRejectReason.BelowMinimum));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
            Assert.That(table.StakedBy(Alice), Is.Zero);
        }

        [Test]
        public void 깎아도_최소에_못_미치면_거절한다()
        {
            OpenBetting();
            Bet(number: 1, amount: 995);          // 잔액 5 남음

            var r = Bet(number: 3, amount: 500);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(RouletteBetRejectReason.NotEnoughGold));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(5), "거절에는 골드가 움직이지 않는다");
        }

        [Test]
        public void 바퀴에_없는_숫자는_거절한다()
        {
            OpenBetting();

            var r = Bet(number: 7, amount: 100);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(RouletteBetRejectReason.NoSuchNumber));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        [Test]
        public void 룰렛_구역_밖에서는_걸_수_없고_화면도_그렇게_보인다()
        {
            OpenBetting();
            presence.Place(Alice, FarAway);

            Assert.That(gate.BuildState(Alice, isMine: true).CanBet, Is.False,
                "버튼이 흐려지는 조건과 실제 판정이 같아야 한다");

            var r = Bet(number: 1, amount: 100);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(RouletteBetRejectReason.NotInRange));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        [Test]
        public void 회전_중에는_걸_수_없다()
        {
            OpenBetting();

            for (int i = 0; i < Ticks; i++)
            {
                Tick();
            }

            Assert.That(table.State, Is.EqualTo(RouletteState.Spinning));

            var r = Bet(number: 1, amount: 100);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(RouletteBetRejectReason.NotBetting));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        [Test]
        public void 준비_단계가_아니면_재전송과_다르게_거절을_알린다()
        {
            OpenBetting();
            economy.EndPreparation();

            var r = Bet(number: 1, amount: 100);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(RouletteBetRejectReason.NotPreparation));
            Assert.That(r.Verdict, Is.EqualTo(EconomyRequestVerdict.PreparationClosed));
        }

        [Test]
        public void 참가자가_아니면_아무_일도_일어나지_않는다()
        {
            OpenBetting();

            var r = gate.HandleBet(Stranger, 1, 100, NextTicket(), isMine: false);

            Assert.That(r.HasEvents, Is.False, "남의 방 사람에게 이벤트를 발행하면 안 된다");
            Assert.That(table.BettorCount, Is.Zero);
        }

        [Test]
        public void 같은_요청을_두_번_보내도_골드는_한_번만_빠진다()
        {
            OpenBetting();

            var ticket = NextTicket();
            gate.HandleBet(Alice, 1, 100, ticket, isMine: true);
            var again = gate.HandleBet(Alice, 1, 100, ticket, isMine: true);

            Assert.That(economy.GetGold(Alice), Is.EqualTo(900), "재전송은 정상이다 — 두 번 빠지면 안 된다");
            Assert.That(table.StakedOn(Alice, 1), Is.EqualTo(100));

            Assert.That(again.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(again.HasRejection, Is.False, "이미 성공한 클릭에 거절을 띄우면 거짓말이 된다");
            Assert.That(again.HasBetPlaced, Is.False, "두 번째로 걸린 것이 아니다");
            Assert.That(again.State.MyStakeTotal, Is.EqualTo(100), "재전송에도 현재 상태는 실려 온다");
        }

        // ── 물리기 ──────────────────────────────────────────────────────

        [Test]
        public void 물리면_전액_돌아오고_모은_골드_누계는_오르지_않는다()
        {
            OpenBetting();
            Bet(number: 1, amount: 200);
            Bet(number: 3, amount: 100);

            int inflowBefore = economy.GetTotalInflow(Alice);

            var r = Withdraw();

            Assert.That(r.HasRejection, Is.False);
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000), "낸 돈이 그대로 돌아온다");
            Assert.That(table.StakedBy(Alice), Is.Zero);
            Assert.That(r.Gold.Reason, Is.EqualTo(GoldChangeReason.Refunded),
                "환불을 \"획득\" 으로 보이면 손해를 이득으로 읽는다");

            Assert.That(economy.GetTotalInflow(Alice), Is.EqualTo(inflowBefore),
                "돌려받은 돈은 번 것이 아니다 — 누계에 섞이면 최종 순위 동점 처리가 조용히 틀린다");
        }

        [Test]
        public void 걸린_것이_없으면_물릴_것도_없다()
        {
            OpenBetting();

            var r = Withdraw();

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(RouletteWithdrawRejectReason.NothingToWithdraw));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        [Test]
        public void 회전이_시작되면_물릴_수_없다()
        {
            OpenBetting();
            Bet(number: 1, amount: 200);

            for (int i = 0; i < Ticks; i++)
            {
                Tick();
            }

            var r = Withdraw();

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(RouletteWithdrawRejectReason.NotBetting));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(800), "결과가 정해진 뒤에는 물릴 수 없다");
        }

        [Test]
        public void 구역을_벗어나도_물리기는_막지_않는다()
        {
            OpenBetting();
            Bet(number: 1, amount: 200);
            presence.Place(Alice, FarAway);

            var r = Withdraw();

            Assert.That(r.HasRejection, Is.False, "물리기는 돈을 돌려받는 쪽이라 막아서 지킬 것이 없다");
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        /// <summary>
        /// <b>걸었다 물리기를 반복해도 "모은 골드" 는 한 푼도 늘지 않는다</b> (S-GD-7VKZA5).
        ///
        /// <para>
        /// 한 번만 확인하면 <b>회수를 획득으로 세는 결함이 작아 보인다</b> — 잔액은 여전히 맞고
        /// 누계만 베팅액만큼 는다. 그런데 이건 <b>플레이어가 마음대로 반복할 수 있는 조작</b>이라,
        /// 준비 시간 동안 걸고 물리기를 계속하면 최종 순위 동점 처리 5단계
        /// (<c>Wallet.TotalInflow</c>, S-GD-MNFBV7)를 <b>원하는 만큼 부풀릴 수 있다.</b>
        /// 잔액은 끝까지 정상이라 아무도 눈치채지 못한다.
        /// </para>
        /// </summary>
        [Test]
        public void 걸었다_물리기를_반복해도_모은_골드_누계는_그대로다()
        {
            OpenBetting();

            int inflowBefore = economy.GetTotalInflow(Alice);
            int goldBefore = economy.GetGold(Alice);

            for (int i = 0; i < 5; i++)
            {
                Bet(number: 1, amount: 300);
                Bet(number: 20, amount: 200);

                Assert.That(economy.GetGold(Alice), Is.EqualTo(goldBefore - 500), "거는 순간 빠진다");

                var r = Withdraw();

                Assert.That(r.HasRejection, Is.False);
                Assert.That(economy.GetGold(Alice), Is.EqualTo(goldBefore), "낸 만큼 그대로 돌아온다");
                Assert.That(economy.GetTotalInflow(Alice), Is.EqualTo(inflowBefore),
                    $"{i + 1}번째 물리기에서 누계가 늘었다 — 반복하면 순위를 마음대로 부풀릴 수 있다");
            }
        }

        /// <summary>
        /// <b>물렸다 다시 걸어 적중해도 누계에는 상금분만 들어간다.</b>
        ///
        /// <para>
        /// 물리기 경로와 정산 경로가 <b>같은 지갑</b>을 만지므로, 한쪽만 맞아도 합쳐 놓으면
        /// 어긋난다. 여기서는 걸고 → 물리고 → 다시 걸고 → 적중까지 한 번에 지나간다.
        /// </para>
        /// </summary>
        [Test]
        public void 물렸다_다시_걸어_적중해도_누계는_상금만큼만_는다()
        {
            OpenBetting();

            int hit = PeekWinningNumber(20260908);

            int inflowBefore = economy.GetTotalInflow(Alice);
            int goldBefore = economy.GetGold(Alice);

            Bet(number: hit, amount: 400);
            Withdraw();

            Assert.That(economy.GetGold(Alice), Is.EqualTo(goldBefore));
            Assert.That(economy.GetTotalInflow(Alice), Is.EqualTo(inflowBefore));

            Bet(number: hit, amount: 100);
            var settlement = RunUntilSettled().Settlements[0];

            int payout = 100 * (hit + 1);

            Assert.That(settlement.Payout, Is.EqualTo(payout));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(goldBefore - 100 + payout));
            Assert.That(economy.GetTotalInflow(Alice), Is.EqualTo(inflowBefore + payout - 100),
                "물린 400 도, 이번에 낸 100 도 번 돈이 아니다 — 누계에는 상금분만 들어간다");
        }

        // ── 정산 ────────────────────────────────────────────────────────

        [Test]
        public void 적중하면_건_돈은_환불로_상금만_획득으로_들어간다()
        {
            OpenBetting();

            int hit = PeekWinningNumber(20260908);
            Bet(number: hit, amount: 100);

            int inflowBefore = economy.GetTotalInflow(Alice);
            int goldBefore = economy.GetGold(Alice);

            var outcome = RunUntilSettled();

            Assert.That(outcome.Settlements.Length, Is.EqualTo(1));
            var s = outcome.Settlements[0];

            int payout = 100 * (hit + 1);

            Assert.That(s.WinningNumber, Is.EqualTo(hit));
            Assert.That(s.WasRefund, Is.False);
            Assert.That(s.Payout, Is.EqualTo(payout));
            Assert.That(s.Net, Is.EqualTo(payout - 100));
            Assert.That(s.Loss, Is.Zero, "딴 판은 손실이 아니다");

            Assert.That(economy.GetGold(Alice), Is.EqualTo(goldBefore + payout));

            // ★ 핵심: 돌려받은 100 은 번 것이 아니다. 누계에는 상금분만 들어간다.
            Assert.That(economy.GetTotalInflow(Alice), Is.EqualTo(inflowBefore + payout - 100),
                "회수액에는 내가 낸 돈이 섞여 있다 — 통째로 넣으면 순위 근거가 부푼다");
        }

        [Test]
        public void 전부_틀리면_건_돈을_전부_잃고_그_값이_축복_트리거가_된다()
        {
            OpenBetting();

            int hit = PeekWinningNumber(20260908);
            int miss = 0;

            foreach (var number in PlaceholderRouletteWheel.Numbers)
            {
                if (number != hit)
                {
                    miss = number;
                    break;
                }
            }

            Bet(number: miss, amount: 200);

            int inflowBefore = economy.GetTotalInflow(Alice);
            var s = RunUntilSettled().Settlements[0];

            Assert.That(s.Payout, Is.Zero);
            Assert.That(s.Net, Is.EqualTo(-200));
            Assert.That(s.Loss, Is.EqualTo(200), "축복 ON_ROULETTE_LOSS 가 이 값을 쓴다");
            Assert.That(economy.GetGold(Alice), Is.EqualTo(800));
            Assert.That(economy.GetTotalInflow(Alice), Is.EqualTo(inflowBefore));
        }

        /// <summary>
        /// <b>본전으로 끝난 판을 환불로 착각하면 안 된다.</b>
        ///
        /// <para>
        /// 여러 숫자에 나눠 걸면 <c>회수 == 건 돈</c> 인 판이 실제로 나온다. 그때
        /// <c>Payout == Staked</c> 로 환불을 판정하면 <b>돌지도 않은 판으로 취급되어</b>
        /// 화면이 결과 숫자를 못 그리고 축복 트리거도 어긋난다.
        /// </para>
        /// </summary>
        [Test]
        public void 나눠_걸어_본전이_난_판은_환불이_아니다()
        {
            OpenBetting();

            int hit = PeekWinningNumber(20260908);
            int other = 0;

            foreach (var number in PlaceholderRouletteWheel.Numbers)
            {
                if (number != hit)
                {
                    other = number;
                    break;
                }
            }

            // 적중에 10 · 다른 곳에 10×hit → 회수 10×(hit+1) = 건 돈과 같다.
            Bet(number: hit, amount: MinBet);
            Bet(number: other, amount: MinBet * hit);

            var s = RunUntilSettled().Settlements[0];

            Assert.That(s.Net, Is.Zero, "이 판은 본전이다");
            Assert.That(s.WasRefund, Is.False, "본전과 환불은 다른 사건이다");
            Assert.That(s.WinningNumber, Is.EqualTo(hit), "환불로 잘못 보면 결과 숫자가 사라진다");
            Assert.That(s.WinningSlot, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void 돌지_않은_판의_돈은_돌려주고_손실로_세지_않는다()
        {
            OpenBetting();
            Bet(number: 1, amount: 300, playerId: Alice);
            Bet(number: 20, amount: 100, playerId: Bob);

            int aliceInflow = economy.GetTotalInflow(Alice);
            int bobInflow = economy.GetTotalInflow(Bob);

            var outcome = gate.Close(Alice, isMine: true);

            Assert.That(table.State, Is.EqualTo(RouletteState.Closed));
            Assert.That(outcome.Settlements.Length, Is.EqualTo(2), "건 사람 전원분이 나온다");

            foreach (var s in outcome.Settlements)
            {
                Assert.That(s.WasRefund, Is.True);
                Assert.That(s.Net, Is.Zero);
                Assert.That(s.Loss, Is.Zero, "플레이어가 통제할 수 없는 일이라 축복 트리거가 걸리면 안 된다");
                Assert.That(s.WinningNumber, Is.Zero);
                Assert.That(s.WinningSlot, Is.EqualTo(-1));
            }

            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
            Assert.That(economy.GetGold(Bob), Is.EqualTo(1_000));
            Assert.That(economy.GetTotalInflow(Alice), Is.EqualTo(aliceInflow));
            Assert.That(economy.GetTotalInflow(Bob), Is.EqualTo(bobInflow));
        }

        [Test]
        public void 정산_이벤트는_보는_사람_것만_내_것으로_표시된다()
        {
            OpenBetting();
            Bet(number: 1, amount: 100, playerId: Alice);
            Bet(number: 1, amount: 100, playerId: Bob);

            var outcome = gate.Close(Alice, isMine: true);

            Assert.That(outcome.Settlements.Length, Is.EqualTo(2));
            Assert.That(outcome.Golds.Length, Is.EqualTo(2), "정산과 골드 이벤트는 짝이다");

            foreach (var s in outcome.Settlements)
            {
                Assert.That(s.IsMine, Is.EqualTo(s.PlayerId == Alice),
                    "거르지 않으면 남의 결과가 내 HUD 에 찍힌다");
            }
        }

        // ── 화면에 보낼 것 ───────────────────────────────────────────────

        [Test]
        public void 바퀴_공지는_배당표와_배치를_그대로_싣는다()
        {
            var wheel = gate.BuildWheel();

            Assert.That(wheel.Layout.Length, Is.EqualTo(25));
            Assert.That(wheel.MinBet, Is.EqualTo(MinBet));
            Assert.That(wheel.Numbers.Length, Is.EqualTo(PlaceholderRouletteWheel.Numbers.Length));

            foreach (var view in wheel.Numbers)
            {
                Assert.That(view.PayoutMultiplier, Is.EqualTo(view.Number + 1), "총 회수는 ×(N+1) 이다");
                Assert.That(view.SlotCount, Is.GreaterThan(0));
            }

            // 화면이 회전 각도를 구할 수 있어야 한다 — 배치가 곧 확률표이자 그림이다.
            Assert.That(wheel.Layout, Is.EqualTo(PlaceholderRouletteWheel.Layout));
        }

        [Test]
        public void 배열은_비어도_null_이_아니다()
        {
            var state = gate.BuildState(Alice, isMine: true);

            Assert.That(state.MyBets, Is.Not.Null, "화면의 null 검사 누락은 늘 생긴다");
            Assert.That(state.MyBets, Is.Empty);

            var outcome = gate.Tick(PlentyOfTime, random, Alice, isMine: true);

            Assert.That(outcome.Settlements, Is.Not.Null);
            Assert.That(outcome.Golds, Is.Not.Null);
        }

        [Test]
        public void 매_틱_상태를_발행하지_않는다()
        {
            OpenBetting();

            // 베팅 시간 한복판 — 상태는 그대로이고 남은 시간만 줄어든다.
            var quiet = Tick();

            Assert.That(quiet.HasStateChange, Is.False,
                "매 틱 발행하면 방 인원수만큼 이벤트가 매 틱 늘어난다 — 카운트다운은 화면이 센다");
        }

        [Test]
        public void 마감_시간에는_새_판을_열지_않는다()
        {
            var outcome = gate.Tick(preparationRemainingTicks: 1, random, Alice, isMine: true);

            Assert.That(table.State, Is.EqualTo(RouletteState.Closed));
            Assert.That(table.Cycle, Is.Zero, "판이 아예 안 열렸다");
            Assert.That(outcome.HasStateChange, Is.True);
            Assert.That(outcome.State.CanBet, Is.False);
        }

        // ── 검증용 대역 ─────────────────────────────────────────────────

        [Test]
        public void 룰렛_환급_축복을_가지면_잃은_금액의_일부가_돌아온다()
        {
            // 엑셀 전용 축복 roulette-refund-t1(잃은 금액의 30%). 실버 후보가 셋뿐이라 반드시 뜬다.
            var refund = new BlessingDefinition("roulette-refund-t1", "룰렛 환급", WeaponTag.Common, BlessingTier.Silver);
            BettingRoyale.Data.Remote.GameContentProvider.Set(new BettingRoyale.Data.Remote.GameContent(2, null,
                new[] { refund }, null, null, null,
                new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal)
                {
                    [refund.Id] = new BlessingMvpDefinition(refund, "roulette_refund", -1, BlessingStackRule.Unique,
                        BlessingDurationKind.Permanent, new[] { "refund_fraction" }, new[] { 0.30f }),
                }));
            try
            {
                var pool = new List<BlessingDefinition>
                {
                    refund,
                    new BlessingDefinition("filler-a", "채움 A", WeaponTag.Common, BlessingTier.Silver),
                    new BlessingDefinition("filler-b", "채움 B", WeaponTag.Common, BlessingTier.Silver),
                };
                foreach (var tier in new[] { BlessingTier.Gold, BlessingTier.Platinum })
                    for (int i = 0; i < 3; i++)
                        pool.Add(new BlessingDefinition($"{tier}-{i}", $"{tier} {i}", WeaponTag.Common, tier));

                economy = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, pool);
                economy.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, seed: 4242);
                ledger = new EconomyRequestLedger();
                economy.BeginPreparation();
                ledger.OpenPreparation();
                var candidates = economy.GetCandidates(Alice);
                int index = -1;
                for (int i = 0; i < candidates.Count; i++) if (candidates[i].Blessing.Id == refund.Id) index = i;
                Assert.That(economy.TrySelect(Alice, index)?.Id, Is.EqualTo(refund.Id));

                table = new RouletteTable(PlaceholderRouletteWheel.Create(), Config());
                gate = new RouletteRequestGate(economy, ledger, table,
                    new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence));
                random = new DeterministicRandom(20260908);
                sequence = 0;

                OpenBetting();
                int hit = PeekWinningNumber(20260908);
                int miss = hit == 1 ? 2 : 1;
                int before = economy.GetGold(Alice);
                Bet(number: miss, amount: 100);

                var outcome = RunUntilSettled();

                Assert.That(outcome.Settlements[0].Loss, Is.EqualTo(100));
                Assert.That(economy.GetGold(Alice), Is.EqualTo(before - 100 + 30), "잃은 100 의 30% 가 돌아온다");
                Assert.That(outcome.Golds[0].Delta, Is.EqualTo(30), "지갑 변화 알림에도 환급이 실린다");
            }
            finally
            {
                BettingRoyale.Data.Remote.GameContentProvider.Reset();
            }
        }

        private sealed class FakePresence : IPreparationPresenceProvider
        {
            private readonly Dictionary<int, WorldPosition> byPlayer = new();

            public void Place(int playerId, WorldPosition position) => byPlayer[playerId] = position;

            public void Remove(int playerId) => byPlayer.Remove(playerId);

            public bool TryGetPosition(int playerId, out WorldPosition position) =>
                byPlayer.TryGetValue(playerId, out position);
        }
    }
}
