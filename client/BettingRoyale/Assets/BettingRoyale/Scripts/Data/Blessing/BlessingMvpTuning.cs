using System;
using System.Collections.Generic;
using System.Globalization;
using BettingRoyale.Data.Remote;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 코드로 동작을 구현한 축복(<see cref="BlessingMvpCatalog"/>)에 <b>엑셀 수치</b>를 입힌다 (S-GD-RTB6H2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>축복 수치의 기준은 엑셀이다.</b> 사용자가 엑셀에서 직접 리밸런싱했다. 그런데 이 축복들의 동작 코드는
    /// <c>BlessingMvpData.cs</c> 에 박힌 수치(<c>client/Balance/blessings.csv</c> 에서 생성)를 읽고 있었다.
    /// 그래서 카드에는 엑셀 숫자가 보이는데 실제 전투는 옛 숫자로 돌았다(2026-09-18 대조: 47종 중 39종이 다름).
    /// </para>
    /// <para>
    /// <b>바꾸는 것은 수치뿐이다.</b> 효과 이름·아이콘·설명·토큰·<b>키 집합</b>은 코드 것을 그대로 쓴다.
    /// 엑셀에 없는 키는 코드 값을 유지한다 — 키를 지우면 <see cref="BlessingMvpDefinition.Number"/> 가 전투 중에
    /// 던진다. 그런 키는 <see cref="Audit"/> 가 알려 준다.
    /// </para>
    /// <para>
    /// <b>엑셀 키 → 코드 키</b>:
    /// <list type="bullet">
    /// <item>같은 이름이면 그대로.</item>
    /// <item>엑셀이 능력치 이름(하이픈)으로 적은 키는 밑줄로도 찾는다 — <c>armor-pen</c> → <c>armor_pen</c>.</item>
    /// <item><see cref="ExcelNameForCodeKey"/> 의 별칭 — 엑셀에서 <b>일부러 이름을 바꾼</b> 키.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>엑셀이 없으면 아무것도 바꾸지 않는다</b>(배치모드·EditMode 테스트). 판단은
    /// <see cref="BlessingBalance.IsFromGameData"/> 와 같다 — 스탯·계수와 다른 출처를 쓰면 안 된다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class BlessingMvpTuning
    {
        /// <summary>
        /// 코드 키 → 엑셀에서 쓰는 이름. <b>엑셀에서 일부러 이름을 바꾼 키만</b> 둔다.
        /// </summary>
        /// <remarks>
        /// <c>shield</c> 는 능력치 이름이라 엑셀에 그대로 두면 조건과 무관하게 <b>상시 보호막</b>으로 얹힌다.
        /// 그래서 엑셀은 조건부 보호막을 <c>shield_amount</c> 로 적는다(1차 엑셀 정리, 2026-09-18).
        /// 코드의 조건부 로직은 여전히 <c>shield</c> 를 읽으므로 여기서 잇는다.
        /// </remarks>
        private static readonly Dictionary<string, string> ExcelNameForCodeKey = new(StringComparer.Ordinal)
        {
            ["shield"] = "shield_amount",
        };

        private static GameContent tunedFor;
        private static readonly Dictionary<string, BlessingMvpDefinition> Tuned = new(StringComparer.Ordinal);

        /// <summary>
        /// 이 축복에 엑셀 수치를 입힌 것을 준다. 엑셀이 없거나 엑셀에 그 축복이 없으면 <paramref name="raw"/> 그대로다.
        /// </summary>
        /// <remarks>게임 데이터 한 벌마다 한 번만 만든다 — 전투 중 매 공격마다 불려도 새로 만들지 않는다.</remarks>
        public static BlessingMvpDefinition Apply(BlessingMvpDefinition raw)
        {
            if (raw == null || !BlessingBalance.IsFromGameData)
            {
                return raw;
            }

            GameContent content = GameContentProvider.Current;
            if (!ReferenceEquals(content, tunedFor))
            {
                Tuned.Clear();
                tunedFor = content;
            }

            string id = raw.Definition.Id;
            if (Tuned.TryGetValue(id, out BlessingMvpDefinition cached))
            {
                return cached;
            }

            IReadOnlyList<BlessingEffectRow> rows = content.Effects.For(id);
            Dictionary<string, float> replacements = ExcelNumbers(raw, rows, null);
            Dictionary<string, string> texts = ExcelTexts(raw, rows);
            BlessingMvpDefinition tuned = replacements.Count == 0 && texts.Count == 0
                ? raw
                : raw.WithNumbersAndTexts(replacements, texts);
            Tuned[id] = tuned;
            return tuned;
        }

        /// <summary>
        /// 지금 올라와 있는 엑셀과 코드 축복을 대조해 <b>엑셀 수치를 쓰지 못하는 곳</b>을 적는다.
        /// </summary>
        /// <remarks>
        /// 코드가 읽는 키가 엑셀에 없으면 그 키는 옛 값으로 돈다 — 카드 설명과 실제가 어긋날 수 있다.
        /// 기동 때 한 번 로그로 남겨 기획이 알 수 있게 한다. 엑셀이 없으면 빈 목록이다.
        /// </remarks>
        public static IReadOnlyList<string> Audit()
        {
            var problems = new List<string>();
            if (!BlessingBalance.IsFromGameData)
            {
                return problems;
            }

            BlessingEffectCatalog effects = GameContentProvider.Current.Effects;
            foreach (BlessingMvpDefinition raw in BlessingMvpCatalog.All)
            {
                string id = raw.Definition.Id;
                if (!effects.Contains(id))
                {
                    continue;
                }

                ExcelNumbers(raw, effects.For(id), problems);
            }

            return problems;
        }

        /// <summary>코드 키마다 엑셀 값을 찾는다. 못 찾으면 <paramref name="problems"/> 에 적는다.</summary>
        private static Dictionary<string, float> ExcelNumbers(
            BlessingMvpDefinition raw, IReadOnlyList<BlessingEffectRow> rows, List<string> problems)
        {
            var excel = new Dictionary<string, BlessingEffectRow>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                if (!excel.ContainsKey(rows[i].ParamKey))
                {
                    excel.Add(rows[i].ParamKey, rows[i]);
                }
            }

            var replacements = new Dictionary<string, float>(StringComparer.Ordinal);
            if (excel.Count == 0)
            {
                return replacements;
            }

            string id = raw.Definition.Id;
            foreach (string codeKey in raw.NumberKeys)
            {
                if (!TryFind(excel, codeKey, out BlessingEffectRow row))
                {
                    problems?.Add($"{id}: 코드가 쓰는 '{codeKey}' 가 엑셀에 없어 코드 값 {Format(raw.Number(codeKey))} 을 씁니다.");
                    continue;
                }

                if (!row.TryGetNumber(out float value) || float.IsNaN(value) || float.IsInfinity(value))
                {
                    problems?.Add($"{id}: 엑셀 '{row.ParamKey}' 값 '{row.Value}' 가 숫자가 아니어서 코드 값을 씁니다.");
                    continue;
                }

                replacements[codeKey] = value;
            }

            return replacements;
        }

        /// <summary>
        /// 엑셀의 숫자가 아닌 파라미터(<c>heal_basis=max_health</c>·<c>damage_mode=weapon_arrow</c>). 코드 축복은
        /// <c>BuildBehaviors</c> 를 거치지 않아 이 값이 그동안 런타임에 오지 않았다 — 여기서 합친다.
        /// 코드 수치 키와 같은 이름은 숫자 쪽이 우선이라 넣지 않는다.
        /// </summary>
        private static Dictionary<string, string> ExcelTexts(BlessingMvpDefinition raw, IReadOnlyList<BlessingEffectRow> rows)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                BlessingEffectRow row = rows[i];
                if (string.IsNullOrWhiteSpace(row.ParamKey) || string.IsNullOrWhiteSpace(row.Value)) continue;
                if (row.TryGetNumber(out _)) continue;
                if (string.Equals(row.Value, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Value, "false", StringComparison.OrdinalIgnoreCase)) continue;
                bool numberKey = false;
                foreach (string codeKey in raw.NumberKeys)
                    if (string.Equals(codeKey, row.ParamKey, StringComparison.Ordinal)) { numberKey = true; break; }
                if (!numberKey && !texts.ContainsKey(row.ParamKey)) texts[row.ParamKey] = row.Value.Trim();
            }

            return texts;
        }

        private static bool TryFind(
            Dictionary<string, BlessingEffectRow> excel, string codeKey, out BlessingEffectRow row)
        {
            if (excel.TryGetValue(codeKey, out row))
            {
                return true;
            }

            if (excel.TryGetValue(codeKey.Replace('_', '-'), out row))
            {
                return true;
            }

            return ExcelNameForCodeKey.TryGetValue(codeKey, out string alias) && excel.TryGetValue(alias, out row);
        }

        private static string Format(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
