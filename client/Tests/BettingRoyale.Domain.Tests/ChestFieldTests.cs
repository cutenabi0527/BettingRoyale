using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Common;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 필드 — 몇 개를·어디에·언제까지 (Jira S15P21A606-32, S-GD-MQP6AS, S-GD-5Y4PTJ).
    ///
    /// 테스트 이름을 티켓 완료 조건 문장에 맞춰 붙여 체크박스와 1:1 로 대조된다.
    ///
    /// <para>
    /// ⚠️ 여기 쓰는 숫자(개봉 5틱·재생성 3틱·보상 500G)는 <b>스펙이 아니다.</b>
    /// 실제 값은 미정이고 설정으로 주입된다 — S-GD-6DNSGY 는 권장안이지 확정이 아니다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ChestFieldTests
    {
        private const int OpenTicks = 5;
        private const int RespawnTicks = 3;
        /// <summary>재시도 지연. ⚠️ 검증용 임의값이다 — 확정값 1.0초는 데이터로 들어온다 (S-GD-5AZ6HN).</summary>
        private const int RetryTicks = 3;
        private const int Reward = 500;
        private const int Alice = 1;
        private const int Bob = 2;

        /// <summary>앵커 8곳 — 전장 프리팹의 ChestAnchor_01~08 을 흉내낸 자리표시자다.</summary>
        private static IReadOnlyList<WorldPosition> Anchors() => new[]
        {
            new WorldPosition(-7f, 0f, 8f), new WorldPosition(7f, 0f, 8f),
            new WorldPosition(-7f, 0f, -8f), new WorldPosition(7f, 0f, -8f),
            new WorldPosition(0f, 0f, 6f), new WorldPosition(0f, 0f, -6f),
            new WorldPosition(-12f, 0f, 0f), new WorldPosition(12f, 0f, 0f),
        };

        private static ChestFieldConfig Config(int chestCount = 2) =>
            new ChestFieldConfig(
                new ChestConfig(OpenTicks, RespawnTicks, RetryTicks),
                Anchors(),
                chestCount,
                new ChestReward(Reward));

        private static ChestField OpenedField(int seed = 4242, int chestCount = 2)
        {
            var field = new ChestField(Config(chestCount));
            field.BeginRound(new DeterministicRandom(seed));
            return field;
        }

        // ── 완료 조건 1: 위치·상태·진행도 (도메인 절반) ──────────────────────

        [Test]
        public void 완료조건1_전투_시작에_상자가_두_개_놓인다()
        {
            var field = OpenedField();

            Assert.That(field.Count, Is.EqualTo(2), "S-GD-MQP6AS 가 2개로 확정했다");
            Assert.That(field.BuildPlacements().Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Host 가 뽑아 복제하는 값이라 <b>같은 시드면 같은 두 자리</b>여야 한다.
        /// 여기가 깨지면 Host 와 Client 가 다른 곳에서 상자를 본다.
        /// </summary>
        [Test]
        public void 완료조건1_같은_시드면_같은_위치가_나온다()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var first = OpenedField(seed).BuildPlacements();
                var second = OpenedField(seed).BuildPlacements();

                for (int i = 0; i < first.Count; i++)
                {
                    Assert.That(second[i].AnchorIndex, Is.EqualTo(first[i].AnchorIndex),
                        $"seed {seed} 의 {i}번 상자 자리가 갈렸습니다");
                    Assert.That(second[i].Position.X, Is.EqualTo(first[i].Position.X));
                    Assert.That(second[i].Position.Z, Is.EqualTo(first[i].Position.Z));
                }
            }
        }

        [Test]
        public void 완료조건1_시드가_다르면_자리도_달라진다()
        {
            var seen = new HashSet<string>();

            for (int seed = 1; seed <= 40; seed++)
            {
                var p = OpenedField(seed).BuildPlacements();
                seen.Add(string.Join(",", p.Select(x => x.AnchorIndex)));
            }

            Assert.That(seen.Count, Is.GreaterThan(1),
                "어떤 시드에서도 같은 자리만 나오면 무작위 배치가 아니라 고정 배치다");
        }

        [Test]
        public void 완료조건1_진행도는_개봉_틱에_따라_오른다()
        {
            var field = OpenedField();
            Assert.That(field.TryBeginOpen(chestId: 0, Alice), Is.True);

            var before = field.BuildPlacements()[0].Progress;
            field.Tick();
            var after = field.BuildPlacements()[0].Progress;

            Assert.That(after, Is.GreaterThan(before));
            Assert.That(field.BuildPlacements()[0].State, Is.EqualTo(ChestState.Opening));
            Assert.That(field.BuildPlacements()[0].OpenerId, Is.EqualTo(Alice));
        }

        // ── 완료 조건 2: 취소에서 보상 미지급 ────────────────────────────────

        [Test]
        public void 완료조건2_이동으로_취소되면_보상이_없다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();

            var cancelled = field.CancelOpenBy(Alice, ChestCancelReason.Moved);

            Assert.That(cancelled.Count, Is.EqualTo(1));
            Assert.That(cancelled[0].Reason, Is.EqualTo(ChestCancelReason.Moved));
            Assert.That(cancelled[0].OpenerId, Is.EqualTo(Alice));

            for (int i = 0; i < OpenTicks + 2; i++)
            {
                Assert.That(field.Tick(), Is.Empty, "취소된 개봉이 나중에 보상을 내면 안 된다");
            }
        }

        [Test]
        public void 완료조건2_피격으로_취소되면_보상이_없다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();

            Assert.That(field.CancelOpenBy(Alice, ChestCancelReason.Damaged).Count, Is.EqualTo(1));
            Assert.That(field.BuildPlacements()[0].State, Is.EqualTo(ChestState.Idle));
        }

        [Test]
        public void 완료조건2_라운드_종료로_취소되면_보상이_없다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.Tick();

            var cancelled = field.EndRound();

            Assert.That(cancelled.Count, Is.EqualTo(1));
            Assert.That(cancelled[0].Reason, Is.EqualTo(ChestCancelReason.RoundEnded),
                "개봉자 잘못이 아니므로 이동·피격과 사유가 구분돼야 한다");
            Assert.That(field.Count, Is.EqualTo(0));
        }

        [Test]
        public void 완료조건2_남의_이동은_내_개봉을_끊지_않는다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);

            Assert.That(field.CancelOpenBy(Bob, ChestCancelReason.Moved), Is.Empty);
            Assert.That(field.BuildPlacements()[0].State, Is.EqualTo(ChestState.Opening));
        }

        /// <summary>
        /// <b>라운드 종료는 여러 건을 돌려준다.</b> 사람이 여럿이기 때문이다.
        ///
        /// <para>
        /// S-GD-PTVC2X("한 사람에 한 상자")가 최대 1건으로 줄이는 것은
        /// <c>CancelOpenBy(playerId, ...)</c> 뿐이다. <c>ChestRequestGate</c> 가
        /// <c>cancelled[0]</c> 만 싣는 것이 안전한 이유는 그 경계에서만 부르기 때문이고,
        /// <b>나중에 게이트에 라운드 종료 핸들러를 붙이는 사람은 이 차이를 알아야 한다.</b>
        /// </para>
        /// </summary>
        [Test]
        public void 완료조건2_라운드_종료는_여러_사람의_개봉을_한꺼번에_끊는다()
        {
            var field = OpenedField();
            Assert.That(field.TryBeginOpen(0, Alice), Is.True);
            Assert.That(field.TryBeginOpen(1, Bob), Is.True);

            var cancelled = field.EndRound();

            Assert.That(cancelled.Count, Is.EqualTo(2), "사람이 둘이라 취소도 둘이다");
            Assert.That(
                new[] { cancelled[0].OpenerId, cancelled[1].OpenerId },
                Is.EquivalentTo(new[] { Alice, Bob }));
        }

        // ── 완료 조건 3: 보상은 한 번만 ──────────────────────────────────────

        [Test]
        public void 완료조건3_개봉이_끝나면_보상이_정확히_한_번_나온다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);

            var all = new List<ChestRewardEarned>();
            for (int i = 0; i < OpenTicks + RespawnTicks + 5; i++)
            {
                all.AddRange(field.Tick());
            }

            Assert.That(all.Count, Is.EqualTo(1), "틱을 더 돌린다고 보상이 또 나오면 안 된다");
            Assert.That(all[0].ChestId, Is.EqualTo(0));
            Assert.That(all[0].ClaimerId, Is.EqualTo(Alice));
            Assert.That(all[0].Gold, Is.EqualTo(Reward));
        }

        [Test]
        public void 완료조건3_상자마다_보상이_따로_나온다()
        {
            var field = OpenedField();
            field.TryBeginOpen(0, Alice);
            field.TryBeginOpen(1, Bob);

            var all = new List<ChestRewardEarned>();
            for (int i = 0; i < OpenTicks + 2; i++)
            {
                all.AddRange(field.Tick());
            }

            Assert.That(all.Count, Is.EqualTo(2));
            Assert.That(all.Select(x => x.ClaimerId), Is.EquivalentTo(new[] { Alice, Bob }));
        }

        [Test]
        public void 완료조건3_이미_열고_있는_상자는_남이_못_연다()
        {
            var field = OpenedField();
            Assert.That(field.TryBeginOpen(0, Alice), Is.True);
            Assert.That(field.TryBeginOpen(0, Bob), Is.False, "동시 개봉 경쟁의 승자는 하나다");
        }

        // ── 완료 조건 4: 중복 상자 없음 ──────────────────────────────────────

        [Test]
        public void 완료조건4_두_상자가_같은_자리에_놓이지_않는다()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var p = OpenedField(seed).BuildPlacements();
                var anchors = p.Select(x => x.AnchorIndex).ToList();

                Assert.That(anchors.Distinct().Count(), Is.EqualTo(anchors.Count),
                    $"seed {seed} 에서 같은 앵커가 두 번 뽑혔습니다 — 화면엔 하나로 보이고 도메인은 둘로 셉니다");
            }
        }

        /// <summary>
        /// 재생성은 <b>같은 자리·같은 id</b> 다 (S-GD-MQP6AS "동일 위치 재생성").
        /// 새 상자를 만들면 화면에 둘이 겹쳐 보인다.
        /// </summary>
        [Test]
        public void 완료조건4_재생성_전후에_상자_수와_자리가_그대로다()
        {
            var field = OpenedField();
            var before = field.BuildPlacements()[0];

            field.TryBeginOpen(0, Alice);
            for (int i = 0; i < OpenTicks + RespawnTicks + 2; i++)
            {
                field.Tick();
            }

            var after = field.BuildPlacements()[0];

            Assert.That(field.Count, Is.EqualTo(2), "재생성이 상자를 늘리면 안 된다");
            Assert.That(after.ChestId, Is.EqualTo(before.ChestId));
            Assert.That(after.AnchorIndex, Is.EqualTo(before.AnchorIndex));
            Assert.That(after.Position.X, Is.EqualTo(before.Position.X));
            Assert.That(after.State, Is.EqualTo(ChestState.Idle), "재생성이 끝나면 다시 열 수 있어야 한다");
        }

        [Test]
        public void 완료조건4_라운드를_다시_열면_지난_상자가_남지_않는다()
        {
            var field = OpenedField();
            field.EndRound();
            field.BeginRound(new DeterministicRandom(99));

            Assert.That(field.Count, Is.EqualTo(2), "지난 라운드 상자가 남으면 4개가 된다");
        }

        // ── 경계 ────────────────────────────────────────────────────────────

        [Test]
        public void 라운드가_열리지_않았으면_아무_동작도_못_한다()
        {
            var field = new ChestField(Config());

            Assert.Throws<System.InvalidOperationException>(() => field.Tick());
            Assert.Throws<System.InvalidOperationException>(() => field.TryBeginOpen(0, Alice));
            Assert.That(field.BuildPlacements(), Is.Empty);
        }

        [Test]
        public void 라운드를_닫지_않고_다시_열_수_없다()
        {
            var field = OpenedField();

            Assert.Throws<System.InvalidOperationException>(
                () => field.BeginRound(new DeterministicRandom(1)),
                "안 막으면 지난 라운드 상자가 조용히 사라지거나 남는다");
        }

        [Test]
        public void 없는_상자_번호는_예외가_아니라_거부다()
        {
            var field = OpenedField();

            Assert.That(field.TryBeginOpen(chestId: 99, Alice), Is.False);
            Assert.That(field.TryBeginOpen(chestId: -1, Alice), Is.False);
        }

        [Test]
        public void 앵커_후보가_상자_수보다_적으면_만들_수_없다()
        {
            Assert.Throws<System.ArgumentException>(() => new ChestFieldConfig(
                new ChestConfig(OpenTicks, RespawnTicks, RetryTicks),
                new[] { new WorldPosition(0f, 0f, 0f) },
                chestCount: 2,
                reward: new ChestReward(Reward)));
        }

        // ── 보상 타입 — 나중에 소모품·장신구를 더할 자리 ──────────────────

        /// <summary>
        /// 보상이 <see cref="ChestReward"/> 로 통째로 흐른다. 이 경로가 유지되는 한
        /// 소모품·장신구를 더해도 <see cref="ChestFieldConfig"/> 와
        /// <see cref="ChestRewardEarned"/> 의 시그니처는 안 바뀐다.
        /// </summary>
        [Test]
        public void 설정에_넣은_보상이_그대로_지급_결과에_실린다()
        {
            var field = OpenedField();
            Assert.That(field.TryBeginOpen(chestId: 0, Alice), Is.True);

            ChestRewardEarned? claimed = null;
            for (int i = 0; i < OpenTicks + 2 && claimed == null; i++)
            {
                foreach (var earned in field.Tick())
                {
                    claimed = earned;
                }
            }

            Assert.That(claimed.HasValue, Is.True, "전제: 보상이 나와야 한다");
            Assert.That(claimed.Value.Reward.Gold, Is.EqualTo(Reward));
            Assert.That(claimed.Value.Gold, Is.EqualTo(Reward), "Gold 는 Reward.Gold 의 지름길이다");
        }

        [Test]
        public void 골드가_없는_보상도_유효하다()
        {
            var empty = new ChestReward(0);

            Assert.That(empty.Gold, Is.EqualTo(0));
            Assert.That(empty.IsEmpty, Is.True,
                "아이템 전용 상자를 표현할 수 있어야 하므로 0 은 유효한 값이다");
        }

        [Test]
        public void 음수_보상은_거부한다()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ChestReward(-1));
        }
    }
}
