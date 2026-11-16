using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 도메인이 <b>화면이 그대로 그릴 수 있는 이벤트</b>를 완성해서 내놓는지 확인한다
    /// (Jira S15P21A606-31 "Network/UI가 계산을 복제하지 않도록 명령·이벤트·스냅샷의 단일 계약 유지",
    /// 축5(-37,-38) "계산 없이 스냅샷을 표시").
    ///
    /// <b>이 테스트가 지키는 것</b> — 어댑터가 조립을 하기 시작하면 같은 규칙이 Fake 와
    /// 실제 어댑터 양쪽에 생기고 반드시 갈라진다. 이 프로젝트는 이미 그 대가를 치렀다
    /// (같은 이름의 이벤트가 두 곳에 생겨 발행과 구독이 영원히 만나지 않았다).
    /// </summary>
    [TestFixture]
    public class MatchEconomyContractTests
    {
        private const int Alice = 1;
        private const int Bob = 2;
        private const int Seed = 0x31;

        private static IReadOnlyList<BlessingDefinition> BuildPool()
        {
            var pool = new List<BlessingDefinition>();
            foreach (BlessingTier tier in Enum.GetValues(typeof(BlessingTier)))
            {
                for (var i = 0; i < 4; i++)
                {
                    pool.Add(new BlessingDefinition(
                        $"{tier}-common-{i}", $"{tier} 공용 {i}", WeaponTag.Common, tier, $"{tier} 공용 설명 {i}"));
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
            state.BeginMatch(
                playerIds.Select(id => new MatchParticipant(id, WeaponTag.Sword)).ToArray(), Seed);
            return state;
        }

        // ── 후보 이벤트 ────────────────────────────────────────────────────

        [Test]
        public void 준비_이벤트는_화면이_그릴_것을_전부_담는다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();

            var e = state.BuildBlessingOffer(Alice, isMine: true);

            Assert.That(e.PlayerId, Is.EqualTo(Alice));
            Assert.That(e.IsMine, Is.True);
            Assert.That(e.Round, Is.EqualTo(1));
            Assert.That(e.Candidates.Length, Is.EqualTo(BlessingConfig.DefaultCandidateCount));
            Assert.That(e.RemainingLockCount, Is.EqualTo(BlessingConfig.DefaultMaxFreeLocks));
            Assert.That(e.HasRerollCost, Is.True, "1라운드 재추첨 비용은 정의돼 있다 (S-GD-SHJGNB)");
            Assert.That(e.CurrentRerollCost, Is.GreaterThan(0));
            Assert.That(e.NextRerollCostPreview, Is.GreaterThan(e.CurrentRerollCost), "2배씩 오른다");
            Assert.That(e.CanReroll, Is.True);
        }

        [Test]
        public void 후보_번호는_명령에_그대로_쓸_수_있는_값이다()
        {
            // 화면이 카드에서 꺼낸 CandidateIndex 를 그대로 명령에 넣을 수 있어야 한다.
            // 여기가 어긋나면 "카드를 눌렀는데 다른 게 잠긴다" 가 된다.
            var state = NewMatch(Alice);
            state.BeginPreparation();

            var e = state.BuildBlessingOffer(Alice, isMine: true);
            var indexes = e.Candidates.Select(c => c.CandidateIndex).ToArray();

            Assert.That(indexes, Is.EqualTo(Enumerable.Range(0, e.Candidates.Length).ToArray()));

            foreach (var index in indexes)
            {
                Assert.That(state.TryToggleLock(Alice, index), Is.True, $"후보 {index} 를 잠글 수 없다");
                state.TryToggleLock(Alice, index); // 다시 풀어 상한에 걸리지 않게 한다
            }
        }

        [Test]
        public void 잠근_후보는_이벤트에도_잠긴_것으로_나온다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();
            state.TryToggleLock(Alice, 1);

            var e = state.BuildBlessingOffer(Alice, isMine: true);

            Assert.That(e.Candidates[1].IsLocked, Is.True);
            Assert.That(e.Candidates.Count(c => c.IsLocked), Is.EqualTo(1));
            Assert.That(e.RemainingLockCount, Is.EqualTo(BlessingConfig.DefaultMaxFreeLocks - 1));
        }

        [Test]
        public void 획득_목록은_명령을_보낼_수_없다는_표시를_갖는다()
        {
            // 획득 목록은 후보가 아니다. -1 이 "이 항목에는 명령을 보내지 않는다" 는 뜻이고,
            // 화면이 실수로 클릭을 붙여도 도메인이 거부한다.
            var state = NewMatch(Alice);
            state.BeginPreparation();
            state.TrySelect(Alice, 0);
            state.EndPreparation();
            state.BeginPreparation();

            var e = state.BuildBlessingOffer(Alice, isMine: true);

            Assert.That(e.Acquired.Length, Is.EqualTo(1));
            Assert.That(e.Acquired[0].CandidateIndex, Is.EqualTo(-1));
            Assert.That(state.TrySelect(Alice, -1), Is.Null, "도메인도 -1 을 거부한다");
        }

        [Test]
        public void 준비_단계가_아니면_후보가_빈_배열이다()
        {
            // null 이 아니라 길이 0 이다 (규약 §11). 화면이 null 검사를 안 해도 안전해야 한다.
            var state = NewMatch(Alice);
            state.BeginPreparation();
            state.EndPreparation();

            var e = state.BuildBlessingOffer(Alice, isMine: true);

            Assert.That(e.Candidates, Is.Not.Null);
            Assert.That(e.Candidates, Is.Empty);
            Assert.That(e.CanReroll, Is.False);
        }

        [Test]
        public void 설명이_없는_축복도_null_이_아니라_빈_문자열이다()
        {
            // 화면이 그대로 출력해도 "null" 이 찍히지 않아야 한다.
            var state = NewMatch(Alice);
            state.BeginPreparation();

            var e = state.BuildBlessingOffer(Alice, isMine: true);

            foreach (var candidate in e.Candidates)
            {
                Assert.That(candidate.Description, Is.Not.Null);
                Assert.That(candidate.DisplayName, Is.Not.Null.And.Not.Empty);
                Assert.That(candidate.BlessingId, Is.Not.Null.And.Not.Empty);
            }
        }

        // ── 골드 이벤트 ────────────────────────────────────────────────────

        [Test]
        public void 골드_이벤트는_잔액과_이월_한도를_함께_싣는다()
        {
            // 한도를 별도 이벤트로 빼면 그걸 놓친 화면이 "이월 한도 —" 로 남는다.
            var state = NewMatch(Alice);
            state.BeginPreparation();

            var e = state.BuildGoldChanged(Alice, isMine: true, delta: 1_000, GoldChangeReason.RoundGrant);

            Assert.That(e.Gold, Is.EqualTo(EconomyConfig.DefaultRoundGrantGold));
            Assert.That(e.Delta, Is.EqualTo(1_000));
            Assert.That(e.Reason, Is.EqualTo(GoldChangeReason.RoundGrant));
            Assert.That(e.IsCarryOverUnlimited, Is.False);
            Assert.That(e.CarryOverCap, Is.EqualTo(EconomyConfig.Default.CarryOverLimit.Gold));
        }

        [Test]
        public void 무제한_이월에서도_골드_이벤트가_예외_없이_만들어진다()
        {
            // CarryOverLimit.Gold 는 무제한일 때 예외를 던진다. 조립하는 쪽이 IsUnlimited 를
            // 먼저 안 보면 여기서 터진다 — 어댑터에 조립을 맡기지 않는 이유 중 하나다.
            var config = EconomyConfig.Default.WithCarryOverLimit(CarryOverLimit.Unlimited);
            var state = new MatchEconomyState(config, BlessingConfig.Default, BuildPool());
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);
            state.BeginPreparation();

            GoldChanged e = default;
            Assert.DoesNotThrow(() =>
                e = state.BuildGoldChanged(Alice, isMine: true, 0, GoldChangeReason.RoundGrant));

            Assert.That(e.IsCarryOverUnlimited, Is.True);
        }

        [Test]
        public void 정산_이벤트는_이월액과_소멸액을_함께_싣는다()
        {
            var config = EconomyConfig.Default.WithCarryOverLimitSetting(1_500);
            var state = new MatchEconomyState(config, BlessingConfig.Default, BuildPool());
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);
            state.BeginPreparation();
            state.Earn(Alice, 5_000);
            state.EndPreparation();
            state.BeginPreparation();

            Assert.That(state.TryBuildRoundGoldSettled(Alice, isMine: true, out var e), Is.True);

            Assert.That(e.Round, Is.EqualTo(1));
            Assert.That(e.CarriedGold, Is.EqualTo(1_500));
            Assert.That(e.ForfeitedGold, Is.EqualTo(4_500));
        }

        /// <summary>
        /// 정산은 <b>다음 라운드 준비가 열릴 때</b> 일어난다. 그때 실리는 라운드 번호는
        /// <b>정산 대상</b>이어야 한다 — 2라운드 준비에서 넘어온 돈은 1라운드에서 남은 것이다.
        ///
        /// <para>
        /// 여기가 틀리면 화면이 "2라운드 정산 — 이월 500G" 로 그리는데 그 500G 는 1라운드 것이다.
        /// 숫자가 그럴듯해서 사람이 봐도 알아채기 어렵다.
        /// </para>
        /// </summary>
        [Test]
        public void 정산_이벤트는_정산_대상_라운드를_싣는다()
        {
            var state = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, BuildPool());
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);

            state.BeginPreparation();   // 1라운드
            state.EndPreparation();
            state.BeginPreparation();   // 2라운드 준비 — 여기서 1라운드가 정산된다

            Assert.That(state.TryBuildRoundGoldSettled(Alice, isMine: true, out var second), Is.True);
            Assert.That(second.Round, Is.EqualTo(1), "2라운드 준비에서 정산되는 것은 1라운드 잔액이다");

            state.EndPreparation();
            state.BeginPreparation();   // 3라운드 준비

            Assert.That(state.TryBuildRoundGoldSettled(Alice, isMine: true, out var third), Is.True);
            Assert.That(third.Round, Is.EqualTo(2));
        }

        /// <summary>
        /// 1라운드에는 정산할 이전 라운드가 없다. 이벤트를 내면 화면이 "0G 이월" 을
        /// <b>정산 결과로</b> 그리게 된다 — 정산이 없었던 것과 구분되지 않는다.
        /// </summary>
        [Test]
        public void 첫_라운드에는_정산_이벤트가_없다()
        {
            var state = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, BuildPool());
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);
            state.BeginPreparation();

            Assert.That(
                state.TryBuildRoundGoldSettled(Alice, isMine: true, out _), Is.False,
                "1라운드 준비에서는 정산이 일어나지 않는다");
        }

        // ── IsMine (규약 §7) ──────────────────────────────────────────────

        [Test]
        public void IsMine_은_도메인이_아니라_부르는_쪽이_정한다()
        {
            // 도메인은 누가 로컬 플레이어인지 모른다. 관전 중에는 남의 것도 그려야 하므로
            // 도메인이 미리 걸러버리면 안 된다.
            var state = NewMatch(Alice, Bob);
            state.BeginPreparation();

            Assert.That(state.BuildBlessingOffer(Bob, isMine: false).IsMine, Is.False);
            Assert.That(state.BuildBlessingOffer(Bob, isMine: true).IsMine, Is.True);
            Assert.That(state.BuildGoldChanged(Bob, false, 0, GoldChangeReason.RoundGrant).IsMine, Is.False);
        }

        [Test]
        public void 참가자가_아니면_이벤트를_만들_수_없다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();

            Assert.Throws<ArgumentOutOfRangeException>(() => state.BuildBlessingOffer(999, true));
        }

        // ── 어댑터가 계산할 것이 남지 않았는가 ────────────────────────────

        [Test]
        public void 재추첨_거부_사유가_이벤트에_그대로_실린다()
        {
            // 화면이 골드를 직접 비교하지 않는다. 판정은 도메인 하나뿐이다.
            var config = EconomyConfig.Default.WithRoundGrantGold(0);
            var state = new MatchEconomyState(config, BlessingConfig.Default, BuildPool());
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, Seed);
            state.BeginPreparation();

            var e = state.BuildBlessingOffer(Alice, isMine: true);

            Assert.That(e.CanReroll, Is.False);
            Assert.That(e.RerollBlockedBy, Is.EqualTo(RerollResult.NotEnoughGold));
        }

        [Test]
        public void 선택_이벤트의_누적_수는_도메인_상태와_같다()
        {
            var state = NewMatch(Alice);
            state.BeginPreparation();
            var selected = state.TrySelect(Alice, 0);

            var e = state.BuildBlessingSelected(Alice, isMine: true, selected);

            Assert.That(e.BlessingId, Is.EqualTo(selected.Id));
            Assert.That(e.AcquiredCount, Is.EqualTo(state.GetAcquired(Alice).Count));
            Assert.That(e.Round, Is.EqualTo(state.Round));
        }

        [Test]
        public void 라운드가_바뀌어도_이벤트가_현재_라운드를_가리킨다()
        {
            // 준비 화면의 라운드 번호가 한 라운드씩 밀리는 문제가 실제로 있었다.
            var state = NewMatch(Alice);

            for (var round = 1; round <= state.TotalRounds; round++)
            {
                state.BeginPreparation();
                Assert.That(state.BuildBlessingOffer(Alice, true).Round, Is.EqualTo(round));
                state.EndPreparation();
            }
        }
    }
}
