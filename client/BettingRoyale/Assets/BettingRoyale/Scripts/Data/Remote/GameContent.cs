using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;

namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// <c>gamedata.bin</c> 에서 복원한 게임 정본 데이터 묶음 (S15P21A606-201).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 여기 담기는 것은 <b>도메인 타입</b>이다 — 유선 타입(<see cref="GameData"/>)을 게임 코드에
    /// 그대로 흘리지 않는다. 경계에서 변환하는 규칙은 REST 어댑터와 같다
    /// (<c>docs/client/architecture.md</c> 8절).
    /// </para>
    /// <para>
    /// <b><see cref="Constants"/> 는 문자열 그대로 둔다.</b> 엑셀 <c>constants</c> 시트에 숫자·TRUE·
    /// 구간 표기가 섞여 있어 수치 타입으로 좁힐 수 없다. 해석은 그 값을 쓰는 쪽이 한다.
    /// </para>
    /// </remarks>
    public sealed class GameContent
    {
        public GameContent(
            int schemaVersion,
            IReadOnlyList<WeaponRow> weapons,
            IReadOnlyList<BlessingDefinition> blessings,
            StatRegistry stats,
            BlessingEffectCatalog effects,
            IReadOnlyDictionary<string, string> constants,
            IReadOnlyDictionary<string, BlessingMvpDefinition> behaviors = null,
            BettingRoyale.Data.Balance.BalanceCatalogs balance = null)
        {
            Balance = balance;
            SchemaVersion = schemaVersion;
            Weapons = weapons ?? Array.Empty<WeaponRow>();
            Blessings = blessings ?? Array.Empty<BlessingDefinition>();
            Stats = stats ?? new StatRegistry(Array.Empty<StatDefinition>());
            Effects = effects ?? new BlessingEffectCatalog(Array.Empty<BlessingEffectRow>());
            Constants = constants ?? new Dictionary<string, string>(StringComparer.Ordinal);
            Behaviors = behaviors ?? new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal);
        }

        public int SchemaVersion { get; }

        /// <summary>
        /// 무기 수치. <b>유선 타입 그대로 둔다</b> — 전투 쪽 도메인 타입이 아직 이 표를 소비하지 않으며,
        /// 쓰지 않는 변환을 미리 만들면 두 곳을 맞춰야 한다.
        /// </summary>
        public IReadOnlyList<WeaponRow> Weapons { get; }

        public IReadOnlyList<BlessingDefinition> Blessings { get; }

        public StatRegistry Stats { get; }

        public BlessingEffectCatalog Effects { get; }

        /// <summary>키 → 값(원문 문자열). 대소문자를 구분한다.</summary>
        public IReadOnlyDictionary<string, string> Constants { get; }

        /// <summary>
        /// <b>코드 목록(MVP)에 없는 엑셀 축복</b>의 동작 정의 — 엑셀 <c>effect</c> 이름과 숫자 파라미터.
        /// 전투는 <see cref="BlessingMvpCatalog.TryGet"/> 로 코드 축복과 똑같이 찾는다. 새 축복은 엑셀 한 줄 +
        /// 그 효과 이름의 전투 코드면 된다.
        /// </summary>
        public IReadOnlyDictionary<string, BlessingMvpDefinition> Behaviors { get; }

        /// <summary>
        /// 밸런스 카탈로그(v3 시트). <c>null</c> 이면 이 콘텐츠에 표가 없거나 만들지 못한 것이고,
        /// 소비자는 <c>BalanceCatalogs.Current</c> 를 통해 코드 대역으로 떨어진다.
        /// </summary>
        public BettingRoyale.Data.Balance.BalanceCatalogs Balance { get; }

        public override string ToString() =>
            $"v{SchemaVersion} · 무기 {Weapons.Count} / 축복 {Blessings.Count} / {Stats} / {Effects}";
    }
}
