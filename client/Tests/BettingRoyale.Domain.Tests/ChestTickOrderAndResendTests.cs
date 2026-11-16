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
    /// 상자 — <b>호출 순서</b>가 결과를 가르는 경계 (Jira S15P21A606-33).
    ///
    /// <para>
    /// 같은 -33 의 나머지 절반은 <c>ChestCompetitionAndCarryOverTests</c> 에 있다.
    /// 그쪽은 <b>한 시점</b>의 경쟁·한도를 본다 — 두 사람이 같은 상자를 열 때, 이월 한도가 0일 때.
    /// 여기는 <b>두 사건이 같은 프레임에 겹칠 때</b>를 본다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>여기 있는 대부분은 "옳은 동작" 이 아니라 "지금 이렇게 동작한다" 를 고정한다.</b>
    /// <b>이동·피격</b> 취소와 완료 틱 사이의 우선순위는 <b>아직 정해지지 않았다</b> — 어댑터가
    /// <c>Tick()</c> 을 먼저 부르느냐 <c>CancelOpenBy()</c> 를 먼저 부르느냐라는 <b>우연</b>이
    /// 결정한다. 규칙이 생기면 그 단언이 먼저 빨개져야 한다.
    /// </para>
    ///
    /// <para>
    /// <b>단 라운드 종료만은 규칙이 생겼다</b> (S-GD-DFG3VR, 조영운 2026-09-04 확정) —
    /// 개봉 완료와 라운드 종료가 같은 순간이면 <b>라운드 종료가 이긴다.</b>
    /// 마지막 1인은 이미 그 라운드의 1등이라 상자 보상까지 얹으면 다음 라운드가 더 기울어진다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>도메인은 그 규칙을 스스로 지킬 수 없다.</b> 상자 필드는 라운드가 끝난다는 것을
    /// 미리 모른다. 그래서 <b>어댑터 규약</b>으로 남았다 —
    /// <i>"한 프레임에서 라운드가 끝나면 <c>EndRound()</c> 를 <c>Tick()</c> 보다 먼저 부른다."</i>
    /// 아래 <see cref="라운드_종료도_완료_틱과의_순서로_결과가_갈린다"/> 가 그 규약을 어겼을 때
    /// 무엇을 잃는지 보여준다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ChestTickOrderAndResendTests
    {
        private const int Alice = 1;
        private const int Bob = 2;
        private const int OpenTicks = 5;
        private const int RespawnTicks = 3;
        /// <summary>재시도 지연. ⚠️ 검증용 임의값이다 — 확정값 1.0초는 데이터로 들어온다 (S-GD-5AZ6HN).</summary>
        private const int RetryTicks = 3;
        private const int Reward = 500;
        private const int TicksPerSecond = 60;

        private static IReadOnlyList<WorldPosition> Anchors() => new[]
        {
            new WorldPosition(-7f, 0f, 8f), new WorldPosition(7f, 0f, 8f),
            new WorldPosition(-7f, 0f, -8f), new WorldPosition(7f, 0f, -8f),
        };

        /// <summary>
        /// 등급당 <b>12개</b>다. 3라운드 × 2명을 도는 테스트가 있어서 6개로는 모자란다 —
        /// 획득한 축복은 다시 뜨지 않으므로(S-GD-VF67AG) 후보가 모자라면 도메인이 예외를 던진다.
        /// </summary>
        private static IReadOnlyList<BlessingDefinition> Pool()
        {
            var pool = new List<BlessingDefinition>();
            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (int i = 0; i < 12; i++)
                {
                    pool.Add(new BlessingDefinition($"{tier}-{i}", $"{tier} {i}", WeaponTag.Common, tier));
                }
            }

            return pool;
        }

        private static ChestFieldConfig Config() => new ChestFieldConfig(
            new ChestConfig(OpenTicks, RespawnTicks, RetryTicks), Anchors(), chestCount: 2,
            reward: new ChestReward(Reward));

        private static ChestField OpenedField(int seed = 5150)
        {
            var field = new ChestField(Config());
            field.BeginRound(new DeterministicRandom(seed));
            return field;
        }

        private static MatchEconomyState NewEconomy(params int[] playerIds)
        {
            var economy = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, Pool());
            var participants = new MatchParticipant[playerIds.Length];
            for (int i = 0; i < playerIds.Length; i++)
            {
                participants[i] = new MatchParticipant(playerIds[i], WeaponTag.Sword);
            }

            economy.BeginMatch(participants, seed: 24680);
            economy.BeginPreparation();
            economy.EndPreparation();
            return economy;
        }

        /// <summary>개봉이 <b>완료되기 직전</b>까지만 틱을 돌린다. 다음 <c>Tick()</c> 이 완료 틱이다.</summary>
        private static ChestField FieldOneTickBeforeReward(int chestId = 0, int opener = Alice)
        {
            var field = OpenedField();
            Assert.That(field.TryBeginOpen(chestId, opener), Is.True, "전제: 개봉이 시작돼야 한다");

            for (int i = 0; i < OpenTicks - 1; i++)
            {
                var earned = field.Tick();
                Assert.That(earned.Count, Is.EqualTo(0), $"아직 완료 전인데 {i + 1}틱에서 보상이 나왔다");
            }

            return field;
        }

        // ── 경계 ① 완료 틱과 취소가 같은 프레임에 겹칠 때 ──────────────────

        /// <summary>
        /// <b>Tick 이 먼저면 보상이 나가고, 뒤이은 취소는 아무 일도 못 한다.</b>
        /// <c>ChestField.Tick()</c> 한 호출 안에서 Opening → Rewarded → 수령 → Respawning 이
        /// 전부 끝나기 때문이다. 취소는 <c>Opening</c> 인 상자만 건드린다.
        /// </summary>
        [Test]
        public void 완료_틱을_먼저_돌리면_보상이_나가고_뒤이은_취소는_아무_일도_못_한다()
        {
            var field = FieldOneTickBeforeReward();

            var earned = field.Tick();
            Assert.That(earned.Count, Is.EqualTo(1), "완료 틱이라 보상이 나와야 한다");
            Assert.That(earned[0].ClaimerId, Is.EqualTo(Alice));
            Assert.That(earned[0].Gold, Is.EqualTo(Reward));

            var cancelled = field.CancelOpenBy(Alice, ChestCancelReason.Damaged);

            Assert.That(cancelled.Count, Is.EqualTo(0),
                "이미 Opening 이 아니다 — 같은 프레임의 피격이라도 보상을 되돌리지 못한다");
        }

        /// <summary>
        /// <b>취소가 먼저면 그 틱의 보상이 통째로 사라진다.</b> 앞 테스트와 입력이 같은데
        /// 순서만 다르다 — 그런데도 결과가 500G 대 0G 로 갈린다.
        ///
        /// <para>
        /// ⚠️ <b>이것이 옳다고 주장하는 테스트가 아니다.</b> S-GD-GQNA2B 에 우선순위 규칙이 없어
        /// 지금은 어댑터의 호출 순서가 결정한다. 규칙이 정해지면 이 단언을 먼저 고쳐야 한다.
        /// </para>
        /// </summary>
        [Test]
        public void 취소를_먼저_처리하면_같은_틱의_보상이_사라진다()
        {
            var field = FieldOneTickBeforeReward();

            var cancelled = field.CancelOpenBy(Alice, ChestCancelReason.Damaged);
            Assert.That(cancelled.Count, Is.EqualTo(1), "아직 Opening 이라 취소된다");
            Assert.That(cancelled[0].Reason, Is.EqualTo(ChestCancelReason.Damaged));

            var earned = field.Tick();

            Assert.That(earned.Count, Is.EqualTo(0),
                "같은 프레임인데 취소가 먼저라 보상이 0 이다 — 호출 순서가 500G 를 가른다");
        }

        /// <summary>라운드 종료도 같은 갈림을 갖는다. <c>EndRound</c> 역시 <c>Opening</c> 만 끊는다.</summary>
        [Test]
        public void 라운드_종료도_완료_틱과의_순서로_결과가_갈린다()
        {
            // ❌ 규약 위반 — 어댑터가 Tick 을 먼저 부르면 마지막 1인이 보상까지 가져간다.
            var tickFirst = FieldOneTickBeforeReward();
            var earnedBeforeEnd = tickFirst.Tick();
            var cancelledAfterTick = tickFirst.EndRound();

            Assert.That(earnedBeforeEnd.Count, Is.EqualTo(1),
                "Tick 이 먼저면 보상이 나간다 — S-GD-DFG3VR 이 막으려는 결과가 바로 이것이다");
            Assert.That(cancelledAfterTick.Count, Is.EqualTo(0), "끊을 개봉이 남아 있지 않다");

            // ✅ 규약대로 — EndRound 를 먼저 부르면 라운드 종료가 이긴다.
            var endFirst = FieldOneTickBeforeReward();
            var cancelledByEnd = endFirst.EndRound();

            Assert.That(cancelledByEnd.Count, Is.EqualTo(1), "EndRound 가 먼저면 개봉이 끊긴다");
            Assert.That(cancelledByEnd[0].Reason, Is.EqualTo(ChestCancelReason.RoundEnded),
                "개봉자의 잘못이 아니므로 사유가 구분돼야 한다");
        }

        /// <summary>
        /// <b>규약대로 부르면 마지막 1인이 상자 보상을 가져가지 못한다</b> (S-GD-DFG3VR).
        ///
        /// <para>
        /// 위 테스트가 두 순서의 <i>차이</i>를 보인다면, 이것은 <b>규칙이 요구하는 결과</b>를
        /// 직접 못박는다. 어댑터가 규약을 어기면 위 테스트는 그대로 통과하지만
        /// 이 이름이 거짓이 된다 — 그래서 규칙 이름으로 남긴다.
        /// </para>
        /// </summary>
        [Test]
        public void 규약대로면_라운드_종료가_개봉_완료를_이긴다()
        {
            var field = FieldOneTickBeforeReward();

            var cancelled = field.EndRound();

            Assert.That(cancelled.Count, Is.EqualTo(1), "완료 직전이던 개봉이 끊긴다");
            Assert.That(cancelled[0].OpenerId, Is.EqualTo(Alice));
            Assert.That(cancelled[0].Reason, Is.EqualTo(ChestCancelReason.RoundEnded));
            Assert.That(field.Count, Is.EqualTo(0), "라운드가 닫혔으므로 상자가 남지 않는다");
        }

        /// <summary>
        /// 같은 갈림이 <b>어댑터가 실제로 보는 표면</b>(<see cref="ChestRequestGate"/>)에서도
        /// 그대로 나타난다. 도메인 안쪽 이야기가 아니라 화면에 가는 이벤트와 잔액이 갈린다.
        /// </summary>
        [Test]
        public void 게이트에서도_틱과_취소의_순서가_보상_지급을_가른다()
        {
            // Tick 이 먼저.
            var fieldA = FieldOneTickBeforeReward();
            var economyA = NewEconomy(Alice, Bob);
            var gateA = new ChestRequestGate(fieldA, economyA, Config(), TicksPerSecond);
            int goldBefore = economyA.GetGold(Alice);

            var tickFirst = gateA.Tick(viewerId: Alice, isMine: true);
            gateA.HandleOpenCancel(Alice, ChestCancelReason.Damaged, isMine: true);

            Assert.That(tickFirst.HasReward, Is.True, "Tick 이 먼저면 보상 이벤트가 나간다");
            Assert.That(tickFirst.HasGold, Is.True);
            Assert.That(tickFirst.Gold.Delta, Is.EqualTo(Reward));
            Assert.That(economyA.GetGold(Alice), Is.EqualTo(goldBefore + Reward));

            // 취소가 먼저.
            var fieldB = FieldOneTickBeforeReward();
            var economyB = NewEconomy(Alice, Bob);
            var gateB = new ChestRequestGate(fieldB, economyB, Config(), TicksPerSecond);
            int goldBeforeB = economyB.GetGold(Alice);

            var cancelFirst = gateB.HandleOpenCancel(Alice, ChestCancelReason.Damaged, isMine: true);
            var tickAfter = gateB.Tick(viewerId: Alice, isMine: true);

            Assert.That(cancelFirst.HasCancellation, Is.True, "취소가 먼저면 취소 이벤트가 나간다");
            Assert.That(tickAfter.HasReward, Is.False, "그 뒤 Tick 은 보상을 만들지 못한다");
            Assert.That(economyB.GetGold(Alice), Is.EqualTo(goldBeforeB),
                "같은 입력·다른 순서인데 잔액이 갈린다");
        }

        // ── 경계 ② 같은 사람이 같은 개봉 요청을 재전송할 때 ────────────────

        /// <summary>
        /// <b>개봉자 본인의 재전송에도 거부가 나간다.</b> 경제 게이트는 정반대다 —
        /// <c>EconomyRequestGateTests.중복_요청에는_거부를_띄우지_않는다</c> 가
        /// *"사용자의 클릭은 이미 한 번 성공했다. 거부를 띄우면 거짓말이 된다"* 로 못박았다.
        ///
        /// <para>
        /// 두 게이트의 원칙이 다른 이유는 <b>입력이 다르기 때문</b>이다. 경제에는 요청 번호
        /// (<c>EconomyRequestTicket</c>)와 장부가 있어 재전송과 진짜 두 번째 클릭을 구분할 수 있다.
        /// 상자 게이트에는 그것이 없어 <b>구분할 방법 자체가 없다.</b>
        /// 통일할지는 사람이 정할 일이고, 정해지면 이 단언이 먼저 빨개진다.
        /// </para>
        /// </summary>
        [Test]
        public void 같은_사람이_같은_개봉_요청을_재전송하면_자기_자신에게_거부가_간다()
        {
            var field = OpenedField();
            var economy = NewEconomy(Alice, Bob);
            var gate = new ChestRequestGate(field, economy, Config(), TicksPerSecond);

            var first = gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);
            Assert.That(first.HasRejection, Is.False, "첫 요청은 성공한다");

            var resend = gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            Assert.That(resend.HasRejection, Is.True,
                "상자 게이트에는 요청 번호가 없어 재전송과 두 번째 클릭을 구분하지 못한다");
            Assert.That(resend.Rejection.Reason, Is.EqualTo(ChestRejectReason.NotIdle));
            Assert.That(resend.Rejection.IsMine, Is.True,
                "내 화면에 '이미 누가 열고 있습니다' 가 뜬다 — 그 '누가' 가 나 자신이다");
        }

        /// <summary>
        /// 재전송이 <b>내 개봉 진행도를 되돌리지 않는다.</b> 이것은 현재 동작 고정이 아니라
        /// <b>옳은 동작 요구</b>다 — 되돌아가면 회선이 나쁜 사람은 개봉을 영원히 못 끝낸다.
        /// </summary>
        [Test]
        public void 재전송이_내_개봉_진행도를_처음으로_되돌리지_않는다()
        {
            var field = OpenedField();
            Assert.That(field.TryBeginOpen(chestId: 0, Alice), Is.True);
            field.Tick();
            field.Tick();

            var before = field.BuildPlacements()[0];
            Assert.That(before.ElapsedTicks, Is.EqualTo(2), "전제: 2틱 진행돼 있어야 한다");

            field.TryBeginOpen(chestId: 0, Alice);
            var after = field.BuildPlacements()[0];

            Assert.That(after.ElapsedTicks, Is.EqualTo(2), "재전송이 진행도를 지우면 개봉을 끝낼 수 없다");
            Assert.That(after.OpenerId, Is.EqualTo(Alice));
            Assert.That(after.State, Is.EqualTo(ChestState.Opening));
        }

        /// <summary>
        /// <b>내 재전송과 남에게 뺏긴 것이 사유로 구분되지 않는다.</b> 둘 다 <c>NotIdle</c> 이고
        /// 지금 그 둘을 가르는 것은 <c>IsMine</c> 뿐이다. 사유를 늘리거나 요청 번호를 도입하면
        /// 여기가 먼저 빨개진다.
        /// </summary>
        [Test]
        public void 내_재전송_거부와_남에게_뺏긴_거부가_사유로_구분되지_않는다()
        {
            var field = OpenedField();
            var economy = NewEconomy(Alice, Bob);
            var gate = new ChestRequestGate(field, economy, Config(), TicksPerSecond);

            gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);

            var mine = gate.HandleOpenBegin(Alice, chestId: 0, isMine: true);
            var theirs = gate.HandleOpenBegin(Bob, chestId: 0, isMine: false);

            Assert.That(mine.Rejection.Reason, Is.EqualTo(theirs.Rejection.Reason),
                "'내가 이미 열고 있다' 와 '남이 열고 있다' 가 같은 사유로 온다");
            Assert.That(mine.Rejection.IsMine, Is.True);
            Assert.That(theirs.Rejection.IsMine, Is.False);
        }

        // ── 경계 ③ 상자 보상 지급과 라운드 정산이 같은 틱일 때 ─────────────

        /// <summary>
        /// <b>보상과 정산의 순서가 이월액을 바꾸지 않는다.</b> 이것은 옳은 동작 요구다.
        /// <c>TryApplyRoundResults</c> 가 지갑을 한 번도 읽지 않고, 이월 마감은 다음
        /// <c>BeginPreparation</c> 이 하기 때문에 성립한다. 정산이 지갑을 읽기 시작하면
        /// 여기가 먼저 빨개진다.
        /// </summary>
        [Test]
        public void 보상_지급과_라운드_정산의_순서가_이월액을_바꾸지_않는다()
        {
            int earnFirst = RunRound(earnBeforeSettle: true);
            int settleFirst = RunRound(earnBeforeSettle: false);

            Assert.That(earnFirst, Is.EqualTo(settleFirst),
                "정산은 지갑을 읽지 않는다 — 순서가 달라도 다음 라운드 잔액이 같아야 한다");

            static int RunRound(bool earnBeforeSettle)
            {
                var economy = NewEconomy(Alice);
                var results = new[] { new RoundOutcome(Alice, kills: 0, assists: 0, survived: true) };

                if (earnBeforeSettle)
                {
                    economy.Earn(Alice, Reward);
                    economy.TryApplyRoundResults(round: 1, results);
                }
                else
                {
                    economy.TryApplyRoundResults(round: 1, results);
                    economy.Earn(Alice, Reward);
                }

                economy.BeginPreparation();
                return economy.GetGold(Alice);
            }
        }

        /// <summary>
        /// <b>정산 뒤·다음 준비 전에 들어온 보상도 이월된다.</b> 마감이 정산이 아니라 다음
        /// <c>BeginPreparation</c> 이라서 생기는 틈이다 — 전투 종료 연출 중에 끝난 개봉이 여기 해당한다.
        /// 스펙이 없으므로 현재 동작을 고정한다.
        /// </summary>
        [Test]
        public void 정산_뒤_준비_전에_들어온_상자_보상도_다음_라운드로_이월된다()
        {
            var economy = NewEconomy(Alice);
            economy.TryApplyRoundResults(
                round: 1, new[] { new RoundOutcome(Alice, kills: 0, assists: 0, survived: true) });

            int beforeLateReward = economy.GetGold(Alice);
            economy.Earn(Alice, Reward);
            economy.BeginPreparation();

            Assert.That(economy.GetGold(Alice), Is.GreaterThanOrEqualTo(beforeLateReward + Reward),
                "정산과 준비 사이에 들어온 골드도 살아남는다 — 마감은 BeginPreparation 이 한다");
        }

        /// <summary>
        /// ⚠️ <b>매치가 끝난 뒤에 들어온 상자 골드가 최종 등수를 뒤집는다.</b>
        /// <c>MatchEconomyState.Earn</c> 에 국면 가드가 없어 <c>Finished</c> 이후에도 골드가 들어가고,
        /// 그 값이 동점 4단계(남은 골드)의 입력이기 때문이다.
        ///
        /// <para>
        /// <b>현재 동작 고정이다.</b> 막기로 하면 <c>Earn</c> 에 국면 가드를 넣어야 하고,
        /// 그러면 이 단언이 먼저 빨개진다.
        /// </para>
        /// </summary>
        [Test]
        public void 매치가_끝난_뒤_들어온_상자_보상이_동점_등수를_뒤집는다()
        {
            var economy = NewEconomy(Alice, Bob);
            economy.TryApplyRoundResults(round: 1, new[]
            {
                new RoundOutcome(Alice, kills: 0, assists: 0, rangedAssists: 0, survived: true, placement: 1),
                new RoundOutcome(Bob, kills: 0, assists: 0, rangedAssists: 0, survived: true, placement: 1),
            });

            var beforeLateReward = economy.BuildFinalStandings();
            Assert.That(beforeLateReward[0].PlayerId, Is.EqualTo(Alice),
                "전제: 완전 동점이라 임시 처리(PlayerId 오름차순)로 Alice 가 앞선다");

            economy.Earn(Bob, Reward);
            var afterLateReward = economy.BuildFinalStandings();

            Assert.That(afterLateReward[0].PlayerId, Is.EqualTo(Bob),
                "매치가 끝난 뒤 들어온 골드가 동점 4단계를 갈라 1등을 바꾼다");
        }
    }
}
