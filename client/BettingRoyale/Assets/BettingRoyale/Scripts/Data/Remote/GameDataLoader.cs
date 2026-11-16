using System;
using System.Collections.Generic;
using System.Globalization;
using BettingRoyale.Data.Blessing;
using MessagePack;

namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// <c>gamedata.bin</c> 을 읽어 도메인 타입으로 옮긴다 (S15P21A606-201, D-CLI-ETYTBX).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 클래스가 유선 타입과 도메인 타입의 유일한 경계다.</b> <see cref="GameData"/> 계열이
    /// 게임 코드로 새어 나가지 않게 한다.
    /// </para>
    /// <para>
    /// 파일을 직접 열지 않고 <b>바이트를 받는다</b> — <see cref="UnityEngine"/> 과 파일 시스템에
    /// 의존하지 않아야 <c>Scripts/Data/</c> 규칙(D-060)을 지키고 테스트에서 그대로 부를 수 있다.
    /// 경로에서 바이트를 읽는 것은 Managers 레이어의 몫이다.
    /// </para>
    /// </remarks>
    public static class GameDataLoader
    {
        /// <summary>이 클라이언트가 읽을 수 있는 스키마 버전.</summary>
        public const int SupportedSchemaVersion = 3;

        /// <summary>엑셀 <c>duration_kind</c> 에서 직접 눌러 쓰는 축복을 뜻하는 값(S-GD-NJQ5NM §4).</summary>
        public const string ActiveDurationKind = "Active";

        /// <summary>
        /// 바이트를 도메인 타입으로 복원한다. <b>예외를 던지지 않는다</b> — 실패는 결과로 돌려준다.
        /// </summary>
        public static GameDataLoadResult Load(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return GameDataLoadResult.Fail("gamedata.bin 이 비어 있습니다.");
            }

            GameData wire;
            try
            {
                wire = MessagePackSerializer.Deserialize<GameData>(payload);
            }
            catch (Exception ex)
            {
                // 손상된 파일·포맷 불일치가 여기로 온다. 무결성(sha256)은 RemoteContentBootstrap 이
                // 이미 확인했으므로, 여기까지 왔는데 실패하면 포맷 쪽 문제다.
                return GameDataLoadResult.Fail($"역직렬화 실패: {ex.GetType().Name}: {ex.Message}");
            }

            if (wire == null)
            {
                return GameDataLoadResult.Fail("역직렬화 결과가 null 입니다.");
            }

            // 상위 버전은 거부한다. 모르는 Key 를 MessagePack 이 조용히 건너뛰므로 계속 진행할 수도
            // 있지만, 그러면 **새로 생긴 규칙을 모른 채 플레이**하게 된다 — 축복이 조용히 빠지는 편이
            // 화면에 "업데이트가 필요합니다" 를 띄우는 것보다 나쁘다.
            if (wire.SchemaVersion > SupportedSchemaVersion)
            {
                return GameDataLoadResult.Fail(
                    $"지원하지 않는 상위 스키마입니다 (파일 v{wire.SchemaVersion} > 클라이언트 v{SupportedSchemaVersion}). 클라이언트를 업데이트하세요.");
            }

            var warnings = new List<string>();
            var blessings = BuildBlessings(wire.Blessings, warnings);
            var stats = BuildStats(wire.Stats, warnings);
            var effects = BuildEffects(wire.Blessings, warnings);
            var constants = BuildConstants(wire.Constants, warnings);
            var behaviors = BuildBehaviors(wire.Blessings, blessings, warnings);
            // v3 밸런스 표 → 카탈로그. 표가 없거나(v2) 규칙에 어긋나면 null 이고 소비자는 코드 대역으로 떨어진다.
            var balance = BettingRoyale.Data.Balance.BalanceCatalogBuilder.Build(wire, stats, constants, warnings);

            var content = new GameContent(
                wire.SchemaVersion,
                wire.Weapons ?? Array.Empty<WeaponRow>(),
                blessings,
                stats,
                effects,
                constants,
                behaviors,
                balance);
            return GameDataLoadResult.Ok(content, warnings);
        }

        // ── 축복 ──────────────────────────────────────────────────────────────
        // 엑셀은 tier·tag 를 **문자열**로 싣는다 (D-CLI-ETYTBX: enum 숫자를 데이터에 박지 않는다).
        // 여기서 이름으로 되돌린다. 값 집합은 파이프라인 빌드가 이미 검증했지만, 그 검증을 통과한
        // 바이너리만 온다고 가정하지 않는다 — 옛 버전 파일이 캐시에 남아 있을 수 있다.
        private static IReadOnlyList<BlessingDefinition> BuildBlessings(
            BlessingRow[] rows, List<string> warnings)
        {
            if (rows == null || rows.Length == 0)
            {
                warnings.Add("축복 행이 0개입니다.");
                return Array.Empty<BlessingDefinition>();
            }

            var result = new List<BlessingDefinition>(rows.Length);
            foreach (BlessingRow row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id))
                {
                    warnings.Add("id 가 빈 축복 행을 건너뜁니다.");
                    continue;
                }

                if (!Enum.TryParse(row.Tier, ignoreCase: false, out BlessingTier tier)
                    || tier < BlessingTier.Silver || tier > BlessingTier.Platinum)
                {
                    warnings.Add($"{row.Id}: 모르는 등급 '{row.Tier}' — 건너뜁니다.");
                    continue;
                }

                if (!Enum.TryParse(row.Tag, ignoreCase: false, out WeaponTag tag)
                    || !BlessingWeaponTagCatalog.IsDefined(tag))
                {
                    // 태그를 임의로 Common 으로 떨어뜨리지 않는다 — 모든 무기에 뜨게 되어
                    // 밸런스가 조용히 달라진다. 빼고 경고하는 쪽이 낫다.
                    warnings.Add($"{row.Id}: 모르는 무기 태그 '{row.Tag}' — 건너뜁니다.");
                    continue;
                }

                // 설명문은 틀이다 — {key}·{key:pct} 를 이 축복의 파라미터로 채워야 화면에 숫자가 보인다.
                // 채우지 못한 자리는 원문을 남기고 경고로 올린다(BlessingDescriptionTemplate).
                string descriptionTemplate = row.DescriptionTemplate;
                // 이전 배포 데이터/캐시를 읽어도 옛 화살비(발수×고정 피해) 설명을 노출하지 않는다 (S-GD-ND8JZ5).
                if (row.Id == "rain-check-t3" && descriptionTemplate != null
                    && (descriptionTemplate.Contains("{damage}") || descriptionTemplate.Contains("조준한 자리")))
                    descriptionTemplate = "화살이 적 또는 벽에 맞으면 {delay}초 후 그 위치 반경 {radius}m 안의 적 모두에게 기본 화살 피해를 준다. "
                        + "치명타와 화살 적중 효과가 적용된다. 첫 적중부터 재사용 대기 {cooldown}초. 빗나가면 소모하지 않으며, "
                        + "같은 공격의 추가 화살과 관통 적중에도 발동한다.";
                string description = BlessingDescriptionTemplate.Render(
                    descriptionTemplate, ParameterMap(row.Parameters), warnings, row.Id);
                // S-GD-T266ZJ: 거리 자체가 동적 공격 사거리 계약이므로 과거 데이터의 고정 m 표기를 대체한다.
                if (row.Id == "close-quarters-t3")
                    description = System.Text.RegularExpressions.Regex.Replace(description,
                        @"\d+(?:\.\d+)?\s*m", "현재 공격 사거리");

                // 직접 눌러 쓰는 축복은 한 번에 하나만 가진다(S-GD-NJQ5NM) — 뽑기가 이 표시로 거른다.
                result.Add(new BlessingDefinition(row.Id, row.Name, tag, tier, description,
                    isActive: string.Equals(row.DurationKind, ActiveDurationKind, StringComparison.Ordinal)));
            }

            return result;
        }

        /// <summary>
        /// 축복마다 <b>전투가 읽는 동작 정의</b>(효과 이름 + 숫자 파라미터)를 만든다 — 코드 목록(MVP)에 없는
        /// 엑셀 전용 축복도 전투 코드가 효과 이름으로 찾아 쓸 수 있게 한다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 숫자가 아닌 파라미터(<c>compare_mode=HP_RATIO</c> 등)는 빼고, <c>true</c>/<c>false</c> 는 1/0 으로 넣는다.
        /// </para>
        /// <para>
        /// <b>효과 이름이 코드 축복의 효과와 겹치면 만들지 않는다.</b> 그 효과의 코드가 이 축복의 파라미터를 읽다가
        /// 없는 키에서 멈추기 때문이다(엑셀 <c>interest-burst-t1</c> 이 코드의 <c>interest-burst</c> 와 동작이 다르다).
        /// 엑셀에서 효과 이름을 새로 지어야 한다 — 경고로 알린다.
        /// </para>
        /// </remarks>
        /// <summary>
        /// 코드 축복의 엑셀 <c>effect</c> 가 코드가 쓰는 효과 이름과 다르면 적는다 (D-CLI-VMX6AN).
        /// </summary>
        /// <remarks>
        /// 이 열은 <b>엑셀에만 있는 축복</b>의 동작을 고르는 유일한 기준이다. 코드에도 있는 축복에서는
        /// 읽지 않으므로, 두 이름이 갈리면 엑셀만 보고 판단하는 사람이 없는 동작을 기대하게 된다.
        /// </remarks>
        private static void WarnOnIgnoredEffectName(BlessingRow row, List<string> warnings)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.Effect)
                || !BlessingMvpCatalog.TryGet(row.Id ?? string.Empty, out BlessingMvpDefinition code))
            {
                return;
            }

            string excel = row.Effect.Trim();
            if (string.Equals(excel, code.Effect, StringComparison.Ordinal))
            {
                return;
            }

            warnings.Add($"{row.Id}: 엑셀 효과 이름 '{excel}' 는 쓰이지 않습니다 — 코드 축복이라 "
                + $"'{code.Effect}' 로 동작합니다. 둘 중 하나를 맞춰 주세요.");
        }

        private static IReadOnlyDictionary<string, BlessingMvpDefinition> BuildBehaviors(
            BlessingRow[] rows, IReadOnlyList<BlessingDefinition> blessings, List<string> warnings)
        {
            var result = new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal);
            if (rows == null)
            {
                return result;
            }

            var definitions = new Dictionary<string, BlessingDefinition>(StringComparer.Ordinal);
            foreach (BlessingDefinition definition in blessings)
            {
                definitions[definition.Id] = definition;
            }

            var codeEffects = new HashSet<string>(StringComparer.Ordinal);
            foreach (BlessingMvpDefinition entry in BlessingMvpCatalog.All)
            {
                codeEffects.Add(entry.Effect);
            }

            foreach (BlessingRow row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Effect)
                    || !definitions.TryGetValue(row.Id ?? string.Empty, out BlessingDefinition definition)
                    || BlessingMvpCatalog.ContainsCode(row.Id))
                {
                    // 코드에도 있는 축복은 id 로 동작을 찾는다 — 엑셀 effect 를 고쳐도 반영되지 않는다.
                    // 그 사실을 모르고 고치면 "바꿨는데 아무 일도 안 일어난다"가 되므로 어긋남을 드러낸다
                    // (D-CLI-VMX6AN). 막지는 않는다 — 동작은 코드 쪽이 옳다.
                    WarnOnIgnoredEffectName(row, warnings);
                    continue;
                }

                string effect = row.Effect.Trim();
                if (codeEffects.Contains(effect))
                {
                    warnings.Add($"{row.Id}: 효과 이름 '{effect}' 가 코드 축복의 효과와 겹칩니다 — 동작이 달라 쓰지 않습니다. "
                        + "엑셀에서 효과 이름을 새로 지어 주세요.");
                    continue;
                }

                var keys = new List<string>();
                var values = new List<float>();
                // 숫자가 아닌 값(compare_mode=HP_RATIO 등)은 **버리지 않는다** — 동작을 고르는 이름이라
                // 여기서 떨어뜨리면 전투 코드가 기획이 정한 분기를 아예 보지 못한다.
                var texts = new List<KeyValuePair<string, string>>();
                if (row.Parameters != null)
                {
                    foreach (KeyValueRow pair in row.Parameters)
                    {
                        if (pair == null || string.IsNullOrWhiteSpace(pair.Key) || keys.Contains(pair.Key))
                        {
                            continue;
                        }

                        if (TryParseBehaviorNumber(pair.Value, out float number))
                        {
                            keys.Add(pair.Key);
                            values.Add(number);
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(pair.Value)
                            && !texts.Exists(existing => existing.Key == pair.Key))
                        {
                            texts.Add(new KeyValuePair<string, string>(pair.Key, pair.Value.Trim()));
                        }
                    }
                }

                if (!Enum.TryParse(row.StackRule, ignoreCase: false, out BlessingStackRule stack))
                {
                    stack = BlessingStackRule.Unique;
                }

                if (!Enum.TryParse(row.DurationKind, ignoreCase: false, out BlessingDurationKind duration))
                {
                    duration = BlessingDurationKind.Instant;
                }

                try
                {
                    result[row.Id] = new BlessingMvpDefinition(definition, effect, BlessingMvpDefinition.MissingIconIndex,
                        stack, duration, keys, values, statTokens: null, parameterTexts: texts);
                }
                catch (ArgumentException ex)
                {
                    warnings.Add($"{row.Id}: 동작 정의를 만들지 못했습니다 — {ex.Message}");
                }
            }

            return result;
        }

        private static bool TryParseBehaviorNumber(string raw, out float number)
        {
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
            {
                number = 1f;
                return true;
            }

            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            {
                number = 0f;
                return true;
            }

            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !float.IsNaN(number) && !float.IsInfinity(number);
        }

        /// <summary>파라미터 쌍을 키 → 값으로 편다. 같은 키가 두 번이면 먼저 온 것을 쓴다.</summary>
        private static IReadOnlyDictionary<string, string> ParameterMap(KeyValueRow[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (pairs == null)
            {
                return map;
            }

            foreach (KeyValueRow pair in pairs)
            {
                if (pair != null && !string.IsNullOrEmpty(pair.Key) && !map.ContainsKey(pair.Key))
                {
                    map.Add(pair.Key, pair.Value);
                }
            }

            return map;
        }

        // ── 스탯 ──────────────────────────────────────────────────────────────
        private static StatRegistry BuildStats(StatRow[] rows, List<string> warnings)
        {
            if (rows == null || rows.Length == 0)
            {
                warnings.Add("스탯 행이 0개입니다.");
                return new StatRegistry(Array.Empty<StatDefinition>());
            }

            var result = new List<StatDefinition>(rows.Length);
            foreach (StatRow row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id))
                {
                    warnings.Add("id 가 빈 스탯 행을 건너뜁니다.");
                    continue;
                }

                if (!Enum.TryParse(row.Kind, ignoreCase: true, out StatKind kind))
                {
                    warnings.Add($"스탯 {row.Id}: 모르는 kind '{row.Kind}' — 건너뜁니다.");
                    continue;
                }

                if (row.Min.HasValue && row.Max.HasValue && row.Min.Value > row.Max.Value)
                {
                    warnings.Add($"스탯 {row.Id}: min({row.Min}) > max({row.Max}) — 건너뜁니다.");
                    continue;
                }

                // base_value 가 비면 **그 계산 방식의 항등원**이다 — 0 이 아니다.
                // StatDefinition 이 float? 을 받지 않으므로 여기서 정한다.
                result.Add(new StatDefinition(
                    row.Id, row.DisplayName, row.BaseValue ?? IdentityBaseOf(kind), kind, row.Min, row.Max));
            }

            return new StatRegistry(result);
        }

        /// <summary>
        /// <c>base_value</c> 가 빈 칸일 때 쓸 기본값 — <b>계산 방식마다 다르다</b>
        /// (<see cref="StatSheet"/> 의 합산 규칙).
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>전부 0 으로 두면 게임이 멈춘다.</b> 계산식이 <c>Flat=base+Σ</c> ·
        /// <c>Multiplier=base×(1+Σ)×Π</c> · <c>Max=max(base,…)</c> · <c>Min=min(base,…)</c> 라서
        /// 0 은 덧셈에서만 "없음" 이다.
        /// </para>
        /// <para>
        /// 실제로 엑셀 <c>stats</c> 시트에서 <c>damage-taken-fixed</c>·<c>damage-taken-cap</c> 의
        /// <c>base_value</c> 가 비어 있다. 둘 다 <c>Min</c> 이라 0 으로 읽으면
        /// <c>DamagePipeline</c> 이 <b>모든 피해를 0 으로 덮어써 아무도 맞지 않는다.</b>
        /// <c>attack-range</c> 도 비어 있고 <c>Multiplier</c> 라 0 이면 사거리가 사라진다.
        /// </para>
        /// </remarks>
        private static float IdentityBaseOf(StatKind kind) => kind switch
        {
            StatKind.Flat => 0f,                        // 덧셈 항등원
            StatKind.Multiplier => 1f,                  // 곱셈 항등원
            StatKind.Max => float.NegativeInfinity,     // max 항등원
            StatKind.Min => float.PositiveInfinity,     // min 항등원 — 하드코딩 정의도 이 값을 쓴다
            _ => 0f,
        };

        // ── 축복 계수 ─────────────────────────────────────────────────────────
        // 엑셀에 effects 시트가 따로 없다 — parameters 열에 `k=v;k=v` 로 패킹돼 있고 파이프라인이
        // 쌍으로 풀어 싣는다. 그 쌍 하나가 BlessingEffectRow 한 줄이 된다.
        //
        // Stack·Condition 은 지금 데이터에 없다. BlessingEffectRow 가 기본값(빈 문자열)을 허용하므로
        // 비워 둔다 — 없는 값을 지어내지 않는다.
        private static BlessingEffectCatalog BuildEffects(BlessingRow[] rows, List<string> warnings)
        {
            if (rows == null || rows.Length == 0)
            {
                return new BlessingEffectCatalog(Array.Empty<BlessingEffectRow>());
            }

            var result = new List<BlessingEffectRow>();
            foreach (BlessingRow row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || row.Parameters == null)
                {
                    continue;
                }

                foreach (KeyValueRow pair in row.Parameters)
                {
                    if (pair == null || string.IsNullOrWhiteSpace(pair.Key))
                    {
                        warnings.Add($"{row.Id}: 키가 빈 파라미터를 건너뜁니다.");
                        continue;
                    }

                    result.Add(new BlessingEffectRow(row.Id, pair.Key, pair.Value));
                }
            }

            return new BlessingEffectCatalog(result);
        }

        // ── 상수 ──────────────────────────────────────────────────────────────
        private static IReadOnlyDictionary<string, string> BuildConstants(
            ConstantRow[] rows, List<string> warnings)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (rows == null)
            {
                return result;
            }

            foreach (ConstantRow row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Key))
                {
                    continue;
                }

                if (result.ContainsKey(row.Key))
                {
                    // 파이프라인이 중복을 막지만 옛 바이너리가 캐시에 남을 수 있다. 먼저 온 값을 지킨다.
                    warnings.Add($"상수 {row.Key} 가 중복입니다 — 처음 값을 유지합니다.");
                    continue;
                }

                result.Add(row.Key, row.Value ?? string.Empty);
            }

            return result;
        }

        /// <summary>
        /// 상수를 숫자로 읽는다. 값이 없거나 숫자가 아니면 <paramref name="fallback"/> 을 돌려준다.
        /// <b>문화권 무관</b>으로 파싱한다 — 저장은 항상 InvariantCulture 형식이다.
        /// </summary>
        public static float ConstantOrDefault(
            IReadOnlyDictionary<string, string> constants, string key, float fallback)
        {
            if (constants == null || key == null || !constants.TryGetValue(key, out string raw))
            {
                return fallback;
            }

            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }
    }
}
