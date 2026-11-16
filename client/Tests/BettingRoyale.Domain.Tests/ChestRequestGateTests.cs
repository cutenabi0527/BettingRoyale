using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 게이트 — 어댑터가 판단 없이 발행만 하면 되는지 (Jira S15P21A606-32).
    ///
    /// <para>
    /// 완료 조건 1·3 은 실제 2PC 없이는 실행으로 증명할 수 없다. 대신 여기서
    /// <b>화면이 받을 이벤트가 도메인에서 완성돼 나오는지</b>를 고정한다 — 어댑터에
    /// 판단이 남으면 그 판단이 Host 와 Client 에서 갈릴 수 있기 때문이다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ChestRequestGateTests
    {
        private const int Alice = 1;
        private const int Bob = 2;
        private const int OpenTicks = 5;
        private const int RespawnTicks = 3;
        /// <summary>재시도 지연. ⚠️ 검증용 임의값이다 — 확정값 1.0초는 데이터로 들어온다 (S-GD-5AZ6HN).</summary>
        private const int RetryTicks = 3;
        private const int Reward = 500;
        private const int TicksPerSecond = 60;

        private ChestField field;
        private MatchEconomyState economy;
        private ChestRequestGate gate;

        private static IReadOnlyList<WorldPosition> Anchors() => new[]
        {
            new WorldPosition(-7f, 0f, 8f), new WorldPosition(7f, 0f, 8f),
            new WorldPosition(-7f, 0f, -8f), new WorldPosition(7f, 0f, -8f),
        };

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
            var config = new ChestFieldConfig(
                new ChestConfig(OpenTicks, RespawnTicks, RetryTicks), Anchors(), chestCount: 2, reward: new ChestReward(Reward));

            field = new ChestField(config);
            field.BeginRound(new DeterministicRandom(2468));

            economy = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, Pool());
            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(Alice, WeaponTag.Sword),
                    new MatchParticipant(Bob, WeaponTag.Bow),
                },
                seed: 13579);
            economy.BeginPreparation();
            economy.EndPreparation();

            gate = new ChestRequestGate(field, economy, config, TicksPerSecond);
        }

        // ── 완료 조건 1: 화면이 받을 것이 도메인에서 완성된다 ────────────────

        [Test]
        public void 완료조건1_상자_목록에_위치와_진행도가_전부_실린다()
        {
            var r = gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            Assert.That(r.HasEvents, Is.True);
            Assert.That(r.Chests.Chests.Length, Is.EqualTo(2));
            Assert.That(r.Chests.TicksPerSecond, Is.EqualTo(TicksPerSecond),
                "화면이 이벤트 사이를 보간하려면 이 값이 필요하다 (규약 §8)");

            var opened = r.Chests.Chests[0];
            Assert.That(opened.State, Is.EqualTo(ChestState.Opening));
            Assert.That(opened.OpenDurationTicks, Is.EqualTo(OpenTicks));

            // 두 상자가 같은 자리면 화면엔 하나로 보인다 (완료 조건 4).
            var other = r.Chests.Chests[1];
            bool samePlace = opened.Position.X == other.Position.X
                && opened.Position.Y == other.Position.Y
                && opened.Position.Z == other.Position.Z;
            Assert.That(samePlace, Is.False, "두 상자가 같은 좌표에 있습니다");
        }

        [Test]
        public void 완료조건1_IsMine_은_내가_열고_있을_때만_참이다()
        {
            gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            var mine = gate.BuildChests(viewerId: Alice);
            var theirs = gate.BuildChests(viewerId: Bob);

            Assert.That(mine.Chests[0].IsMine, Is.True);
            Assert.That(theirs.Chests[0].IsMine, Is.False, "남이 여는 상자가 내 것으로 보이면 안 된다");
            Assert.That(mine.Chests[1].IsMine, Is.False, "아무도 안 여는 상자는 내 것이 아니다");
        }

        // ── 완료 조건 2: 취소 ────────────────────────────────────────────────

        [Test]
        public void 완료조건2_취소하면_사유가_담긴_이벤트가_나온다()
        {
            gate.HandleOpenBegin(Alice, 0, isMine: true);

            var r = gate.HandleOpenCancel(Alice, ChestCancelReason.Damaged, isMine: true);

            Assert.That(r.HasCancellation, Is.True);
            Assert.That(r.Cancellation.Reason, Is.EqualTo(ChestCancelReason.Damaged));
            Assert.That(r.Cancellation.OpenerId, Is.EqualTo(Alice));
            Assert.That(r.Chests.Chests[0].State, Is.EqualTo(ChestState.Idle));
        }

        /// <summary>
        /// 열고 있지 않은데 취소가 오는 것은 실패가 아니라 "아무 일도 안 일어남" 이다.
        /// 거부를 띄우면 화면이 없는 오류를 보여준다.
        /// </summary>
        [Test]
        public void 완료조건2_열고_있지_않을_때의_취소는_거부가_아니다()
        {
            var r = gate.HandleOpenCancel(Alice, ChestCancelReason.Moved, isMine: true);

            Assert.That(r.HasEvents, Is.True);
            Assert.That(r.HasCancellation, Is.False);
            Assert.That(r.HasRejection, Is.False);
        }

        // ── 완료 조건 3: 보상 한 번 + 골드 ───────────────────────────────────

        [Test]
        public void 완료조건3_개봉이_끝나면_보상과_골드가_함께_나온다()
        {
            int before = economy.GetGold(Alice);
            gate.HandleOpenBegin(Alice, 0, isMine: true);

            ChestRequestOutcome rewarded = default;
            for (int i = 0; i < OpenTicks + 2; i++)
            {
                var r = gate.Tick(viewerId: Alice, isMine: true);
                if (r.HasReward)
                {
                    rewarded = r;
                }
            }

            Assert.That(rewarded.HasReward, Is.True);
            Assert.That(rewarded.Reward.ClaimerId, Is.EqualTo(Alice));
            Assert.That(rewarded.HasGold, Is.True);
            Assert.That(rewarded.Gold.Delta, Is.EqualTo(Reward));
            Assert.That(rewarded.Gold.Gold, Is.EqualTo(before + Reward), "잔액은 현재 확정값이어야 한다");
            Assert.That(economy.GetGold(Alice), Is.EqualTo(before + Reward));
        }

        [Test]
        public void 완료조건3_보상_골드는_한_번만_들어온다()
        {
            int before = economy.GetGold(Alice);
            gate.HandleOpenBegin(Alice, 0, isMine: true);

            for (int i = 0; i < OpenTicks + RespawnTicks + 5; i++)
            {
                gate.Tick(viewerId: Alice, isMine: true);
            }

            Assert.That(economy.GetGold(Alice), Is.EqualTo(before + Reward),
                "틱을 더 돌린다고 골드가 또 들어오면 안 된다");
        }

        /// <summary>
        /// 남이 먹어도 그 사람 골드는 실제로 늘어야 한다. 다만 내 화면에는 골드 이벤트가 오지 않는다.
        /// </summary>
        [Test]
        public void 완료조건3_남이_먹은_보상도_그_사람_골드에_들어간다()
        {
            int bobBefore = economy.GetGold(Bob);
            gate.HandleOpenBegin(Bob, 0, isMine: false);

            ChestRequestOutcome seen = default;
            for (int i = 0; i < OpenTicks + 2; i++)
            {
                var r = gate.Tick(viewerId: Alice, isMine: true);
                if (r.HasReward)
                {
                    seen = r;
                }
            }

            Assert.That(economy.GetGold(Bob), Is.EqualTo(bobBefore + Reward));
            Assert.That(seen.HasReward, Is.True, "누가 먹었는지는 전원이 안다 (전체 알림)");
            Assert.That(seen.HasGold, Is.False, "내 골드는 안 변했으므로 내 화면에 골드 이벤트가 오면 안 된다");
        }

        [Test]
        public void 완료조건3_이미_열고_있는_상자에는_거부_사유가_온다()
        {
            gate.HandleOpenBegin(Alice, 0, isMine: true);

            var r = gate.HandleOpenBegin(Bob, 0, isMine: true);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ChestRejectReason.NotIdle));
            Assert.That(r.Rejection.ChestId, Is.EqualTo(0));
        }

        [Test]
        public void 없는_상자_번호에는_그런_상자가_없다는_사유가_온다()
        {
            var r = gate.HandleOpenBegin(Alice, chestId: 99, isMine: true);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ChestRejectReason.NoSuchChest));
        }

        // ── 경계 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 참가자가 아니면 아무것도 하지 않는다. <c>EconomyRequestGate</c> 와 같은 가드다.
        ///
        /// <para>
        /// ⚠️ <b>음수 id 는 특히 위험하다.</b> <c>TreasureChest.TryBeginOpen</c> 이 <c>false</c> 가 아니라
        /// <b>예외를 던지고</b>, 그 예외가 EventBus 핸들러에서 삼켜져 화면이 무반응이 된다.
        /// 2026-09-02 사전 조사에서 발견한 구멍이다.
        /// </para>
        /// </summary>
        [Test]
        public void 참가자가_아니면_아무것도_발행하지_않는다()
        {
            Assert.That(gate.HandleOpenBegin(playerId: 999, chestId: 0, isMine: false).HasEvents, Is.False);
            Assert.That(gate.HandleOpenCancel(999, ChestCancelReason.Moved, isMine: false).HasEvents, Is.False);
            Assert.That(gate.Tick(viewerId: 999, isMine: false).HasEvents, Is.False);
        }

        [Test]
        public void 음수_id_는_예외가_아니라_거부다()
        {
            Assert.That(
                gate.HandleOpenBegin(playerId: -1, chestId: 0, isMine: false).HasEvents, Is.False,
                "TreasureChest 가 음수에 예외를 던지므로 게이트에서 먼저 걸러야 한다");

            Assert.That(gate.HandleOpenBegin(int.MinValue, 0, isMine: false).HasEvents, Is.False);
        }

        [Test]
        public void 참가자가_아닌_사람은_남의_개봉을_취소할_수_없다()
        {
            gate.HandleOpenBegin(Alice, 0, isMine: true);

            gate.HandleOpenCancel(playerId: 999, ChestCancelReason.Moved, isMine: false);

            Assert.That(
                gate.BuildChests(Alice).Chests[0].State, Is.EqualTo(ChestState.Opening),
                "모르는 id 로 남의 개봉이 끊기면 안 된다");
        }

        [Test]
        public void 라운드가_닫히면_아무것도_발행하지_않는다()
        {
            field.EndRound();

            Assert.That(gate.HandleOpenBegin(Alice, 0, isMine: true).HasEvents, Is.False);
            Assert.That(gate.Tick(Alice, isMine: true).HasEvents, Is.False);
            Assert.That(gate.HandleOpenCancel(Alice, ChestCancelReason.Moved, isMine: true).HasEvents, Is.False);
        }

        // ── 한 사람에 한 상자 (S-GD-PTVC2X) ──────────────────────────────────

        /// <summary>
        /// <b>대상 상자는 비어 있는데 거부된다.</b> 그래서 <c>NotIdle</c> 을 쓰면 안 된다 —
        /// 화면이 아무도 안 연 상자 위에 "이미 누가 열고 있습니다" 를 띄우게 된다.
        /// 사유를 새로 만든 이유가 이것뿐이라, 대상 상자가 <c>Idle</c> 인 것을 함께 잠근다.
        /// </summary>
        [Test]
        public void 다른_상자를_열고_있으면_전용_사유로_거부된다()
        {
            gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            var r = gate.HandleOpenBegin(Alice, chestId: 1, isMine: true);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ChestRejectReason.AlreadyOpeningAnother));
            Assert.That(r.Rejection.ChestId, Is.EqualTo(1), "거부된 요청의 상자 번호를 그대로 싣는다");
            Assert.That(r.Chests.Chests[1].State, Is.EqualTo(ChestState.Idle),
                "1번은 손도 안 탔다 — NotIdle 이 거짓말인 이유가 바로 이것이다");
        }

        /// <summary>
        /// <b>같은 상자를 다시 요청하는 것은 두 번째 상자가 아니라 재전송이다.</b>
        /// 가드가 요청한 상자를 검사에서 빼는 설계 결정을 고정한다 — 이 테스트가 없으면
        /// 다음 사람이 가드를 "모든 상자" 로 넓혀도 아무것도 안 깨진다.
        /// </summary>
        [Test]
        public void 같은_상자를_다시_요청하면_여전히_NotIdle_이다()
        {
            gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            var resend = gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            Assert.That(resend.Rejection.Reason, Is.EqualTo(ChestRejectReason.NotIdle),
                "재전송은 AlreadyOpeningAnother 가 아니다");
        }

        [Test]
        public void 남이_열고_있는_상자는_여전히_NotIdle_이다()
        {
            gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            var theirs = gate.HandleOpenBegin(Bob, chestId: 0, isMine: false);

            Assert.That(theirs.Rejection.Reason, Is.EqualTo(ChestRejectReason.NotIdle),
                "Bob 은 다른 상자를 열고 있지 않다 — 상자가 잡혀 있는 것이 이유다");
        }

        // ── 재시도 지연 (S-GD-5AZ6HN) ────────────────────────────────────────

        /// <summary>
        /// 취소당한 직후의 거부는 <b>전용 사유</b>로 온다. 여기서도 대상 상자는 <c>Idle</c> 이라
        /// <c>NotIdle</c> 은 거짓말이고, 플레이어가 할 일도 다르다 — "잠깐 기다려라" 다.
        /// </summary>
        [Test]
        public void 취소당한_직후의_재시도는_전용_사유로_거부된다()
        {
            gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);
            gate.Tick(viewerId: Alice, isMine: true);
            gate.HandleOpenCancel(Alice, ChestCancelReason.Damaged, isMine: true);

            var r = gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ChestRejectReason.RetryDelayed));
            Assert.That(r.Chests.Chests[0].State, Is.EqualTo(ChestState.Idle),
                "상자는 비어 있다 — NotIdle 이 거짓말인 이유가 여기서도 같다");
        }

        /// <summary>
        /// 라운드 막바지 거부는 <b>기다린다고 풀리지 않는다.</b> 다른 사유는 전부 "잠시 뒤 다시" 인데
        /// 이것만 "이번 라운드는 끝" 이라, 화면 문구가 갈려야 한다 (S-GD-DFG3VR).
        /// </summary>
        [Test]
        public void 라운드_막바지에는_전용_사유로_거부된다()
        {
            field.StopAcceptingNewOpens();

            var r = gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ChestRejectReason.RoundEnding));
        }

        /// <summary>
        /// 쿨다운 중에 <b>다른</b> 상자를 요청하면 통과한다 — 지연은 그 상자에만 걸린다.
        /// </summary>
        [Test]
        public void 쿨다운_중에도_다른_상자는_열린다()
        {
            gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);
            gate.Tick(viewerId: Alice, isMine: true);
            gate.HandleOpenCancel(Alice, ChestCancelReason.Moved, isMine: true);

            var other = gate.HandleOpenBegin(Alice, chestId: 1, isMine: true);

            Assert.That(other.HasRejection, Is.False, "1번은 지연 대상이 아니다");
            Assert.That(other.Chests.Chests[1].State, Is.EqualTo(ChestState.Opening));
        }
    }
}
