using System;

namespace BettingRoyale.Data.Dto
{
    // ─────────────────────────────────────────────────────────────
    //  원격 콘텐츠 매니페스트(manifest.json)의 유선 형태.
    //  정본은 docs/infra/version-contract.md 3절 (D-REL-Y4RH6K, legacy D-068).
    //
    //  ⚠️ 필드 이름이 곧 계약이다. JsonUtility 는 이름으로만 맞추므로 바꾸면 조용히 기본값이 된다.
    //     계약과 이름을 맞추기 위해 public 필드를 쓴다 — lint-rules.ps1 이 이 폴더를 예외로 둔다.
    //
    //  ⚠️ JsonUtility 는 JSON 에 없는 중첩 객체를 null 로 두지 않고 빈 인스턴스로 채운다.
    //     그래서 "블록이 있는가" 를 null 검사로 판정하지 않는다 — 필수 문자열이 비었는지로 판정하고,
    //     그 판정은 RemoteManifestParser 한 곳에만 둔다.
    //
    //  실측 (2026-09-10, https://bettingroyale.p.example.invalid/manifest.json): 운영 매니페스트에는
    //  data 블록만 있고 asset 블록이 아직 없다. asset.version 은 표시·운영용 라벨이라
    //  클라 로직이 이 값으로 분기하지 않으므로(계약 3절) 부재를 정상으로 취급한다.
    // ─────────────────────────────────────────────────────────────

    /// <summary>매니페스트 루트 (계약 3절).</summary>
    [Serializable]
    public sealed class RemoteManifestDto
    {
        public int schemaVersion;
        public RemoteManifestAssetDto asset;
        public RemoteManifestDataDto data;
    }

    /// <summary>
    /// Asset 블록. <c>version</c> 은 배포 이력 추적·QA 확인용 라벨이며 전환 판단에 쓰지 않는다
    /// — 실제 에셋 전환은 Addressables 카탈로그가 담당한다 (계약 2·3절).
    /// </summary>
    [Serializable]
    public sealed class RemoteManifestAssetDto
    {
        public string version;
        public string buildTarget;
    }

    /// <summary>Data 블록. <c>version</c> 이 Data 전환의 기준값이다 (계약 3절).</summary>
    [Serializable]
    public sealed class RemoteManifestDataDto
    {
        public string version;
        public string file;
        public string sha256;
        public long sizeBytes;
    }
}
