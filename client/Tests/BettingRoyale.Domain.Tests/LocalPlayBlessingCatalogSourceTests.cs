using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.LocalPlay;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복 카탈로그가 정본과 대역 사이를 어떻게 고르는지 (S15P21A606-201).
    ///
    /// <para>
    /// ⚠️ 이 id 들은 <b>회선에 실린다</b>(OnlineEconomyCoordinator). 두 피어가 서로 다른 목록을
    /// 보면 안 되므로, "언제 대역을 쓰는가" 가 계약이다.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class LocalPlayBlessingCatalogSourceTests
    {
        [TearDown]
        public void 정본을_비운다() => GameContentProvider.Reset();

        /// <summary>배치모드(CI·EditMode)는 정본이 없다 — 대역이 정상 경로다.</summary>
        [Test]
        public void 정본이_없으면_대역을_쓴다()
        {
            GameContentProvider.Reset();

            Assert.That(GameContentProvider.IsLoaded, Is.False);
            Assert.That(LocalPlayBlessingCatalog.All, Is.Not.Empty);
        }

        [Test]
        public void 정본을_불러오면_그것을_쓴다()
        {
            var loaded = new[]
            {
                new BlessingDefinition("from-data-t1", "정본", WeaponTag.Common, BlessingTier.Silver),
            };
            GameContentProvider.Set(new GameContent(2, null, loaded, null, null, null));

            Assert.That(LocalPlayBlessingCatalog.All.Select(b => b.Id), Is.EqualTo(new[] { "from-data-t1" }));
        }

        /// <summary>정본이 비어 있으면 대역으로 돌아간다 — 빈 목록이면 후보를 못 뽑는다.</summary>
        [Test]
        public void 정본이_비어_있으면_대역으로_돌아간다()
        {
            GameContentProvider.Set(new GameContent(2, null, new BlessingDefinition[0], null, null, null));

            Assert.That(LocalPlayBlessingCatalog.All, Is.Not.Empty);
        }
    }
}
