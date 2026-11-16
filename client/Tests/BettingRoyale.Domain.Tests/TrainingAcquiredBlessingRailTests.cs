using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.LocalPlay;
using BettingRoyale.Data.Training;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 훈련장에서 고른 축복이 <b>좌측 보유 축복 레일</b>까지 도달하는지 못 박는다 (Jira 182).
    /// </summary>
    /// <remarks>
    /// 레일은 <see cref="BlessingOfferChanged"/> 만 듣는데 훈련 경로에는 그 발행자가 하나도
    /// 없었다 — Client 뿐 아니라 <b>Host 에서도</b> 빈 액자였다 (2026-09-16 5인 실측).
    /// </remarks>
    public class TrainingAcquiredBlessingRailTests
    {
        private const int PlayerId = 7;

        private TrainingAcquiredBlessingRail rail;

        [SetUp]
        public void SetUp() => rail = new TrainingAcquiredBlessingRail();

        [Test]
        public void AcquiredIds_BecomeTheProductContract()
        {
            BlessingDefinition first = Catalog[0];
            BlessingDefinition second = Catalog[1];

            Assert.That(TryBuild(out BlessingOfferChanged offer, out int unresolved,
                first.Id, second.Id), Is.True, "보유 축복이 레일 계약으로 나가지 않았습니다.");

            Assert.That(unresolved, Is.Zero);
            Assert.That(offer.PlayerId, Is.EqualTo(PlayerId));
            Assert.That(offer.IsMine, Is.True,
                "훈련 상태는 본인 것만 도착한다 — 레일이 그리려면 IsMine 이어야 합니다.");
            Assert.That(offer.Acquired.Length, Is.EqualTo(2));

            // 회선에는 id 만 실린다. 이름·설명·등급은 카탈로그에서 되찾는다.
            Assert.That(offer.Acquired[0].BlessingId, Is.EqualTo(first.Id));
            Assert.That(offer.Acquired[0].DisplayName, Is.EqualTo(first.DisplayName));
            Assert.That(offer.Acquired[0].Description, Is.EqualTo(first.Description));
            Assert.That(offer.Acquired[0].Tier, Is.EqualTo(first.Tier));
            Assert.That(offer.Acquired[0].Tag, Is.EqualTo(first.Tag));
            Assert.That(offer.Acquired[1].BlessingId, Is.EqualTo(second.Id));
        }

        [Test]
        public void Acquired_AreNotCandidates()
        {
            TryBuild(out BlessingOfferChanged offer, out _, Catalog[0].Id);

            // 획득분은 후보가 아니다 — 계약이 정한 자리값 (S-GD-RFXF8X).
            Assert.That(offer.Acquired[0].CandidateIndex, Is.EqualTo(-1));
            Assert.That(offer.Acquired[0].IsLocked, Is.False);
        }

        [Test]
        public void TrainingOffer_NeverOffersARerollButton()
        {
            TryBuild(out BlessingOfferChanged offer, out _, Catalog[0].Id);

            // 훈련장에는 추첨 자체가 없다. 후보 칸을 채우거나 재추첨을 열어두면
            // 화면이 "다시 뽑기" 를 그리고, 눌러도 아무 일이 없는 버튼이 생긴다.
            Assert.That(offer.Candidates, Is.Not.Null);
            Assert.That(offer.Candidates.Length, Is.Zero);
            Assert.That(offer.CanReroll, Is.False);
            Assert.That(offer.RerollBlockedBy, Is.EqualTo(RerollResult.AlreadySelected));
            Assert.That(offer.HasRerollCost, Is.False);
        }

        [Test]
        public void SameSnapshotTwice_PublishesOnce()
        {
            string id = Catalog[0].Id;

            Assert.That(TryBuild(out _, out _, id), Is.True);

            // 스냅샷은 주기적으로 온다. 같은 내용을 매번 흘리면 레일이 초당 몇 번씩 다시 그려진다.
            Assert.That(TryBuild(out _, out _, id), Is.False, "같은 보유 목록이 두 번 발행됐습니다.");
        }

        [Test]
        public void NewlyAcquiredBlessing_PublishesAgain()
        {
            string first = Catalog[0].Id;
            string second = Catalog[1].Id;

            Assert.That(TryBuild(out _, out _, first), Is.True);

            // 중복 차단이 과하면 레일이 첫 축복에서 얼어붙는다 — 그게 원래 증상과 구분되지 않는다.
            Assert.That(TryBuild(out BlessingOfferChanged offer, out _, first, second), Is.True);
            Assert.That(offer.Acquired.Length, Is.EqualTo(2));
        }

        [Test]
        public void ReorderedSameIds_StillPublish()
        {
            string a = Catalog[0].Id, b = Catalog[1].Id;

            Assert.That(TryBuild(out _, out _, a, b), Is.True);

            // 순서가 바뀌면 화면에 보이는 줄도 바뀐다. 개수만 비교하면 이걸 놓친다.
            Assert.That(TryBuild(out BlessingOfferChanged offer, out _, b, a), Is.True);
            Assert.That(offer.Acquired[0].BlessingId, Is.EqualTo(b));
        }

        [Test]
        public void EmptyAcquiredList_DoesNotWakeTheRail()
        {
            // 입장 직후 스냅샷은 보유 0개다. 그때마다 빈 이벤트를 쏘면 다른 화면의
            // 축복 표시가 매번 지워진다.
            Assert.That(TryBuild(out _, out _), Is.False);
        }

        [Test]
        public void NullAcquiredList_IsTreatedAsEmpty()
        {
            Assert.That(rail.TryBuild(PlayerId, null, Catalog, out _, out _), Is.False);
        }

        [Test]
        public void ClearedAfterOwning_PublishesTheEmptyRail()
        {
            Assert.That(TryBuild(out _, out _, Catalog[0].Id), Is.True);

            // 초기화(Reset 액션)로 보유가 비면 레일도 비어야 한다. 안 그러면 없는 축복이 남아 보인다.
            Assert.That(TryBuild(out BlessingOfferChanged offer, out _), Is.True);
            Assert.That(offer.Acquired.Length, Is.Zero);
        }

        [Test]
        public void IdMissingFromCatalog_IsCountedNotSwallowed()
        {
            Assert.That(TryBuild(out BlessingOfferChanged offer, out int unresolved,
                Catalog[0].Id, "존재하지-않는-축복-id"), Is.True);

            // 말없이 하나 적게 그리면 "축복이 사라졌다" 를 아무도 설명하지 못한다.
            // 부르는 쪽이 이 수를 보고 경고를 남긴다.
            Assert.That(offer.Acquired.Length, Is.EqualTo(1));
            Assert.That(unresolved, Is.EqualTo(1));
        }

        [Test]
        public void Reset_LetsTheNextSessionRepublishTheSameList()
        {
            string id = Catalog[0].Id;
            Assert.That(TryBuild(out _, out _, id), Is.True);
            Assert.That(TryBuild(out _, out _, id), Is.False);

            rail.Reset();

            // 새 훈련 세션의 첫 스냅샷이 우연히 같은 내용이면, 기억을 안 비우면 영원히 안 그려진다.
            Assert.That(TryBuild(out _, out _, id), Is.True);
        }

        [Test]
        public void NullCatalog_LosesEveryIdButStillReports()
        {
            Assert.That(rail.TryBuild(PlayerId, new[] { "any-id" }, null,
                out BlessingOfferChanged offer, out int unresolved), Is.True);

            Assert.That(offer.Acquired.Length, Is.Zero);
            Assert.That(unresolved, Is.EqualTo(1));
        }

        private static IReadOnlyList<BlessingDefinition> Catalog => LocalPlayBlessingCatalog.All;

        private bool TryBuild(out BlessingOfferChanged offer, out int unresolved, params string[] ids) =>
            rail.TryBuild(PlayerId, ids ?? Array.Empty<string>(), Catalog, out offer, out unresolved);
    }
}
