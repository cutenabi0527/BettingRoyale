using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 설명 템플릿의 <c>{key}</c> · <c>{key:pct}</c> 를 그 축복의 파라미터 값으로 채운다 (S15P21A606-201).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 엑셀 <c>description_template</c> 은 <c>치명타 확률이 {crit-chance:pct} 오른다.</c> 같은 <b>틀</b>이다.
    /// 채우지 않고 넘기면 축복 카드에 중괄호가 그대로 보인다.
    /// </para>
    /// <para>
    /// <b>숫자 표기는 <c>scripts/generate-blessings.py</c> 와 같다</b> — 코드에 직접 적힌 60종의 설명문이 그
    /// 생성기로 만들어졌으므로 두 쪽이 같은 모양으로 보여야 한다.
    /// <list type="bullet">
    /// <item><c>{key}</c> — 값 그대로, 끝의 0 을 뗀다 (<c>2.20 → 2.2</c>, <c>3 → 3</c>)</item>
    /// <item><c>{key:pct}</c> — 100 을 곱해 <c>%</c> 를 붙인다 (<c>0.065 → 6.5%</c>, <c>1 → 100%</c>)</item>
    /// </list>
    /// 부동소수 오차가 끼지 않도록 <see cref="decimal"/> 로 계산한다 — <c>0.07 × 100</c> 이
    /// <c>7.000000000000001%</c> 로 보이면 안 된다.
    /// </para>
    /// <para>
    /// <b>생성기와 다른 점 하나</b> — 키에 하이픈을 받는다. 엑셀은 능력치 파라미터를 능력치 이름 그대로
    /// (<c>move-speed</c>) 적기 때문이다.
    /// </para>
    /// <para>
    /// <b>던지지 않는다.</b> 설명문 하나가 잘못됐다고 게임 데이터 전체를 못 쓰게 만들 수는 없다.
    /// 채울 수 없는 자리는 <b>원문 그대로 남기고</b> 문제를 <paramref name="problems"/> 에 적는다 —
    /// 화면에 중괄호가 보이면 기획이 바로 알아챈다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class BlessingDescriptionTemplate
    {
        private static readonly Regex Placeholder = new(@"\{([^{}]*)\}", RegexOptions.CultureInvariant);

        private static readonly Regex Body =
            new(@"^([a-z][a-z0-9_-]*)(?::(pct))?$", RegexOptions.CultureInvariant);

        /// <summary>템플릿을 채운다.</summary>
        /// <param name="template">설명 템플릿. <c>null</c> 이면 빈 문자열이다.</param>
        /// <param name="values">파라미터 키 → 원문 값.</param>
        /// <param name="problems">채우지 못한 자리를 적을 곳. <c>null</c> 이면 적지 않는다.</param>
        /// <param name="where">문제 메시지 앞에 붙일 위치 표시(보통 축복 id).</param>
        public static string Render(
            string template,
            IReadOnlyDictionary<string, string> values,
            ICollection<string> problems = null,
            string where = "")
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var text = new StringBuilder(template.Length + 16);
            int cursor = 0;

            foreach (Match match in Placeholder.Matches(template))
            {
                text.Append(template, cursor, match.Index - cursor);
                text.Append(RenderOne(match, values, problems, where));
                cursor = match.Index + match.Length;
            }

            text.Append(template, cursor, template.Length - cursor);

            string leftovers = Placeholder.Replace(template, string.Empty);
            if (leftovers.IndexOf('{') >= 0 || leftovers.IndexOf('}') >= 0)
            {
                problems?.Add($"{where}: 설명문에 짝이 맞지 않는 중괄호가 있습니다.");
            }

            return text.ToString();
        }

        /// <summary>
        /// 값 그대로, 끝의 0 을 뗀다. <c>2.20 → "2.2"</c>, <c>3 → "3"</c>, <c>0.08 → "0.08"</c>.
        /// </summary>
        public static string FormatDecimal(decimal value) =>
            value.ToString("0.############################", CultureInfo.InvariantCulture);

        /// <summary>백분율. <c>0.065 → "6.5%"</c>, <c>1 → "100%"</c>.</summary>
        public static string FormatPercent(decimal value) => FormatDecimal(value * 100m) + "%";

        /// <summary>
        /// 설명문에 적을 <b>크기</b>. 부호는 떼고 절댓값만 남긴다 — <c>-0.1 → "10%"</c> (S-UX-B3CJ97).
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>방향은 설명문의 말이 정한다.</b> 엑셀 <c>description_template</c> 이 "줄어든다 · 준다 ·
        /// 낮아진다" 로 이미 적고 있으므로, 값의 음수 부호까지 찍으면 "-10% 줄어든다" 가 되어 읽는 사람이
        /// <b>이중 부정</b>으로 읽는다. 값은 크기만 채우고 방향은 글이 말한다.
        /// </remarks>
        private static decimal Magnitude(decimal value) => value < 0m ? -value : value;

        private static string RenderOne(
            Match match, IReadOnlyDictionary<string, string> values, ICollection<string> problems, string where)
        {
            string body = match.Groups[1].Value.Trim();
            Match parsed = Body.Match(body);

            if (!parsed.Success)
            {
                problems?.Add($"{where}: '{{{body}}}' 를 해석할 수 없습니다. '{{key}}' 또는 '{{key:pct}}' 여야 합니다.");
                return match.Value;
            }

            string key = parsed.Groups[1].Value;
            bool percent = parsed.Groups[2].Success;

            if (values == null || !values.TryGetValue(key, out string raw) || raw == null)
            {
                problems?.Add($"{where}: 설명문의 '{{{body}}}' 에 해당하는 파라미터가 없습니다.");
                return match.Value;
            }

            if (decimal.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
            {
                decimal magnitude = Magnitude(number);
                return percent ? FormatPercent(magnitude) : FormatDecimal(magnitude);
            }

            // 숫자가 아닌 값(예: DOT, true)은 그대로 적는다. 백분율로 적으라는데 숫자가 아니면 데이터 실수다.
            if (percent)
            {
                problems?.Add($"{where}: '{{{body}}}' 는 백분율인데 값 '{raw}' 가 숫자가 아닙니다.");
            }

            return raw.Trim();
        }
    }
}
