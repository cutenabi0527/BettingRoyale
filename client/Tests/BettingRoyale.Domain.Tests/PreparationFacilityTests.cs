using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Preparation;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 준비맵 기능 구역 — 다가가면 열리는 원 (S-GD-66GTWA, S-CLI-9QYFCT, Jira S15P21A606-84).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것은 둘이다.</b> 구역이 겹치지 않는다는 것과, 구역 밖에서는
    /// 아무 시설도 열리지 않는다는 것. 겹치면 한 걸음에 상점이 닫히고 룰렛이 열리는 자리가 생기고,
    /// 구역 판정이 없으면 맵 반대편에서 물건을 살 수 있다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PreparationFacilityTests
    {
        private static WorldPosition At(float x, float z) => new WorldPosition(x, 0f, z);

        // ── 겹침 금지 ───────────────────────────────────────────────────

        [Test]
        public void 구역이_겹치면_만들_수_없다()
        {
            var overlapping = new List<PreparationFacilityArea>
            {
                new PreparationFacilityArea(PreparationFacility.Roulette, At(0f, 0f), 6f),
                new PreparationFacilityArea(PreparationFacility.Shop, At(11f, 0f), 6f),
            };

            var error = Assert.Throws<ArgumentException>(() => new PreparationFacilityMap(overlapping));

            Assert.That(error.Message, Does.Contain("겹칩니다"),
                "겹치는 경계에서 어느 시설이 열릴지 사람이 예측할 수 없다");
        }

        [Test]
        public void 딱_맞닿는_것도_겹침으로_본다()
        {
            // 11m 간격에 5 + 6 = 11 — 한 점에서 만난다. 그 점에 선 사람은 두 구역 안이다.
            var touching = new List<PreparationFacilityArea>
            {
                new PreparationFacilityArea(PreparationFacility.Roulette, At(0f, 0f), 5f),
                new PreparationFacilityArea(PreparationFacility.Shop, At(11f, 0f), 6f),
            };

            Assert.Throws<ArgumentException>(() => new PreparationFacilityMap(touching));
        }

        [Test]
        public void 같은_시설을_두_번_넣을_수_없다()
        {
            var duplicated = new List<PreparationFacilityArea>
            {
                new PreparationFacilityArea(PreparationFacility.Shop, At(11f, 0f), 3f),
                new PreparationFacilityArea(PreparationFacility.Shop, At(-11f, 0f), 3f),
            };

            Assert.Throws<ArgumentException>(() => new PreparationFacilityMap(duplicated));
        }

        [Test]
        public void 대체_배치는_겹침을_허용해야_만들어진다()
        {
            // 룰렛 8 + 상점 5 = 13 > 11m 간격 — 겹침 허용 없이는 생성자가 던진다 (S-GD-EMDEBM).
            Assert.That(PlaceholderPreparationFacilityMap.AllowOverlap, Is.True);
            Assert.DoesNotThrow(() => PlaceholderPreparationFacilityMap.Create());
            Assert.That(PlaceholderPreparationFacilityMap.Create().Count, Is.EqualTo(3));
        }

        /// <summary>
        /// 준비 콜로세움의 룰렛 받침 충돌체(반지름 4.65m)에 막힌 캐릭터 중심(캡슐 0.5 + skin 0.08)도 구역 안이어야 한다.
        /// 예전 5m 로는 5.17~5.23m 에서 멈춘 캐릭터가 영영 들어가지 못했다 (S-GD-EMDEBM).
        /// </summary>
        [TestCase(5.23f, 0f, TestName = "받침_꼭짓점_방향")]
        [TestCase(0f, 5.23f, TestName = "받침_앞쪽")]
        [TestCase(0f, -5.23f, TestName = "받침_뒤쪽")]
        public void 룰렛_받침에_막힌_자리도_룰렛이다(float x, float z)
        {
            var map = PlaceholderPreparationFacilityMap.Create();

            Assert.That(map.Resolve(At(x, z)), Is.EqualTo(PreparationFacility.Roulette));
        }

        // ── 잘못된 구역 ─────────────────────────────────────────────────

        [Test]
        public void None_에는_구역을_줄_수_없다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PreparationFacilityArea(PreparationFacility.None, At(0f, 0f), 3f),
                "None 은 '구역 없음' 을 뜻하는 값이다");
        }

        [TestCase(0f, TestName = "반경_0")]
        [TestCase(-1f, TestName = "반경_음수")]
        [TestCase(float.NaN, TestName = "반경_NaN")]
        public void 반경이_0_이하면_만들_수_없다(float radius)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PreparationFacilityArea(PreparationFacility.Shop, At(11f, 0f), radius));
        }

        // ── 어느 시설이 열리는가 ────────────────────────────────────────

        [TestCase(0f, 0f, PreparationFacility.Roulette, TestName = "룰렛_중심")]
        [TestCase(0f, 8f, PreparationFacility.Roulette, TestName = "룰렛_경계는_안쪽")]
        [TestCase(0f, 8.1f, PreparationFacility.None, TestName = "룰렛_경계_밖")]
        [TestCase(5.9f, 0f, PreparationFacility.Roulette, TestName = "상점_구역_바로_앞은_룰렛")]
        [TestCase(11f, 0f, PreparationFacility.Shop, TestName = "상점_중심")]
        [TestCase(-11f, 0f, PreparationFacility.Upgrade, TestName = "모루_중심")]
        [TestCase(7.5f, 0f, PreparationFacility.Shop, TestName = "겹친_자리_상점에_가까우면_상점")]
        [TestCase(-7.5f, 0f, PreparationFacility.Upgrade, TestName = "겹친_자리_모루에_가까우면_모루")]
        [TestCase(6f, 0f, PreparationFacility.Shop, TestName = "상점_경계는_안쪽")]
        [TestCase(-6f, 0f, PreparationFacility.Upgrade, TestName = "모루_경계는_안쪽")]
        [TestCase(6.5f, 0f, PreparationFacility.Shop, TestName = "예전_빈_공간이_이제_상점이다")]
        [TestCase(0f, 12f, PreparationFacility.None, TestName = "허수아비_앞")]
        [TestCase(0f, -12f, PreparationFacility.None, TestName = "집결_공간")]
        public void 좌표가_어느_시설을_여는지(float x, float z, PreparationFacility expected)
        {
            var map = PlaceholderPreparationFacilityMap.Create();

            Assert.That(map.Resolve(At(x, z)), Is.EqualTo(expected));
        }

        [Test]
        public void 룰렛과_상점_사이는_상점_가장자리에서_갈린다()
        {
            var map = PlaceholderPreparationFacilityMap.Create();

            // 빈 공간은 더 이상 없다 (S-GD-EMDEBM). 겹친 자리(x 6~8)는 두 구역 모두에 있고 상점 중심이 더 가까우므로
            // 상점 가장자리 x = 6 에서 갈린다. 옛 확정(S-GD-VYGZTJ)의 "5.1~5.9 는 아무 시설도 아니다" 를 대체한다.
            for (float x = 0f; x < 5.95f; x += 0.1f)
                Assert.That(map.Resolve(At(x, 0f)), Is.EqualTo(PreparationFacility.Roulette), $"x={x}");
            for (float x = 6.05f; x <= 16f; x += 0.1f)
                Assert.That(map.Resolve(At(x, 0f)), Is.EqualTo(PreparationFacility.Shop), $"x={x}");
        }

        // ── 게이트 ──────────────────────────────────────────────────────

        [Test]
        public void 위치를_모르면_아무_시설도_열리지_않는다()
        {
            var presence = new FakePresence();
            var gate = new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence);

            Assert.That(gate.Resolve(playerId: 1), Is.EqualTo(PreparationFacility.None));
            Assert.That(gate.CanUse(1, PreparationFacility.Shop), Is.False);
        }

        [Test]
        public void 구역을_벗어나면_None_이_실려_창을_닫는다()
        {
            var presence = new FakePresence();
            var gate = new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence);

            presence.Place(1, At(11f, 0f));
            Assert.That(gate.BuildInRange(1, isMine: true).Facility, Is.EqualTo(PreparationFacility.Shop));

            presence.Place(1, At(11f, 10f));   // 걸어 나갔다
            var left = gate.BuildInRange(1, isMine: true);

            Assert.That(left.Facility, Is.EqualTo(PreparationFacility.None),
                "이 값이 열려 있던 창을 닫는 신호다 (조영운 확정, 2026-09-08)");
            Assert.That(left.PlayerId, Is.EqualTo(1));
            Assert.That(left.IsMine, Is.True);
        }

        [Test]
        public void None_은_어떤_시설로도_쓸_수_없다()
        {
            var presence = new FakePresence();
            presence.Place(1, At(11f, 0f));
            var gate = new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence);

            Assert.That(gate.CanUse(1, PreparationFacility.None), Is.False,
                "'아무 데도 아님' 을 시설로 물으면 항상 false 여야 한다");
        }

        private sealed class FakePresence : IPreparationPresenceProvider
        {
            private readonly Dictionary<int, WorldPosition> byPlayer = new();

            public void Place(int playerId, WorldPosition position) => byPlayer[playerId] = position;

            public bool TryGetPosition(int playerId, out WorldPosition position) =>
                byPlayer.TryGetValue(playerId, out position);
        }
    }
}
