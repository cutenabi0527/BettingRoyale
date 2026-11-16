using System;
using MessagePack;

namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// <c>gamedata.bin</c> 의 루트 타입 (version-contract §3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 파일이 스키마의 유일한 정본이다.</b> 파이프라인(<c>data/tools/DataContracts</c>)이 이 파일을
    /// <c>&lt;Compile Include&gt;</c> 로 링크해 같은 타입으로 쓴다 — 복사본이 아니다
    /// (<c>client/Tests</c> 가 이미 쓰는 방식, D-060). 손으로 복사하면 <see cref="KeyAttribute"/> 번호가
    /// 어긋나는 순간 <b>조용히 잘못된 값이 읽힌다.</b>
    /// </para>
    /// <para>
    /// 직렬화는 MessagePack 이며 IL2CPP 대응은 v3 소스 제너레이터가 한다 (D-CLI-ETYTBX).
    /// <b>모든 <see cref="MessagePackObjectAttribute"/> 타입은 <c>partial</c> 이어야 한다</b> —
    /// 생성된 포매터가 private/protected 멤버에 접근하기 위한 v3 요구사항이다.
    /// </para>
    /// <para>
    /// 필드 추가는 새 Key 번호로만 한다. <b>기존 Key 번호·타입은 바꾸지 않는다</b> — 이미 배포된
    /// 바이너리를 읽을 수 없게 된다.
    /// </para>
    /// <para>
    /// <see cref="UnityEngine"/> 을 참조하지 않는다 (D-060). 그래야 파이프라인의 <c>net8.0</c> 에서도
    /// 그대로 컴파일된다.
    /// </para>
    /// </remarks>
    [MessagePackObject]
    public sealed partial class GameData
    {
        /// <summary>
        /// v1 = Weapons 만. v2 = 축복·스탯·상수 추가 (S15P21A606-133).
        /// v3 = 밸런스 표(<see cref="Tables"/>) 추가 — 강화·아이템·투척·상자·시설·룰렛·자기장 (S15P21A606-274, S-CLI-QXRTWX).
        /// </summary>
        [Key(0)] public int SchemaVersion { get; set; } = 3;
        [Key(1)] public WeaponRow[] Weapons { get; set; } = Array.Empty<WeaponRow>();
        [Key(2)] public BlessingRow[] Blessings { get; set; } = Array.Empty<BlessingRow>();
        [Key(3)] public StatRow[] Stats { get; set; } = Array.Empty<StatRow>();
        [Key(4)] public ConstantRow[] Constants { get; set; } = Array.Empty<ConstantRow>();

        /// <summary>
        /// 엑셀의 나머지 밸런스 시트를 <b>시트 한 행 = 열 이름·값 쌍 묶음</b>으로 싣는다 (v3).
        /// 시트마다 유선 타입을 새로 만들지 않는 이유는 <b>Key 번호가 append-only</b> 라서다 —
        /// 시트가 늘 때마다 루트 Key 를 더하면 옛 클라이언트가 읽을 수 없는 바이너리가 쌓인다.
        /// 어떤 시트·열이 계약인지는 <c>BalanceTableSchema</c>(파이프라인·클라이언트 공용)가 정한다.
        /// </summary>
        [Key(5)] public BalanceTableRow[] Tables { get; set; } = Array.Empty<BalanceTableRow>();
    }

    /// <summary>밸런스 시트 한 행. <see cref="Sheet"/> 는 소문자 시트 이름, <see cref="Cells"/> 는 열 이름 → 원문 값이다.</summary>
    [MessagePackObject]
    public sealed partial class BalanceTableRow
    {
        [Key(0)] public string Sheet { get; set; } = "";
        [Key(1)] public KeyValueRow[] Cells { get; set; } = Array.Empty<KeyValueRow>();
    }

    /// <summary>무기 테이블 한 줄. 원본은 <c>data/source/weapons.xlsx</c> 의 <c>Weapons</c> 시트.</summary>
    [MessagePackObject]
    public sealed partial class WeaponRow
    {
        [Key(0)] public string WeaponId { get; set; } = "";
        [Key(1)] public string DisplayName { get; set; } = "";
        [Key(2)] public int Damage { get; set; }

        /// <summary>
        /// ⚠️ 폐기 후보. <c>constants.BASE_ATTACKS_PER_SECOND</c> 가 1 (CONFIRMED, "쿨타임 방식 폐기")이라
        /// 이 열과 의미가 겹친다. 존치 여부는 S15P21A606-133 의 기획 확인 항목이다.
        /// </summary>
        [Key(3)] public float CooldownSeconds { get; set; }

        /// <summary>검·둔기 = 부채꼴 사거리, 활 = 최대 비행거리 (m)</summary>
        [Key(4)] public float RangeMeters { get; set; }

        /// <summary>투사체 속도 (m/s). 근접 무기는 0.</summary>
        [Key(5)] public float ProjectileSpeedMps { get; set; }

        /// <summary>공격 직후 이동 감속 배율(0 = 없음). 활 0.4 (v3, S-CLI-QXRTWX).</summary>
        [Key(6)] public float AttackSlowMultiplier { get; set; }

        /// <summary>공격 직후 이동 감속 시간(초). 0 = 없음.</summary>
        [Key(7)] public float AttackSlowSeconds { get; set; }

        /// <summary>투사체 충돌 반경(m). 0 = 선 판정. 활 세트가 배율로 키운다 (S-GD-G3D2WP).</summary>
        [Key(8)] public float CollisionRadiusMeters { get; set; }
    }

    /// <summary>
    /// 축복 한 줄. 원본은 <c>BettingRoyale_Blessing.xlsx</c> 의 <c>blessings</c> 시트이며
    /// <b>시트 한 행과 1:1</b> 이다 — 축복별 타입으로 정형화하지 않는다.
    /// </summary>
    /// <remarks>
    /// <b><see cref="Tier"/>·<see cref="Tag"/>·<see cref="StackRule"/>·<see cref="DurationKind"/> 는
    /// enum 이 아니라 문자열이다.</b> enum 숫자를 데이터에 박으면 값 목록이 바뀔 때 이미 배포된 바이너리가 깨진다.
    /// </remarks>
    [MessagePackObject]
    public sealed partial class BlessingRow
    {
        [Key(0)] public string Id { get; set; } = "";
        [Key(1)] public string Name { get; set; } = "";

        /// <summary>Silver · Gold · Platinum. id 접미사 <c>-t1</c>/<c>-t2</c>/<c>-t3</c> 와 대응한다 (S-GD-ZQMB7Y).</summary>
        [Key(2)] public string Tier { get; set; } = "";

        /// <summary>Common · Melee · Ranged · Sword · Bow · Mace 의 3단 계층 (S-GD-DPWH9W).</summary>
        [Key(3)] public string Tag { get; set; } = "";

        [Key(4)] public int IconIndex { get; set; }

        /// <summary>효과 식별자. 게임 코드가 이 값으로 구현을 찾는다.</summary>
        [Key(5)] public string Effect { get; set; } = "";

        /// <summary>
        /// <c>parameters</c> 열의 <b>원문 그대로</b>. 파서가 나중에 틀렸다고 판명돼도
        /// 원문이 남아 있으면 소비 쪽에서 복구할 수 있다.
        /// </summary>
        [Key(6)] public string ParametersRaw { get; set; } = "";

        /// <summary><see cref="ParametersRaw"/> 를 <c>;</c>·첫 <c>=</c> 기준으로 나눈 결과. 순서를 보존한다.</summary>
        [Key(7)] public KeyValueRow[] Parameters { get; set; } = Array.Empty<KeyValueRow>();

        [Key(8)] public string VisualTokensRaw { get; set; } = "";
        [Key(9)] public KeyValueRow[] VisualTokens { get; set; } = Array.Empty<KeyValueRow>();

        [Key(10)] public string DescriptionTemplate { get; set; } = "";

        /// <summary>Unique · Stack · Refresh · Independent</summary>
        [Key(11)] public string StackRule { get; set; } = "";

        /// <summary>Permanent · Timed · WhileCondition · Instant · Round</summary>
        [Key(12)] public string DurationKind { get; set; } = "";

        /// <summary>등급 접미사를 뗀 계열 id (S-GD-ZQMB7Y).</summary>
        [Key(13)] public string SeriesId { get; set; } = "";

        /// <summary>
        /// READY · NEEDS_CONTRACT · NEEDS_STAT · NEEDS_SYSTEM.
        /// <b>빌드는 이 값으로 행을 거르지 않는다</b> — READY 만 실으면 무기별·등급별 후보 풀이 하한 아래로 떨어져
        /// <c>BlessingDraft</c> 가 예외를 던진다. 거르는 것은 클라이언트 설정의 몫이다.
        /// </summary>
        [Key(14)] public string ImplStatus { get; set; } = "";

        [Key(15)] public string Author { get; set; } = "";
        [Key(16)] public string Note { get; set; } = "";
        [Key(17)] public string SourceUrl { get; set; } = "";
    }

    /// <summary>스탯 정의 한 줄. 원본은 <c>stats</c> 시트.</summary>
    [MessagePackObject]
    public sealed partial class StatRow
    {
        [Key(0)] public string Id { get; set; } = "";
        [Key(1)] public string DisplayName { get; set; } = "";

        /// <summary>빈 칸은 <c>null</c> — "없음" 과 0 은 다르다.</summary>
        [Key(2)] public float? BaseValue { get; set; }

        [Key(3)] public string Kind { get; set; } = "";
        [Key(4)] public float? Min { get; set; }
        [Key(5)] public float? Max { get; set; }
        [Key(6)] public string Note { get; set; } = "";
    }

    /// <summary>
    /// 상수 한 줄. 원본은 <c>constants</c> 시트.
    /// <see cref="Value"/> 는 <b>문자열 고정</b> — 숫자·TRUE·구간 표기가 섞여 있어 수치 타입으로 좁힐 수 없다.
    /// </summary>
    [MessagePackObject]
    public sealed partial class ConstantRow
    {
        [Key(0)] public string Key { get; set; } = "";
        [Key(1)] public string Value { get; set; } = "";
        [Key(2)] public string Unit { get; set; } = "";

        /// <summary>CONFIRMED · ASSUMED 등. 기획의 확정 상태이며 빌드는 이 값으로 거르지 않는다.</summary>
        [Key(3)] public string Status { get; set; } = "";

        [Key(4)] public string Note { get; set; } = "";
    }

    /// <summary>순서를 보존하는 키·값 쌍. <c>Dictionary</c> 를 쓰지 않는 이유는 원본의 열거 순서가 의미를 갖기 때문이다.</summary>
    [MessagePackObject]
    public sealed partial class KeyValueRow
    {
        [Key(0)] public string Key { get; set; } = "";
        [Key(1)] public string Value { get; set; } = "";
    }
}
