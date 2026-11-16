using System;
using System.Collections.Generic;
using System.Globalization;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 후보 풀에 넣을지 말지를 정하는 <b>데이터 기반 규칙</b> (축복 시스템 기획 §6.1).
    ///
    /// <para>
    /// 지금 다루는 것은 <c>min_match_size</c> 하나다 — 인원이 적은 매치에서는 성립하지 않는
    /// 축복이 있다(승부사는 생존 인원 조건이라 2인 매치에서 뜻이 없다). 값은 엑셀
    /// <c>effects</c> 시트의 행동 파라미터로 들어온다.
    /// </para>
    ///
    /// <para>
    /// <b>여기에 수치가 없다.</b> 계수 카탈로그를 읽어 만들 뿐이다(D-041).
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 기획 §6.2 — 하한 판정은 2인 매치 기준으로 한다.</b> <c>min_match_size: 4</c> 인
    /// 승부사가 빠지면 실버 Common 이 하나 줄어들므로, 풀이 3개를 넘는지 확인할 때는
    /// <b>가장 인원이 적은 경우</b>로 재야 안전하다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class BlessingPoolRules
    {
        /// <summary>엑셀 <c>effects</c> 시트의 파라미터 이름.</summary>
        public const string MinMatchSizeKey = "min_match_size";

        private readonly Dictionary<string, int> minMatchSizeById;

        /// <summary>계수 카탈로그에서 풀 규칙을 뽑아낸다.</summary>
        public BlessingPoolRules(BlessingEffectCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            minMatchSizeById = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var blessingId in catalog.BlessingIds)
            {
                var rows = catalog.For(blessingId);

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];

                    if (!string.Equals(row.ParamKey, MinMatchSizeKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
                    {
                        // 조용히 0 으로 넘기면 그 축복이 2인 매치에도 뜬다.
                        throw new FormatException(
                            $"{row.BlessingId}.{MinMatchSizeKey} 의 값 '{row.Value}' 가 정수가 아닙니다.");
                    }

                    minMatchSizeById[row.BlessingId] = size;
                }
            }
        }

        /// <summary>규칙이 하나도 없는 상태. 필터가 아무것도 거르지 않는다.</summary>
        public static BlessingPoolRules Empty { get; } =
            new BlessingPoolRules(new BlessingEffectCatalog(Array.Empty<BlessingEffectRow>()));

        public int RuleCount => minMatchSizeById.Count;

        /// <summary>이 축복이 요구하는 최소 매치 인원. 지정이 없으면 0(제한 없음)이다.</summary>
        public int MinMatchSizeOf(string blessingId) =>
            blessingId != null && minMatchSizeById.TryGetValue(blessingId, out int size) ? size : 0;

        /// <summary>
        /// 이 인원의 매치에서 뽑아도 되는가.
        ///
        /// <para>
        /// <paramref name="matchSize"/> 가 0 이면 <b>인원을 모르는 것</b>이라 거르지 않는다 —
        /// 인원을 안 넘긴 호출부의 동작이 지금과 같게 유지된다.
        /// </para>
        /// </summary>
        public bool Allows(string blessingId, int matchSize)
        {
            if (matchSize <= 0)
            {
                return true;
            }

            return matchSize >= MinMatchSizeOf(blessingId);
        }

        public override string ToString() => $"풀 규칙 {minMatchSizeById.Count}건";
    }
}
