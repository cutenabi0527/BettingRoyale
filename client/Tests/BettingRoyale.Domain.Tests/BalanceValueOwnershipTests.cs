using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 기획 데이터와 프리팹 대역이 어긋났는지 가리는 검사 (D-CLI-VMX6AN).
    /// </summary>
    [TestFixture]
    public sealed class BalanceValueOwnershipTests
    {
        [Test]
        public void 같은_값이면_경고하지_않는다()
        {
            Assert.That(BalanceValueOwnership.IsMismatch(6.5f, 6.5f), Is.False);
            Assert.That(BalanceValueOwnership.DescribeMismatch("move-speed", 6.5f, 6.5f), Is.Null);
        }

        [Test]
        public void 값이_다르면_두_값을_모두_적은_경고를_낸다()
        {
            string note = BalanceValueOwnership.DescribeMismatch("max-hp", 100f, 150f);

            Assert.That(note, Is.Not.Null);
            Assert.That(note, Does.Contain("max-hp"));
            Assert.That(note, Does.Contain("100"), "어느 값이 데이터인지 알 수 없으면 고칠 곳을 못 찾는다.");
            Assert.That(note, Does.Contain("150"));
        }

        [TestCase(0f, 5f)]
        [TestCase(5f, 0f)]
        [TestCase(float.NaN, 5f)]
        [TestCase(5f, float.PositiveInfinity)]
        public void 한쪽이_없으면_비교하지_않는다(float dataValue, float prefabFallback)
        {
            // 데이터에 없는 스탯(0)까지 어긋났다고 하면, 대역만 있는 항목마다 경고가 뜬다.
            Assert.That(BalanceValueOwnership.IsMismatch(dataValue, prefabFallback), Is.False);
        }

        [Test]
        public void 부동소수_반올림_차이는_같다고_본다()
        {
            Assert.That(BalanceValueOwnership.IsMismatch(6.5f, 6.5001f), Is.False);
            Assert.That(BalanceValueOwnership.IsMismatch(6.5f, 6.51f), Is.True);
        }

        [Test]
        public void 어긋난_항목만_모아_돌려준다()
        {
            var notes = BalanceValueOwnership.DescribeMismatches(new[]
            {
                ("max-hp", 100f, 150f),
                ("move-speed", 6.5f, 6.5f),
                ("dash-distance", 1.8f, 1.8f),
            });

            Assert.That(notes.Count, Is.EqualTo(1));
            Assert.That(notes[0], Does.Contain("max-hp"));
        }

        [Test]
        public void 목록이_없어도_터지지_않는다()
        {
            Assert.That(BalanceValueOwnership.DescribeMismatches(null), Is.Empty);
        }
    }
}
