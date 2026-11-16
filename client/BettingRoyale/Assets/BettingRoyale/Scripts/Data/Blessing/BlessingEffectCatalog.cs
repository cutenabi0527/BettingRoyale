using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 계수 전체 (엑셀 <c>effects</c> 시트). 축복 하나가 여러 행을 갖는다.
    ///
    /// <para>
    /// <b>여기에 수치가 없다.</b> 행을 받아 들고 있을 뿐이고, 값은 데이터 파이프라인이 준다(D-041).
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class BlessingEffectCatalog
    {
        private static readonly BlessingEffectRow[] Empty = new BlessingEffectRow[0];

        private readonly Dictionary<string, List<BlessingEffectRow>> byBlessing;
        private readonly int rowCount;

        public BlessingEffectCatalog(IEnumerable<BlessingEffectRow> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            byBlessing = new Dictionary<string, List<BlessingEffectRow>>(StringComparer.Ordinal);

            foreach (var row in rows)
            {
                if (!byBlessing.TryGetValue(row.BlessingId, out var list))
                {
                    list = new List<BlessingEffectRow>(4);
                    byBlessing[row.BlessingId] = list;
                }

                list.Add(row);
                rowCount++;
            }
        }

        public int RowCount => rowCount;

        public int BlessingCount => byBlessing.Count;

        public IReadOnlyCollection<string> BlessingIds => byBlessing.Keys;

        /// <summary>
        /// 이 축복의 계수 전부. <b>없으면 빈 목록이다</b> — 계수가 없는 축복이 실제로 있다
        /// (시스템 대기 중이거나 순수 표기용).
        /// </summary>
        public IReadOnlyList<BlessingEffectRow> For(string blessingId)
        {
            if (blessingId != null && byBlessing.TryGetValue(blessingId, out var list))
            {
                return list;
            }

            return Empty;
        }

        public bool Contains(string blessingId) => blessingId != null && byBlessing.ContainsKey(blessingId);

        public override string ToString() => $"축복 계수 {rowCount}행 · {byBlessing.Count}종";
    }
}
