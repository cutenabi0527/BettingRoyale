using BettingRoyale.Data.Remote;
using NUnit.Framework;
using Verdict = BettingRoyale.Data.Remote.ClientVersionGate.ClientVersionVerdict;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 기동 시 구버전 차단 판정 회귀 (S15P21A606-289, S-REL-WVSB8C, D-REL-RBJ9AA).
    /// </summary>
    /// <remarks>
    /// 이 판정이 틀리면 <b>아무도 게임에 들어오지 못한다.</b> 그래서 "막는다" 쪽보다
    /// "확신이 없으면 통과시킨다" 쪽을 더 촘촘히 지킨다.
    /// </remarks>
    public class ClientVersionGateTests
    {
        [Test]
        public void MacPlayer_ReadsMacChannelLatest()
        {
            // Mac 게시(publish-game-files.sh ... macos-arm64)가 쓰는 경로와 같아야 한다 (S15P21A606-311).
            Assert.That(ClientVersionGate.LatestInfoPath(isMacOSPlayer: true), Is.EqualTo("client/macos/latest.json"));
        }

        [Test]
        public void WindowsAndEditor_KeepExistingLatestPath()
        {
            Assert.That(ClientVersionGate.LatestInfoPath(isMacOSPlayer: false), Is.EqualTo("client/latest.json"));
        }

        [Test]
        public void SameCommit_IsCurrent()
        {
            Verdict verdict = ClientVersionGate.Evaluate("0.1.0+758e1586", "758e1586", out string local);

            Assert.That(verdict, Is.EqualTo(Verdict.Current));
            Assert.That(local, Is.EqualTo("758e1586"));
        }

        [Test]
        public void DifferentCommit_IsOutdated()
        {
            Assert.That(
                ClientVersionGate.Evaluate("0.1.0+758e1586", "2128382d", out _),
                Is.EqualTo(Verdict.Outdated));
        }

        [TestCase("0.1.0+758e1586aabbccdd", "758e1586")]
        [TestCase("0.1.0+758e1586", "758E1586AABB")]
        public void DifferentLengthsOfSameCommit_AreCurrent(string applicationVersion, string published)
        {
            Assert.That(
                ClientVersionGate.Evaluate(applicationVersion, published, out _),
                Is.EqualTo(Verdict.Current));
        }

        [TestCase("0.1.0", "758e1586")]
        [TestCase("", "758e1586")]
        [TestCase(null, "758e1586")]
        public void UnstampedDevelopmentBuild_IsUnknown(string applicationVersion, string published)
        {
            Assert.That(
                ClientVersionGate.Evaluate(applicationVersion, published, out _),
                Is.EqualTo(Verdict.Unknown));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void MissingPublishedCommit_IsUnknown(string published)
        {
            Assert.That(
                ClientVersionGate.Evaluate("0.1.0+758e1586", published, out _),
                Is.EqualTo(Verdict.Unknown));
        }

        /// <summary>
        /// rollback-client.sh 는 과거 기록이 없는 버전으로 되돌릴 때 <c>"commit": "unknown"</c> 을 쓴다.
        /// 이것을 커밋으로 견주면 롤백한 빌드까지 전부 막힌다.
        /// </summary>
        [TestCase("unknown")]
        [TestCase("UNKNOWN")]
        [TestCase("n/a")]
        public void NonCommitPublishedValue_IsUnknown(string published)
        {
            Assert.That(
                ClientVersionGate.Evaluate("0.1.0+758e1586", published, out _),
                Is.EqualTo(Verdict.Unknown));
        }

        [TestCase("7")]
        [TestCase("758e15")]
        public void TooShortPublishedValue_IsUnknown(string published)
        {
            Assert.That(
                ClientVersionGate.Evaluate("0.1.0+758e1586", published, out _),
                Is.EqualTo(Verdict.Unknown));
        }

        [Test]
        public void NonCommitLocalStamp_IsUnknown()
        {
            Assert.That(
                ClientVersionGate.Evaluate("0.1.0+local-dev", "758e1586", out _),
                Is.EqualTo(Verdict.Unknown));
        }
    }
}
