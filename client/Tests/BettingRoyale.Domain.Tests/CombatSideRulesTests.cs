using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public class CombatSideRulesTests
    {
        [Test]
        public void ShouldEndCombat_ReturnsTrue_WhenOnlyOneIndividualRemains()
        {
            var members = new[]
            {
                new CombatSideRules.MemberStatus(sideId: 101, isAlive: false),
                new CombatSideRules.MemberStatus(sideId: 102, isAlive: true),
                new CombatSideRules.MemberStatus(sideId: 103, isAlive: false),
            };

            Assert.That(CombatSideRules.ShouldEndCombat(members), Is.True);
        }

        [Test]
        public void ShouldEndCombat_ReturnsTrue_WhenOnlyOneTeamRemains()
        {
            var members = new[]
            {
                new CombatSideRules.MemberStatus(sideId: 1, isAlive: true),
                new CombatSideRules.MemberStatus(sideId: 1, isAlive: false),
                new CombatSideRules.MemberStatus(sideId: 2, isAlive: false),
                new CombatSideRules.MemberStatus(sideId: 2, isAlive: false),
            };

            Assert.That(CombatSideRules.ShouldEndCombat(members), Is.True);
        }

        [Test]
        public void ShouldEndCombat_ReturnsFalse_WhileMultipleSidesRemain()
        {
            var members = new[]
            {
                new CombatSideRules.MemberStatus(sideId: 1, isAlive: true),
                new CombatSideRules.MemberStatus(sideId: 1, isAlive: false),
                new CombatSideRules.MemberStatus(sideId: 2, isAlive: true),
            };

            Assert.That(CombatSideRules.ShouldEndCombat(members), Is.False);
        }

        [Test]
        public void ShouldEndCombat_ReturnsFalse_WhenMatchNeverHadAnOpponentSide()
        {
            var members = new[]
            {
                new CombatSideRules.MemberStatus(sideId: 7, isAlive: true),
                new CombatSideRules.MemberStatus(sideId: 7, isAlive: false),
            };

            Assert.That(CombatSideRules.ShouldEndCombat(members), Is.False);
        }

        [TestCase(3, 3, true)]
        [TestCase(3, 4, false)]
        public void CanViewSideScopedFeedback_AllowsOnlyTheSourceSide(
            int viewerSideId,
            int sourceSideId,
            bool expected)
        {
            Assert.That(
                CombatSideRules.CanViewSideScopedFeedback(viewerSideId, sourceSideId),
                Is.EqualTo(expected));
        }

        [Test]
        public void IndividualResolver_UsesTheSameSideMappingForRulesAndScopedFeedback()
        {
            ICombatSideResolver resolver = IndividualCombatSideResolver.Instance;
            var viewer = new CombatantId(41);
            var teammate = new CombatantId(41);
            var opponent = new CombatantId(42);

            Assert.That(resolver.ResolveSideId(viewer), Is.EqualTo(41));
            Assert.That(
                CombatSideRules.CanViewSideScopedFeedback(
                    resolver.ResolveSideId(viewer),
                    resolver.ResolveSideId(teammate)),
                Is.True);
            Assert.That(
                CombatSideRules.CanViewSideScopedFeedback(
                    resolver.ResolveSideId(viewer),
                    resolver.ResolveSideId(opponent)),
                Is.False);
        }
    }
}
