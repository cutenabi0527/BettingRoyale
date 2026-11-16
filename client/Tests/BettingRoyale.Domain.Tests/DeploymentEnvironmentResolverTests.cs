using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// Base URL 하나로 배포 환경을 가른다 (S15P21A606-230, D-REL-NM9GTF).
    ///
    /// 이 판정이 틀리면 Preview 빌드가 Production 방 목록에 들어가고, 서로 다른 DB 의 계정이
    /// 한 매치에 섞인다. 그 어긋남은 런타임에 아무 오류도 내지 않으므로 여기서 막는다.
    /// </summary>
    [TestFixture]
    public class DeploymentEnvironmentResolverTests
    {
        [TestCase("https://bettingroyale.p.example.invalid")]
        [TestCase("https://bettingroyale.p.example.invalid/")]
        [TestCase("https://bettingroyale.P.example.invalid")]
        public void 맨_경로는_Production_이다(string baseUrl)
        {
            Assert.That(
                DeploymentEnvironmentResolver.Resolve(baseUrl),
                Is.EqualTo(DeploymentEnvironment.Production));
        }

        [TestCase("https://bettingroyale.p.example.invalid/preview")]
        [TestCase("https://bettingroyale.p.example.invalid/preview/")]
        [TestCase("https://bettingroyale.p.example.invalid/PREVIEW")]
        public void preview_프리픽스는_Preview_다(string baseUrl)
        {
            Assert.That(
                DeploymentEnvironmentResolver.Resolve(baseUrl),
                Is.EqualTo(DeploymentEnvironment.Preview));
        }

        [TestCase("http://localhost:8000")]
        [TestCase("http://127.0.0.1:8080")]
        [TestCase("https://example.invalid/api")]
        public void 모르는_주소는_Local_이다(string baseUrl)
        {
            // Production 으로 떨어뜨리지 않는다 — 개발 서버를 보면서 운영 방에 들어가는 쪽이
            // 훨씬 위험하다.
            Assert.That(
                DeploymentEnvironmentResolver.Resolve(baseUrl),
                Is.EqualTo(DeploymentEnvironment.Local));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void 주소가_비면_Production_이다(string baseUrl)
        {
            // RemoteConfig 가 기본값을 적용하기 전 상태이고, 그 기본값이 운영 주소다.
            Assert.That(
                DeploymentEnvironmentResolver.Resolve(baseUrl),
                Is.EqualTo(DeploymentEnvironment.Production));
        }

        [Test]
        public void 다른_호스트의_preview_도_Preview_다()
        {
            // 경로 프리픽스가 호스트보다 우선한다. 스테이징 호스트가 생겨도 규칙이 유지된다.
            Assert.That(
                DeploymentEnvironmentResolver.Resolve("http://localhost:8000/preview"),
                Is.EqualTo(DeploymentEnvironment.Preview));
        }

        // ── 캐시 키 (S15P21A606-231) ────────────────────────────────────────

        [Test]
        public void 캐시_키는_환경마다_다르다()
        {
            string production = DeploymentEnvironmentResolver.ResolveCacheKey("https://bettingroyale.p.example.invalid");
            string preview = DeploymentEnvironmentResolver.ResolveCacheKey("https://bettingroyale.p.example.invalid/preview");

            Assert.That(production, Is.EqualTo("production"));
            Assert.That(preview, Is.EqualTo("preview"));
            Assert.That(production, Is.Not.EqualTo(preview));
        }

        [Test]
        public void 로컬_서버는_주소마다_캐시가_갈린다()
        {
            string a = DeploymentEnvironmentResolver.ResolveCacheKey("http://localhost:8000");
            string b = DeploymentEnvironmentResolver.ResolveCacheKey("http://localhost:9000");

            Assert.That(a, Does.StartWith("local-"));
            Assert.That(b, Does.StartWith("local-"));
            Assert.That(a, Is.Not.EqualTo(b), "다른 로컬 서버가 같은 캐시를 밟으면 안 된다.");
        }

        [Test]
        public void 같은_주소는_항상_같은_캐시_키다()
        {
            // string.GetHashCode 를 쓰면 실행할 때마다 달라져 캐시가 매번 새로 생긴다.
            string first = DeploymentEnvironmentResolver.ResolveCacheKey("http://localhost:8000/");
            string second = DeploymentEnvironmentResolver.ResolveCacheKey("HTTP://LOCALHOST:8000");

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void 캐시_키는_경로에_쓸_수_있는_문자만_쓴다()
        {
            foreach (string url in new[]
                     {
                         "https://bettingroyale.p.example.invalid",
                         "https://bettingroyale.p.example.invalid/preview",
                         "http://localhost:8000",
                     })
            {
                string key = DeploymentEnvironmentResolver.ResolveCacheKey(url);
                Assert.That(
                    key.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()),
                    Is.EqualTo(-1),
                    $"{url} 의 캐시 키 '{key}' 에 폴더 이름으로 못 쓰는 문자가 있다.");
            }
        }

        [Test]
        public void previewer_는_preview_가_아니다()
        {
            // 조각 단위로 비교한다. 앞글자만 맞는 경로를 Preview 로 읽으면 조용히 환경이 바뀐다.
            Assert.That(
                DeploymentEnvironmentResolver.Resolve("https://bettingroyale.p.example.invalid/previewer"),
                Is.EqualTo(DeploymentEnvironment.Production));
        }
    }
}
