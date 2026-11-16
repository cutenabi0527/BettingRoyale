using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 같은 요청이 두 번 적용되는 것을 막는다 (Jira S15P21A606-26 완료 조건 1·2).
    /// 이름을 티켓 문장에 맞춰 붙여 체크박스와 1:1 로 대조된다.
    /// </summary>
    [TestFixture]
    public class EconomyRequestLedgerTests
    {
        private static EconomyRequestLedger OpenedLedger()
        {
            var ledger = new EconomyRequestLedger();
            ledger.OpenPreparation();
            return ledger;
        }

        [Test]
        public void 완료조건1_같은_요청을_두_번_보내면_두_번째는_처리되지_않는다()
        {
            var ledger = OpenedLedger();
            var ticket = new EconomyRequestTicket(ledger.Epoch, 1);

            Assert.That(ledger.Accept(playerId: 1, ticket), Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(ledger.Accept(playerId: 1, ticket), Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(ledger.Accept(playerId: 1, ticket), Is.EqualTo(EconomyRequestVerdict.Duplicate));
        }

        [Test]
        public void 다른_플레이어의_같은_순번은_서로_막지_않는다()
        {
            var ledger = OpenedLedger();
            var ticket = new EconomyRequestTicket(ledger.Epoch, 1);

            Assert.That(ledger.Accept(playerId: 1, ticket), Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(ledger.Accept(playerId: 2, ticket), Is.EqualTo(EconomyRequestVerdict.Fresh),
                "플레이어마다 순번이 따로다. 여기가 막히면 남의 요청이 내 것을 취소시킨다");
        }

        [Test]
        public void 지난_준비_단계의_요청은_순번을_보기_전에_걸린다()
        {
            var ledger = OpenedLedger();
            int oldEpoch = ledger.Epoch;

            ledger.OpenPreparation();

            Assert.That(
                ledger.Accept(playerId: 1, new EconomyRequestTicket(oldEpoch, 1)),
                Is.EqualTo(EconomyRequestVerdict.StaleEpoch));
        }

        [Test]
        public void 매치가_바뀌어도_준비_단계_번호는_되돌아가지_않는다()
        {
            var ledger = new EconomyRequestLedger();

            int first = ledger.OpenPreparation();
            int second = ledger.OpenPreparation();
            int third = ledger.OpenPreparation();

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
            Assert.That(third, Is.EqualTo(3),
                "되돌아가면 지난 매치의 요청이 이번 매치에서 통과한다");
        }

        [Test]
        public void 순번이_1_미만이면_형식_오류다()
        {
            var ledger = OpenedLedger();

            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 0)),
                Is.EqualTo(EconomyRequestVerdict.Malformed));
            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, -1)),
                Is.EqualTo(EconomyRequestVerdict.Malformed));
            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, int.MinValue)),
                Is.EqualTo(EconomyRequestVerdict.Malformed));
        }

        /// <summary>
        /// RPC 가 순서를 지키지 않으면 늦게 온 요청이 <b>버려지면 안 된다</b> —
        /// 사용자가 누른 재추첨 하나가 조용히 사라지는 것이다.
        /// </summary>
        [Test]
        public void 순서가_뒤바뀌어_늦게_온_요청도_한_번은_처리된다()
        {
            var ledger = OpenedLedger();

            Assert.That(ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 3)), Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(ledger.GapCountOf(1), Is.EqualTo(2), "1·2번이 구멍으로 남아야 한다");

            Assert.That(ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 1)), Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 2)), Is.EqualTo(EconomyRequestVerdict.Fresh));

            Assert.That(ledger.GapCountOf(1), Is.EqualTo(0));
            Assert.That(ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 2)), Is.EqualTo(EconomyRequestVerdict.Duplicate),
                "구멍을 메운 뒤 또 오면 중복이다");
        }

        /// <summary>
        /// 잠금 토글은 <b>횟수 제한이 없다.</b> 본 순번을 전부 기억하면 버튼 연타로
        /// Host(= 플레이어 PC) 메모리가 계속 는다. 워터마크 방식이 그것을 막는지 확인한다.
        /// </summary>
        [Test]
        public void 요청을_아무리_많이_보내도_기억하는_양이_늘지_않는다()
        {
            var ledger = OpenedLedger();

            for (int sequence = 1; sequence <= 200_000; sequence++)
            {
                Assert.That(
                    ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, sequence)),
                    Is.EqualTo(EconomyRequestVerdict.Fresh));
            }

            Assert.That(ledger.TrackedPlayerCount, Is.EqualTo(1));
            Assert.That(ledger.GapCountOf(1), Is.EqualTo(0), "빈틈없이 오면 구멍이 하나도 없어야 한다");
        }

        /// <summary>
        /// 구멍이 상한을 넘으면 오래된 것부터 버린다. <b>버려진 요청은 나중에 와도 무시된다</b> —
        /// 두 번 적용되는 것보다 한 번 사라지는 쪽이 안전하다.
        /// </summary>
        [Test]
        public void 구멍이_상한을_넘으면_오래된_것부터_버리고_두_번_적용하지_않는다()
        {
            var ledger = OpenedLedger();
            int far = EconomyRequestLedger.MaxTrackedGaps * 3;

            Assert.That(ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, far)), Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(ledger.GapCountOf(1), Is.EqualTo(EconomyRequestLedger.MaxTrackedGaps),
                "구멍이 상한에서 멈춰야 한다");

            // 버려진 오래된 순번은 이제 "이미 처리함" 으로 취급된다 — 사라질 뿐 두 번 적용되지 않는다.
            Assert.That(ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 1)), Is.EqualTo(EconomyRequestVerdict.Duplicate));
        }

        /// <summary>
        /// <b>회선에서 온 값을 반복 횟수로 쓰면 안 된다.</b>
        /// <c>Sequence</c> 는 클라이언트가 보낸 값이라 <c>int.MaxValue</c> 도 올 수 있다.
        /// 순진하게 워터마크부터 그 값까지 돌면 Host 가 20억 번을 돌고,
        /// Host 는 서버가 아니라 <b>플레이어 PC</b> 이므로(D-030) 그동안 게임이 멈춘다.
        ///
        /// <para>2026-09-02 코드 리뷰에서 지적된 실제 취약점이다.</para>
        /// </summary>
        [Test, Timeout(2000)]
        public void 비정상적으로_큰_순번이_와도_즉시_처리된다()
        {
            var ledger = OpenedLedger();

            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, int.MaxValue)),
                Is.EqualTo(EconomyRequestVerdict.Fresh));

            Assert.That(
                ledger.GapCountOf(1), Is.EqualTo(EconomyRequestLedger.MaxTrackedGaps),
                "구멍은 상한에서 멈춰야 한다");

            // 같은 값이 또 오면 중복이다 — 큰 순번이라고 판정이 무너지면 안 된다.
            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, int.MaxValue)),
                Is.EqualTo(EconomyRequestVerdict.Duplicate));

            // 바로 아래 순번은 구멍이므로 아직 한 번은 받는다.
            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, int.MaxValue - 1)),
                Is.EqualTo(EconomyRequestVerdict.Fresh));

            // 상한 밖의 옛 순번은 버려졌으므로 무시된다 — 두 번 적용되지는 않는다.
            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 1)),
                Is.EqualTo(EconomyRequestVerdict.Duplicate));
        }

        /// <summary>큰 순번을 여러 번 받아도 누적되지 않는다.</summary>
        [Test, Timeout(3000)]
        public void 큰_순번이_반복되어도_메모리가_늘지_않는다()
        {
            var ledger = OpenedLedger();

            for (int i = 0; i < 200; i++)
            {
                ledger.Accept(playerId: 1, new EconomyRequestTicket(ledger.Epoch, int.MaxValue - i));
            }

            Assert.That(ledger.GapCountOf(1), Is.LessThanOrEqualTo(EconomyRequestLedger.MaxTrackedGaps));
            Assert.That(ledger.TrackedPlayerCount, Is.EqualTo(1));
        }

        [Test]
        public void 준비_단계를_다시_열면_이전_원장을_버린다()
        {
            var ledger = OpenedLedger();
            ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 1));
            ledger.Accept(2, new EconomyRequestTicket(ledger.Epoch, 1));
            Assert.That(ledger.TrackedPlayerCount, Is.EqualTo(2));

            ledger.OpenPreparation();

            Assert.That(ledger.TrackedPlayerCount, Is.EqualTo(0), "매치가 길어져도 쌓이지 않아야 한다");
            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(ledger.Epoch, 1)),
                Is.EqualTo(EconomyRequestVerdict.Fresh),
                "새 단계에서는 순번이 1부터 다시 시작한다");
        }

        [Test]
        public void 준비_단계를_열기_전에는_모든_요청이_지난_것으로_걸린다()
        {
            var ledger = new EconomyRequestLedger();

            Assert.That(ledger.Epoch, Is.EqualTo(0), "0 은 '열린 적 없음' 이다");
            Assert.That(
                ledger.Accept(1, new EconomyRequestTicket(0, 1)),
                Is.EqualTo(EconomyRequestVerdict.StaleEpoch),
                "티켓의 Epoch 는 1부터다. 0 을 받아주면 준비 전 요청이 통과한다");
        }
    }
}
