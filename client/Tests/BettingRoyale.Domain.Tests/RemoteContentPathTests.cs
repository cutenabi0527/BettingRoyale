using System.IO;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// URL·캐시 경로 규약 회귀 (docs/infra/version-contract.md 2·4-2절, S15P21A606-74).
    /// </summary>
    public class RemoteContentPathTests
    {
        // 구분자 없는 단일 세그먼트를 쓴다. 리터럴 경로를 쓰면 Path 가 플랫폼 구분자로
        // 정규화해 Windows·macOS 에서 기대값이 갈린다 (실제로 그렇게 한 번 깨졌다).
        private const string PersistentRoot = "persistent-root";

        [TestCase("https://bettingroyale.p.example.invalid")]
        [TestCase("https://bettingroyale.p.example.invalid/")]
        public void GetManifestUrl_IsStableRegardlessOfTrailingSlash(string baseUrl)
        {
            Assert.That(
                RemoteContentUrl.GetManifestUrl(baseUrl),
                Is.EqualTo("https://bettingroyale.p.example.invalid/manifest.json"));
        }

        [Test]
        public void Combine_JoinsBaseAndRelativeWithSingleSlash()
        {
            Assert.That(
                RemoteContentUrl.Combine("http://localhost:8000/", "/data/v1/gamedata.bin"),
                Is.EqualTo("http://localhost:8000/data/v1/gamedata.bin"));
        }

        [Test]
        public void ManifestFileName_IsTheOnlyHardcodedRemoteAddress()
        {
            // 계약 1절 4번 — 우리 코드가 주소를 아는 파일은 manifest.json 하나다.
            Assert.That(RemoteContentUrl.ManifestFileName, Is.EqualTo("manifest.json"));
        }

        [Test]
        public void DownloadTempPath_LivesOutsideAnyVersionDirectory()
        {
            // 계약 4-2절 — 검증을 통과하지 못한 파일은 임시 경로 밖으로 나오지 않는다.
            // 임시 파일이 버전 디렉터리 안에 있으면 그 보장이 경로에서 이미 깨진다.
            var tempPath = RemoteDataCacheLayout.GetDownloadTempPath(PersistentRoot);
            var versionDirectory = RemoteDataCacheLayout.GetVersionDirectory(PersistentRoot, "20260902-145805");

            Assert.That(Path.GetDirectoryName(tempPath), Is.Not.EqualTo(versionDirectory));
            Assert.That(
                Path.GetDirectoryName(tempPath),
                Is.EqualTo(RemoteDataCacheLayout.GetDataRoot(PersistentRoot)));
        }

        [Test]
        public void DataFilePath_IsVersionDirectoryPlusContractFileName()
        {
            var dataFile = RemoteDataCacheLayout.GetDataFilePath(PersistentRoot, "20260902-145805");
            var versionDirectory = RemoteDataCacheLayout.GetVersionDirectory(PersistentRoot, "20260902-145805");

            Assert.That(Path.GetDirectoryName(dataFile), Is.EqualTo(versionDirectory));
            Assert.That(Path.GetFileName(dataFile), Is.EqualTo("gamedata.bin"));
        }

        [Test]
        public void VersionDirectory_IsNamedByVersionAlone()
        {
            // 버전 마커 파일을 두지 않는 근거 — 디렉터리 이름이 곧 버전이다.
            var versionDirectory = RemoteDataCacheLayout.GetVersionDirectory(PersistentRoot, "20260902-145805");

            Assert.That(Path.GetFileName(versionDirectory), Is.EqualTo("20260902-145805"));
            Assert.That(
                Path.GetDirectoryName(versionDirectory),
                Is.EqualTo(RemoteDataCacheLayout.GetDataRoot(PersistentRoot)));
        }
    }
}
