using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Roulette;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 2026-09-09 적대적 검증에서 나온 결함들의 재발 방지 (Jira S15P21A606-107).
    ///
    /// <para>
    /// <b>574개가 통과하는 상태에서 발견된 것들이다.</b> 기존 테스트가 하나도 못 잡았다는 뜻이라
    /// 여기에 따로 모아 둔다 — 각 테스트는 <b>고치기 전에는 실패했어야 하는 것</b>이다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UpgradeRouletteRegressionTests
    {
        /// <summary>첫 판 지연 구간을 지나 베팅이 열릴 때까지 돌린다.</summary>
        private static void OpenFirstRound(
            RouletteTable table, RouletteConfig config, DeterministicRandom random, int remaining)
        {
            for (int i = 0; i <= config.StartDelayTicks; i++)
            {
                table.Tick(remaining, random);

                if (table.State == RouletteState.Betting)
                {
                    return;
                }
            }

            Assert.Fail("첫 판 지연을 지나도 베팅이 열리지 않았다");
        }

        // ── 룰렛: 시작한 판은 반드시 끝까지 돈다 (S-GD-7VKZA5) ──────────

        /// <summary>
        /// <b>확정 임시값에서 유령 베팅창이 떴다.</b> 마감 리드타임 20초가 한 판(10+10+2=22초)보다
        /// 짧아서, 남은 시간이 21초일 때 판을 열고 20초에 취소했다.
        ///
        /// <para>
        /// 화면에는 베팅창이 뜨는데 <b>아무도 돈을 못 걸고 사라진다.</b> 스펙이 약속한
        /// *"시작한 판은 반드시 끝까지 돌게 한다"* 가 거짓이었다.
        /// </para>
        /// </summary>
        [Test]
        public void 끝까지_돌_시간이_없으면_판을_아예_열지_않는다()
        {
            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);
            var random = new DeterministicRandom(1);

            // 기준선과 정확히 같은 상태 — "이하면 열지 않는다" 이므로 열리면 안 된다.
            table.Tick(config.MinTicksToOpen, random);

            Assert.That(table.State, Is.EqualTo(RouletteState.Closed),
                "열면 반드시 취소되는 판이라 애초에 열지 않아야 한다");
            Assert.That(table.Cycle, Is.Zero, "유령 판이 판 번호를 올리면 안 된다");
        }

        [Test]
        public void 끝까지_돌_시간이_있으면_연다()
        {
            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);

            OpenFirstRound(table, config, new DeterministicRandom(1), config.MinTicksToOpen + 1);

            Assert.That(table.State, Is.EqualTo(RouletteState.Betting));
            Assert.That(table.Cycle, Is.EqualTo(1));
        }

        /// <summary>
        /// 연 판은 끝까지 돈다 — 여는 순간의 남은 시간이 정확히 한 판일 때도 정산까지 간다.
        /// </summary>
        [Test]
        public void 아슬아슬하게_연_판도_정산까지_간다()
        {
            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);
            var random = new DeterministicRandom(1);

            int remaining = config.MinTicksToOpen + config.StartDelayTicks + 1;
            OpenFirstRound(table, config, random, remaining);
            remaining -= config.StartDelayTicks;
            table.RecordBet(1, 1, 100);

            bool settled = false;

            for (int i = 0; i < config.CycleTicks + 4; i++)
            {
                remaining--;

                if (table.Tick(remaining, random).Length > 0)
                {
                    settled = true;
                    break;
                }
            }

            Assert.That(settled, Is.True, "건 돈이 정산도 환불도 없이 사라지면 안 된다");

            // ⚠️ 위 단언만으로는 이 테스트가 무의미하다. Tick 은 환불에서도 비어 있지 않은
            // 배열을 돌려주므로(CloseAndRefund) Length > 0 은 "정산됐다" 와
            // "취소돼서 환불됐다" 를 구분하지 못한다 — 그 둘의 차이가 정확히 이 테스트가
            // 지키려는 결함(유령 베팅창)이다. 상태까지 봐야 취소 로직이 되살아난 것을 잡는다.
            Assert.That(
                table.State,
                Is.EqualTo(RouletteState.Result),
                "회전을 마치고 결과까지 갔어야 한다 — Closed 면 중간에 취소된 것이다");
        }

        /// <summary>
        /// <b>준비 90초에서 실제로 몇 판이 도는가</b> (기획 확정 2026-09-10).
        ///
        /// <para>
        /// 한 판 20초(베팅 10 + 회전 8 + 결과 2), 여유 10초.
        /// <b>남은 시간이 30초 이하면 열지 않는다</b> — 룰렛이 돌다가 전투로 넘어가는 것을 막는다.
        /// </para>
        ///
        /// <para>
        /// <b>준비 시간은 설정으로 바뀔 예정이므로</b> 여기서 숫자를 못 박는 대신
        /// 실제로 돌려서 세고, <b>마지막 판이 준비 시간을 넘기지 않는 것</b>을 확인한다.
        /// </para>
        /// </summary>
        [Test]
        public void 준비_90초에서_판이_몇_번_도는지_실제로_센다()
        {
            const int PreparationSeconds = 90;

            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);
            var random = new DeterministicRandom(20260910);

            Assert.That(config.CycleTicks, Is.EqualTo(20), "베팅 10 + 회전 8 + 결과 2");
            Assert.That(config.MinTicksToOpen, Is.EqualTo(30), "한 판 20 + 여유 10");

            int settledCycles = 0;
            int lastSettleAt = -1;

            for (int remaining = PreparationSeconds; remaining > 0; remaining--)
            {
                if (table.Tick(remaining, random).Length >= 0 && table.State == RouletteState.Result
                    && table.RemainingTicks == config.ResultTicks)
                {
                    settledCycles++;
                    lastSettleAt = remaining;
                }
            }

            Assert.That(settledCycles, Is.EqualTo(3), "준비 90초에서 세 판이 돈다");
            Assert.That(lastSettleAt, Is.GreaterThan(0),
                $"마지막 판이 준비 시간 안에서 끝나야 한다 (남은 {lastSettleAt}초에 정산)");
            Assert.That(table.State, Is.EqualTo(RouletteState.Closed), "끝에는 닫혀 있어야 한다");
        }

        /// <summary>
        /// <b>첫 판은 준비 시작 10초 뒤에 열린다</b> (기획 확정 2026-09-10).
        /// 그 구간은 축복을 고르는 시간이다 — 룰렛이 먼저 뜨면 그쪽에 정신이 팔린다.
        /// </summary>
        [Test]
        public void 첫_판은_준비_시작_직후가_아니라_지연_뒤에_열린다()
        {
            const int PreparationSeconds = 90;

            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);
            var random = new DeterministicRandom(1);

            Assert.That(config.StartDelayTicks, Is.EqualTo(10));

            int remaining = PreparationSeconds;

            // 지연 구간 동안은 열리지 않는다.
            for (int i = 0; i < config.StartDelayTicks - 1; i++)
            {
                table.Tick(remaining--, random);
                Assert.That(table.State, Is.EqualTo(RouletteState.Idle),
                    $"{i + 1}초에 벌써 열렸다 — 축복 고르는 시간을 침범한다");
            }

            table.Tick(remaining, random);
            Assert.That(table.State, Is.EqualTo(RouletteState.Betting), "10초가 지나면 열린다");
        }

        /// <summary>
        /// <b>지연은 첫 판에만 걸린다.</b> 두 번째 판부터는 앞 판이 끝나는 대로 이어서 연다 —
        /// 판마다 10초를 쉬면 준비 시간 안에 두 판밖에 못 돈다.
        /// </summary>
        [Test]
        public void 두_번째_판부터는_지연_없이_이어서_열린다()
        {
            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);
            var random = new DeterministicRandom(1);

            int remaining = 90;

            // 첫 판이 끝날 때까지 돌린다.
            while (table.Cycle < 1 || table.State != RouletteState.Idle)
            {
                table.Tick(remaining--, random);
            }

            int idleAt = remaining;
            table.Tick(remaining, random);

            Assert.That(table.State, Is.EqualTo(RouletteState.Betting),
                $"첫 판이 끝난 바로 다음 틱(남은 {idleAt}초)에 두 번째 판이 열려야 한다");
            Assert.That(table.Cycle, Is.EqualTo(2));
        }

        /// <summary>
        /// <b>지연 판정에 판 번호를 쓰면 안 된다.</b> 판 번호는 매치 내내 이어지므로
        /// 2라운드부터는 0 이 아니어서 <b>지연이 통째로 사라진다.</b>
        /// </summary>
        [Test]
        public void 두_번째_라운드에서도_첫_판_지연이_걸린다()
        {
            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);
            var random = new DeterministicRandom(1);

            // 1라운드를 한 판 돌리고 닫는다.
            int remaining = 90;

            while (table.Cycle < 1)
            {
                table.Tick(remaining--, random);
            }

            table.Close();

            // 2라운드.
            table.BeginPreparation();
            Assert.That(table.Cycle, Is.EqualTo(1), "판 번호는 매치 내내 이어진다");

            table.Tick(90, random);

            Assert.That(table.State, Is.EqualTo(RouletteState.Idle),
                "2라운드에서도 첫 판은 지연 뒤에 열려야 한다");
        }

        // ── 룰렛: 라운드마다 다시 열린다 ────────────────────────────────

        /// <summary>
        /// <b>1라운드에만 룰렛이 돌고 2·3라운드는 조용히 안 열렸다.</b>
        /// 준비 단계가 끝나면 <c>Closed</c> 가 되는데 <c>Tick</c> 이 그 상태에서 곧바로
        /// 되돌아가므로 <b>스스로는 절대 되살아나지 못했다.</b> 오류도 로그도 없다.
        /// </summary>
        [Test]
        public void 준비_단계를_다시_열면_룰렛도_다시_돈다()
        {
            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);
            var random = new DeterministicRandom(1);

            OpenFirstRound(table, config, random, config.MinTicksToOpen + 1);
            table.Close();
            Assert.That(table.State, Is.EqualTo(RouletteState.Closed));

            // 고치기 전에는 여기서 아무 일도 안 일어났다.
            table.Tick(config.MinTicksToOpen + 1, random);
            Assert.That(table.State, Is.EqualTo(RouletteState.Closed), "닫힌 채로는 안 열린다");

            table.BeginPreparation();
            OpenFirstRound(table, config, random, config.MinTicksToOpen + 1);

            Assert.That(table.State, Is.EqualTo(RouletteState.Betting), "새 준비 단계에서는 다시 열려야 한다");
        }

        [Test]
        public void 새_준비_단계는_지난_베팅을_들고_시작하지_않는다()
        {
            var config = PlaceholderRouletteConfig.Create(ticksPerSecond: 1);
            var table = new RouletteTable(PlaceholderRouletteWheel.Create(), config);
            var random = new DeterministicRandom(1);

            OpenFirstRound(table, config, random, config.MinTicksToOpen + 1);
            table.RecordBet(1, 1, 100);

            table.BeginPreparation();

            Assert.That(table.StakedBy(1), Is.Zero, "남의 돈이 새 판에 걸린 채로 시작하면 안 된다");
            Assert.That(table.WinningNumber, Is.Zero);
            Assert.That(table.WinningSlot, Is.EqualTo(-1));
        }

        // ── 강화: 범위 밖 부위 ──────────────────────────────────────────

        /// <summary>
        /// <b>범위 밖 값이 무기 가격을 내고 방어구 단계를 올렸다.</b>
        /// <c>LevelOf</c> 는 "무기가 아니면 방어구" 로, <c>TryGetTrack</c> 은 "방어구가 아니면 무기" 로
        /// 갈라서 <b>기본 분기가 서로 반대였다.</b> 회선에서 오는 값이라 실제로 도달할 수 있다.
        /// </summary>
        [Test]
        public void 모르는_부위는_조용히_통과하지_않고_터진다()
        {
            var state = new UpgradeState(PlaceholderUpgradeCatalog.Create(), WeaponType.Sword, UpgradeSuccessRoll.AlwaysSucceeds);
            var wallet = new Wallet(10_000);
            var unknown = (UpgradeSlot)7;

            Assert.Throws<ArgumentOutOfRangeException>(() => state.TryUpgrade(unknown, wallet));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.LevelOf(unknown));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.TryGetNextCost(unknown, out _));

            Assert.That(wallet.Gold, Is.EqualTo(10_000), "던지기 전에 골드가 빠지면 안 된다");
            Assert.That(state.WeaponLevel, Is.Zero);
            Assert.That(state.ArmorLevel, Is.Zero);
        }

        // ── 강화: 시트를 한 덩어리로 갈아 끼운다 ────────────────────────

        /// <summary>
        /// <b>걷어낸 뒤 얹다가 던지면 시트가 부르기 전보다 나빠진 채 남았다.</b>
        /// 강화는 매 라운드 다시 얹는 구조라, 데이터가 하나만 잘못돼도
        /// <b>멀쩡하던 기여까지 사라진 상태로 전투가 시작된다.</b>
        /// </summary>
        [Test]
        public void 얹다가_실패해도_시트가_부르기_전보다_나빠지지_않는다()
        {
            var registry = new StatRegistry(new List<StatDefinition>
            {
                new StatDefinition("move-speed", "이동 속도", 5f, StatKind.Multiplier),
            });

            var sheet = new StatSheet(registry);
            sheet.Add("move-speed", 0.10f, StatStackMode.Add, "blessing");
            sheet.Add("move-speed", 0.05f, StatStackMode.Add, "upgrade:weapon");

            float before = sheet.Get("move-speed");

            // 두 번째 기여가 정의에 없는 스탯이라 던진다.
            var broken = new[]
            {
                new StatContribution("move-speed", 0.20f, StatStackMode.Add),
                new StatContribution("no-such-stat", 1f, StatStackMode.Add),
            };

            Assert.Throws<KeyNotFoundException>(() => sheet.ReplaceFrom("upgrade:weapon", broken));

            Assert.That(sheet.Get("move-speed"), Is.EqualTo(before).Within(0.0001f),
                "던졌으면 시트는 손도 안 댄 상태여야 한다");
        }

        [Test]
        public void 갈아_끼우면_그_출처의_기여만_바뀐다()
        {
            var registry = new StatRegistry(new List<StatDefinition>
            {
                new StatDefinition("move-speed", "이동 속도", 5f, StatKind.Multiplier),
            });

            var sheet = new StatSheet(registry);
            sheet.Add("move-speed", 0.10f, StatStackMode.Add, "blessing");
            sheet.Add("move-speed", 0.05f, StatStackMode.Add, "upgrade:weapon");

            sheet.ReplaceFrom("upgrade:weapon", new[]
            {
                new StatContribution("move-speed", 0.20f, StatStackMode.Add),
            });

            // 축복 0.10 은 남고 강화만 0.05 → 0.20
            Assert.That(sheet.Get("move-speed"), Is.EqualTo(5f * 1.30f).Within(0.0001f));
        }

        [Test]
        public void 빈_목록으로_갈아_끼우면_그_출처가_사라진다()
        {
            var registry = new StatRegistry(new List<StatDefinition>
            {
                new StatDefinition("move-speed", "이동 속도", 5f, StatKind.Multiplier),
            });

            var sheet = new StatSheet(registry);
            sheet.Add("move-speed", 0.10f, StatStackMode.Add, "blessing");
            sheet.Add("move-speed", 0.05f, StatStackMode.Add, "upgrade:weapon");

            sheet.ReplaceFrom("upgrade:weapon", null);

            Assert.That(sheet.Get("move-speed"), Is.EqualTo(5f * 1.10f).Within(0.0001f));
        }
    }
}
