using BettingRoyale.Data.Chest;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 보물상자 상태 전이와 중복 보상 방지 (S-GD-5Y4PTJ).
    ///
    /// 개봉 시간·재생성 대기는 스펙 미정값이라 <see cref="ChestConfig"/> 에 기본값이 없다.
    /// 테스트는 검증에 편한 값을 <b>명시적으로</b> 넘긴다 — 이 숫자는 스펙이 아니다.
    /// </summary>
    [TestFixture]
    public class TreasureChestTests
    {
        private const int OpenTicks = 5;
        private const int RespawnTicks = 3;
        /// <summary>재시도 지연. ⚠️ 검증용 임의값이다 — 확정값 1.0초는 데이터로 들어온다 (S-GD-5AZ6HN).</summary>
        private const int RetryTicks = 3;
        private const int Opener = 1;

        [Test]
        public void NoticeSequences_CountOnlySuccessfulTransitionsAndSurviveRespawn()
        {
            var chest = new TreasureChest(new ChestConfig(1, 0, 0));
            Assert.That(chest.TryBeginOpen(Opener), Is.True);
            Assert.That(chest.TryBeginOpen(Opener), Is.False);
            Assert.That(chest.OpeningSequence, Is.EqualTo(1));
            chest.TryCancel(ChestCancelReason.Moved);
            Assert.That(chest.CompletionSequence, Is.Zero);
            chest.TryBeginOpen(Opener);
            chest.Tick(); chest.Tick();
            Assert.That(chest.CompletionSequence, Is.EqualTo(1));
            chest.TryClaimReward(); chest.Tick();
            Assert.That(chest.State, Is.EqualTo(ChestState.Idle));
            Assert.That(chest.OpeningSequence, Is.EqualTo(2));
            Assert.That(chest.CompletionSequence, Is.EqualTo(1));
            chest.TryBeginOpen(Opener); chest.Tick();
            Assert.That(chest.OpeningSequence, Is.EqualTo(3));
            Assert.That(chest.CompletionSequence, Is.EqualTo(2));
        }

        private static TreasureChest MakeChest() =>
            new TreasureChest(new ChestConfig(OpenTicks, RespawnTicks, RetryTicks));

        private static TreasureChest OpenedChest()
        {
            var chest = MakeChest();
            chest.TryBeginOpen(Opener);
            for (var i = 0; i < OpenTicks; i++)
            {
                chest.Tick();
            }

            return chest;
        }

        // ------------------------------------------------------------------ 상태 전이

        [Test]
        public void 처음에는_대기_상태다()
        {
            Assert.That(MakeChest().State, Is.EqualTo(ChestState.Idle));
        }

        [Test]
        public void 개봉을_시작하면_개봉_중이_된다()
        {
            var chest = MakeChest();

            Assert.That(chest.TryBeginOpen(Opener), Is.True);
            Assert.That(chest.State, Is.EqualTo(ChestState.Opening));
            Assert.That(chest.OpenerId, Is.EqualTo(Opener));
        }

        [Test]
        public void 개봉_시간을_다_채우면_보상_상태가_된다()
        {
            var chest = MakeChest();
            chest.TryBeginOpen(Opener);

            for (var i = 0; i < OpenTicks - 1; i++)
            {
                Assert.That(chest.Tick(), Is.False, $"{i + 1}틱에서 너무 일찍 완료됐다");
                Assert.That(chest.State, Is.EqualTo(ChestState.Opening));
            }

            Assert.That(chest.Tick(), Is.True, "마지막 틱에서 완료된다");
            Assert.That(chest.State, Is.EqualTo(ChestState.Rewarded));
        }

        [Test]
        public void 보상을_받으면_재생성_대기로_넘어간다()
        {
            var chest = OpenedChest();

            Assert.That(chest.TryClaimReward(), Is.True);
            Assert.That(chest.State, Is.EqualTo(ChestState.Respawning));
        }

        [Test]
        public void 재생성_대기가_끝나면_다시_대기_상태가_된다()
        {
            var chest = OpenedChest();
            chest.TryClaimReward();

            for (var i = 0; i < RespawnTicks - 1; i++)
            {
                Assert.That(chest.Tick(), Is.False);
                Assert.That(chest.State, Is.EqualTo(ChestState.Respawning));
            }

            Assert.That(chest.Tick(), Is.True);
            Assert.That(chest.State, Is.EqualTo(ChestState.Idle));
            Assert.That(chest.IsRewardClaimed, Is.False, "재생성되면 다시 보상을 줄 수 있다");
        }

        // ---------------------------------------------------------------------- 취소

        [TestCase(ChestCancelReason.Moved)]
        [TestCase(ChestCancelReason.Damaged)]
        [TestCase(ChestCancelReason.Attacked)]
        [TestCase(ChestCancelReason.Dashed)]
        public void 집중을_끊으면_완료직전에도_보상없이_초기화된다(ChestCancelReason reason)
        {
            var chest = MakeChest();
            chest.TryBeginOpen(Opener);
            for (int i = 0; i < OpenTicks - 1; i++) chest.Tick();
            Assert.That(chest.TryCancel(reason), Is.True);
            Assert.That(chest.Tick(), Is.False);
            Assert.That(chest.State, Is.EqualTo(ChestState.Idle));
            Assert.That(chest.OpenProgress, Is.Zero);
            Assert.That(chest.OpenerId, Is.EqualTo(TreasureChest.NoOpener));
            Assert.That(chest.TryClaimReward(), Is.False);
            Assert.That(chest.TryBeginOpen(Opener), Is.True);
            Assert.That(chest.OpenProgress, Is.Zero);
        }

        [Test]
        public void 이동하면_개봉이_취소되고_대기로_돌아간다()
        {
            var chest = MakeChest();
            chest.TryBeginOpen(Opener);
            chest.Tick();

            Assert.That(chest.TryCancel(ChestCancelReason.Moved), Is.True);
            Assert.That(chest.State, Is.EqualTo(ChestState.Idle));
            Assert.That(chest.OpenerId, Is.EqualTo(TreasureChest.NoOpener));
        }

        [Test]
        public void 피격당해도_개봉이_취소된다()
        {
            var chest = MakeChest();
            chest.TryBeginOpen(Opener);

            Assert.That(chest.TryCancel(ChestCancelReason.Damaged), Is.True);
            Assert.That(chest.State, Is.EqualTo(ChestState.Idle));
        }

        [Test]
        public void 취소되면_진행도가_사라지고_다시_처음부터다()
        {
            var chest = MakeChest();
            chest.TryBeginOpen(Opener);
            for (var i = 0; i < OpenTicks - 1; i++)
            {
                chest.Tick();   // 거의 다 열었다
            }

            chest.TryCancel(ChestCancelReason.Moved);
            chest.TryBeginOpen(Opener);

            Assert.That(chest.ElapsedTicks, Is.EqualTo(0), "진행도가 이어지면 안 된다");
            Assert.That(chest.Tick(), Is.False, "한 틱 만에 열려서는 안 된다");
            Assert.That(chest.State, Is.EqualTo(ChestState.Opening));
        }

        [Test]
        public void 개봉_중이_아니면_취소할_것이_없다()
        {
            Assert.That(MakeChest().TryCancel(ChestCancelReason.Moved), Is.False);
            Assert.That(OpenedChest().TryCancel(ChestCancelReason.Moved), Is.False,
                "이미 열린 상자는 취소되지 않는다");
        }

        // ------------------------------------------------------------- 중복 보상 방지

        [Test]
        public void 같은_상자에서_보상을_두_번_받을_수_없다()
        {
            var chest = OpenedChest();

            Assert.That(chest.TryClaimReward(), Is.True);
            Assert.That(chest.TryClaimReward(), Is.False, "중복 보상 방지");
        }

        [Test]
        public void 개봉되지_않은_상자는_보상을_주지_않는다()
        {
            var chest = MakeChest();

            Assert.That(chest.IsRewardClaimable, Is.False);
            Assert.That(chest.TryClaimReward(), Is.False);

            chest.TryBeginOpen(Opener);
            Assert.That(chest.TryClaimReward(), Is.False, "개봉 중에는 아직 보상이 없다");
        }

        [Test]
        public void 재생성된_상자는_다시_보상을_준다()
        {
            var chest = OpenedChest();
            chest.TryClaimReward();
            chest.Respawn();

            Assert.That(chest.TryBeginOpen(Opener), Is.True);
            for (var i = 0; i < OpenTicks; i++)
            {
                chest.Tick();
            }

            Assert.That(chest.TryClaimReward(), Is.True, "새 생애에서는 보상이 다시 가능하다");
        }

        // ---------------------------------------------------------------------- 경쟁

        [Test]
        public void 이미_누군가_열고_있으면_다른_사람은_시작할_수_없다()
        {
            var chest = MakeChest();
            chest.TryBeginOpen(Opener);

            Assert.That(chest.TryBeginOpen(2), Is.False);
            Assert.That(chest.OpenerId, Is.EqualTo(Opener), "개봉자가 바뀌지 않는다");
        }

        [Test]
        public void 재생성_중에는_개봉할_수_없다()
        {
            var chest = OpenedChest();
            chest.TryClaimReward();

            Assert.That(chest.State, Is.EqualTo(ChestState.Respawning));
            Assert.That(chest.TryBeginOpen(Opener), Is.False);
        }
    }
}
