using BettingRoyale.Data.Throwing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public sealed class VariableThrowTimingTests
    {
        [TestCase(0, 0.2)]
        // 최대 비행 0.7초 기준. 최소 0.2초에서 거리 비율로 보간한다 (S-GD-JFG6XM).
        [TestCase(0.5, 0.45)]
        [TestCase(1, 0.7)]
        public void DistanceScalesFlightWithoutChangingLandingWarning(double ratio, double expected)
        {
            var catalog = new RecommendedMagicBottleCatalog();
            Assert.That(catalog.TryGetThrowing(2103, out var definition), Is.True);
            var accepted = new AcceptedThrow(1, 1, ThrowRoundPhase.Combat, 2103, 0, 1, 10, 1, 12, definition);
            Assert.That(ThrownProjectile.TryCreateLaunched(accepted, ratio, out var projectile), Is.True);
            Assert.That(projectile.FlightSeconds, Is.EqualTo(expected).Within(0.00001));
            Assert.That(projectile.ExpectedLandingTimeSeconds, Is.EqualTo(10 + expected).Within(0.00001));
            projectile.ReportLanded(10 + expected);
            Assert.That(projectile.FuseEndsAtSeconds, Is.EqualTo(10 + expected + definition.WarningSeconds).Within(0.00001));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void NonFiniteRatioCannotCreateAProjectile(double ratio)
        {
            Assert.That(ThrowFlightTiming.Resolve(1, ratio), Is.Zero);
        }
    }
}
