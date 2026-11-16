using BettingRoyale.Data.Dto;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 매니페스트 검증 규칙 회귀 (docs/infra/version-contract.md 3절, S15P21A606-74).
    /// </summary>
    public class RemoteManifestParserTests
    {
        // 2026-09-10 https://bettingroyale.p.example.invalid/manifest.json 실측값.
        private const string LiveDataVersion = "20260902-145805";
        private const string LiveDataFile = "data/20260902-145805/gamedata.bin";
        private const string LiveSha256 = "f342904bd32db996e35f0386f6d42a793de1aef0019aa347e5fb0181d1006a46";

        private static RemoteManifestDto LiveManifest() => new RemoteManifestDto
        {
            schemaVersion = 1,
            data = new RemoteManifestDataDto
            {
                version = LiveDataVersion,
                file = LiveDataFile,
                sha256 = LiveSha256,
                sizeBytes = 55,
            },
        };

        [Test]
        public void Validate_Succeeds_ForLiveManifestWithoutAssetBlock()
        {
            // 운영 매니페스트에는 asset 블록이 아직 없다. 표시용 라벨이라 부재가 정상이다 (계약 3절).
            var result = RemoteManifestParser.Validate(LiveManifest());

            Assert.That(result.IsSuccess, Is.True, result.Detail);
            Assert.That(result.Manifest.DataVersion, Is.EqualTo(LiveDataVersion));
            Assert.That(result.Manifest.DataFileRelativePath, Is.EqualTo(LiveDataFile));
            Assert.That(result.Manifest.DataSizeBytes, Is.EqualTo(55));
            Assert.That(result.Manifest.HasAssetVersionLabel, Is.False);
            Assert.That(result.Manifest.AssetVersionLabel, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Validate_ReadsAssetLabel_WhenAssetBlockIsPresent()
        {
            var dto = LiveManifest();
            dto.asset = new RemoteManifestAssetDto
            {
                version = "20260910-101500",
                buildTarget = "StandaloneWindows64",
            };

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.IsSuccess, Is.True, result.Detail);
            Assert.That(result.Manifest.HasAssetVersionLabel, Is.True);
            Assert.That(result.Manifest.AssetVersionLabel, Is.EqualTo("20260910-101500"));
        }

        [Test]
        public void Validate_TreatsEmptyAssetVersionAsAbsent()
        {
            // JsonUtility 는 JSON 에 없는 중첩 객체를 null 이 아니라 빈 인스턴스로 채운다.
            // 그 상태가 "라벨 있음" 으로 새지 않아야 한다.
            var dto = LiveManifest();
            dto.asset = new RemoteManifestAssetDto();

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.IsSuccess, Is.True, result.Detail);
            Assert.That(result.Manifest.HasAssetVersionLabel, Is.False);
        }

        [Test]
        public void Validate_Fails_WhenPayloadIsNull()
        {
            var result = RemoteManifestParser.Validate(null);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.EqualTo(RemoteManifestError.EmptyPayload));
            Assert.That(result.Manifest, Is.Null);
        }

        [Test]
        public void Validate_Fails_ForUnknownSchemaVersion()
        {
            var dto = LiveManifest();
            dto.schemaVersion = 2;

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.Error, Is.EqualTo(RemoteManifestError.UnsupportedSchemaVersion));
        }

        [Test]
        public void Validate_Fails_WhenDataBlockIsAnEmptyInstance()
        {
            // JsonUtility 함정의 본체 — data 키가 없는 JSON 도 null 이 아닌 인스턴스가 된다.
            var dto = LiveManifest();
            dto.data = new RemoteManifestDataDto();

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.Error, Is.EqualTo(RemoteManifestError.MissingDataBlock));
        }

        [Test]
        public void Validate_Fails_WhenDataBlockIsNull()
        {
            var dto = LiveManifest();
            dto.data = null;

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.Error, Is.EqualTo(RemoteManifestError.MissingDataBlock));
        }

        [TestCase("..")]
        [TestCase(".")]
        [TestCase("../../etc")]
        [TestCase("2026/09/02")]
        [TestCase("ver\\sion")]
        [TestCase("ver sion")]
        public void Validate_Fails_WhenDataVersionIsNotPathSafe(string version)
        {
            // 버전 문자열은 로컬 캐시 디렉터리 이름이 된다 (계약 4-2절).
            // 매니페스트는 신뢰 경계 밖에서 오므로 경로로 해석될 값을 통과시키지 않는다.
            var dto = LiveManifest();
            dto.data.version = version;

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.Error, Is.EqualTo(RemoteManifestError.InvalidDataVersion), result.Detail);
        }

        [Test]
        public void Validate_AcceptsCurrentVersionStampFormat()
        {
            // 계약은 형식을 강제하지 않지만(불투명 문자열) 현행 스탬프는 통과해야 한다.
            var dto = LiveManifest();
            dto.data.version = "20260902-145805";

            Assert.That(RemoteManifestParser.Validate(dto).IsSuccess, Is.True);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("https://evil.example.com/gamedata.bin")]
        [TestCase("data/../../gamedata.bin")]
        [TestCase("data\\20260902\\gamedata.bin")]
        [TestCase("data//gamedata.bin")]
        public void Validate_Fails_ForInvalidDataFilePath(string file)
        {
            var dto = LiveManifest();
            dto.data.file = file;

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.Error, Is.EqualTo(RemoteManifestError.InvalidDataFile), result.Detail);
        }

        [Test]
        public void Validate_StripsLeadingSlashFromDataFile()
        {
            var dto = LiveManifest();
            dto.data.file = "/" + LiveDataFile;

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.IsSuccess, Is.True, result.Detail);
            Assert.That(result.Manifest.DataFileRelativePath, Is.EqualTo(LiveDataFile));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("F342904BD32DB996E35F0386F6D42A793DE1AEF0019AA347E5FB0181D1006A46")]
        [TestCase("f342904bd32db996e35f0386f6d42a793de1aef0019aa347e5fb0181d1006a4")]
        [TestCase("f342904bd32db996e35f0386f6d42a793de1aef0019aa347e5fb0181d1006a46a")]
        [TestCase("g342904bd32db996e35f0386f6d42a793de1aef0019aa347e5fb0181d1006a46")]
        public void Validate_Fails_ForInvalidSha256(string sha256)
        {
            // 계약 3절이 소문자 hex 로 규정한다 — 대문자는 비교가 조용히 실패하므로 여기서 막는다.
            var dto = LiveManifest();
            dto.data.sha256 = sha256;

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.Error, Is.EqualTo(RemoteManifestError.InvalidDataSha256), result.Detail);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Validate_Fails_ForNonPositiveSize(long sizeBytes)
        {
            var dto = LiveManifest();
            dto.data.sizeBytes = sizeBytes;

            var result = RemoteManifestParser.Validate(dto);

            Assert.That(result.Error, Is.EqualTo(RemoteManifestError.InvalidDataSize));
        }
    }
}
