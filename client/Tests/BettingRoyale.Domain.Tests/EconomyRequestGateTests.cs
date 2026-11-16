using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// Host 가 받은 준비 단계 요청의 중복 방지 (Jira S15P21A606-26 완료 조건 1·2).
    ///
    /// 이름을 티켓 문장에 맞춰 붙여 체크박스와 1:1 로 대조된다.
    /// </summary>
    [TestFixture]
    public class EconomyRequestGateTests
    {
        private const int Me = 1;
        private const int Other = 2;

        private MatchEconomyState economy;
        private EconomyRequestLedger ledger;
        private EconomyRequestGate gate;

        private static BlessingDefinition Common(string id, BlessingTier tier) =>
            new BlessingDefinition(id, id, WeaponTag.Common, tier);

        /// <summary>
        /// 등급마다 넉넉히 채운다. <b>등급은 라운드가 정하므로</b>(S-GD-HE87JY)
        /// 한 등급만 넣으면 2라운드에서 후보를 못 채워 도메인이 예외를 던진다.
        /// </summary>
        private static IReadOnlyList<BlessingDefinition> Pool()
        {
            var pool = new List<BlessingDefinition>();

            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (int i = 0; i < 12; i++)
                {
                    pool.Add(Common($"{tier}-{i}", tier));
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
                    new MatchParticipant(Me, WeaponTag.Sword),
                    new MatchParticipant(Other, WeaponTag.Bow),
                },
                seed: 12345);

            economy.BeginPreparation();

            ledger = new EconomyRequestLedger();
            gate = new EconomyRequestGate(economy, ledger);
            gate.OpenPreparation();
        }

        private EconomyRequestTicket Ticket(int sequence) => new EconomyRequestTicket(gate.Epoch, sequence);

        // ------------------------------------------------------------ 완료 조건 1

        [Test]
        public void 완료조건1_같은_재추첨_요청을_두_번_보내도_골드는_한_번만_차감된다()
        {
            int start = economy.GetGold(Me);
            var ticket = Ticket(1);

            var first = gate.HandleReroll(Me, ticket, isMine: true);
            int afterFirst = economy.GetGold(Me);

            var second = gate.HandleReroll(Me, ticket, isMine: true);
            int afterSecond = economy.GetGold(Me);

            Assert.That(first.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(second.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(afterFirst, Is.LessThan(start), "첫 요청은 골드를 써야 한다");
            Assert.That(afterSecond, Is.EqualTo(afterFirst), "두 번째 요청에 골드가 또 깎이면 안 된다");
        }

        [Test]
        public void 중복_요청에는_거부를_띄우지_않는다()
        {
            var ticket = Ticket(1);
            gate.HandleReroll(Me, ticket, isMine: true);

            var replay = gate.HandleReroll(Me, ticket, isMine: true);

            Assert.That(replay.HasRejection, Is.False,
                "사용자의 클릭은 이미 한 번 성공했다. 거부를 띄우면 거짓말이 된다");
        }

        [Test]
        public void 중복_요청에도_화면이_받을_이벤트는_온다()
        {
            var ticket = Ticket(1);
            gate.HandleReroll(Me, ticket, isMine: true);

            var replay = gate.HandleReroll(Me, ticket, isMine: true);

            Assert.That(replay.HasEvents, Is.True, "침묵은 성공이 아니다 (규약 §6)");
            Assert.That(replay.Gold.Delta, Is.EqualTo(0), "적용되지 않았으므로 연출용 델타는 0 이다");
            Assert.That(replay.Gold.Gold, Is.EqualTo(economy.GetGold(Me)), "잔액은 현재 확정값이어야 한다");
            Assert.That(replay.Offer.PlayerId, Is.EqualTo(Me));
        }

        // ------------------------------------------------------------ 완료 조건 2

        [Test]
        public void 완료조건2_같은_선택_요청을_두_번_보내도_축복은_하나만_얻는다()
        {
            var ticket = Ticket(1);

            var first = gate.HandleSelect(Me, 0, ticket, isMine: true);
            var second = gate.HandleSelect(Me, 0, ticket, isMine: true);

            Assert.That(first.HasSelected, Is.True);
            Assert.That(second.HasSelected, Is.False);
            Assert.That(economy.GetAcquired(Me).Count, Is.EqualTo(1), "두 번 눌러 두 개를 얻으면 안 된다");
        }

        [Test]
        public void 중복_선택에_이미_선택했다는_거부가_뜨지_않는다()
        {
            var ticket = Ticket(1);
            gate.HandleSelect(Me, 0, ticket, isMine: true);

            var replay = gate.HandleSelect(Me, 0, ticket, isMine: true);

            Assert.That(replay.HasRejection, Is.False,
                "한 번뿐인 클릭이 성공했는데 '이미 선택했습니다' 가 뜨면 사용자가 혼란스럽다");
        }

        [Test]
        public void 서로_다른_순번의_선택은_두_번째가_진짜_거부된다()
        {
            gate.HandleSelect(Me, 0, Ticket(1), isMine: true);

            // 새 요청이다 — 중복이 아니라 실제로 "이미 선택함" 이다.
            var genuine = gate.HandleSelect(Me, 1, Ticket(2), isMine: true);

            Assert.That(genuine.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(genuine.HasRejection, Is.True);
            Assert.That(genuine.Rejection.Reason, Is.EqualTo(BlessingSelectRejectReason.AlreadySelected));
        }

        // ------------------------------------------------------------ 잠금

        [Test]
        public void 같은_잠금_요청을_두_번_보내도_한_번만_토글된다()
        {
            var ticket = Ticket(1);

            gate.HandleLockToggle(Me, 0, ticket, isMine: true);
            bool lockedAfterFirst = economy.GetCandidates(Me)[0].IsLocked;

            gate.HandleLockToggle(Me, 0, ticket, isMine: true);
            bool lockedAfterSecond = economy.GetCandidates(Me)[0].IsLocked;

            Assert.That(lockedAfterFirst, Is.True);
            Assert.That(lockedAfterSecond, Is.True,
                "재전송이 토글을 한 번 더 돌리면 잠금이 풀린다 — 화면과 상태가 갈린다");
        }

        [Test]
        public void 없는_후보_번호에는_그런_후보가_없다는_사유가_온다()
        {
            var outcome = gate.HandleLockToggle(Me, 99, Ticket(1), isMine: true);

            Assert.That(outcome.HasRejection, Is.True);
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(BlessingLockRejectReason.NoSuchCandidate));
        }

        // ------------------------------------------------------------ 경계

        [Test]
        public void 참가자가_아니면_아무것도_발행하지_않는다()
        {
            var outcome = gate.HandleReroll(playerId: 999, Ticket(1), isMine: false);

            Assert.That(outcome.HasEvents, Is.False, "모르는 id 로 예외가 나거나 이벤트가 나가면 안 된다");
        }

        [Test]
        public void 남의_순번은_내_요청을_막지_않는다()
        {
            var ticket = Ticket(1);

            var mine = gate.HandleReroll(Me, ticket, isMine: true);
            var theirs = gate.HandleReroll(Other, ticket, isMine: false);

            Assert.That(mine.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(theirs.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh),
                "막히면 남이 내 재추첨을 취소시킬 수 있다");
        }

        [Test]
        public void 준비_단계가_끝난_뒤의_요청은_적용되지_않는다()
        {
            economy.EndPreparation();

            var outcome = gate.HandleReroll(Me, Ticket(1), isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.PreparationClosed));
            Assert.That(outcome.HasRejection, Is.False);
            Assert.That(outcome.HasEvents, Is.True, "화면은 현재 상태를 받아야 한다");
        }

        [Test]
        public void 지난_준비_단계의_요청은_적용되지_않는다()
        {
            var staleTicket = Ticket(1);

            economy.EndPreparation();
            economy.TryApplyRoundResults(1, new[] { new RoundOutcome(Me, 0, 0, true), new RoundOutcome(Other, 0, 0, false) });
            economy.BeginPreparation();
            gate.OpenPreparation();

            int before = economy.GetGold(Me);
            var outcome = gate.HandleReroll(Me, staleTicket, isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.StaleEpoch));
            Assert.That(economy.GetGold(Me), Is.EqualTo(before), "지난 라운드의 요청이 이번 골드를 쓰면 안 된다");
        }

        [Test]
        public void 순번이_잘못된_요청은_형식_오류로_걸린다()
        {
            int before = economy.GetGold(Me);

            var outcome = gate.HandleReroll(Me, new EconomyRequestTicket(gate.Epoch, 0), isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.Malformed));
            Assert.That(economy.GetGold(Me), Is.EqualTo(before));
        }
    }
}
