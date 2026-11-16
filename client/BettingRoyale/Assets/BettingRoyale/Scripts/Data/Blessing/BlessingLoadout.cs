using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 한 플레이어가 이번 매치에서 획득한 축복 (S-GD-RFXF8X).
    ///
    /// 축복은 <b>라운드를 넘어 누적되고 매치를 넘지 못한다.</b> 골드 이월과 같은 축이지만
    /// 단위가 다르다 — 골드는 라운드 사이에 한도가 걸리고, 축복은 라운드 사이에 그냥 쌓인다.
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class BlessingLoadout
    {
        private readonly List<BlessingDefinition> acquired = new List<BlessingDefinition>();

        /// <summary>획득 순서대로의 축복 목록.</summary>
        public IReadOnlyList<BlessingDefinition> Acquired => acquired;

        public int Count => acquired.Count;

        /// <summary>
        /// 직접 눌러 쓰는 축복을 이미 가졌는가. 가졌으면 뽑기 후보에 다른 액티브가 나오지 않는다(S-GD-NJQ5NM §2).
        /// </summary>
        public bool HasActive
        {
            get
            {
                for (var i = 0; i < acquired.Count; i++)
                {
                    if (acquired[i].IsActive)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>준비 단계에서 선택한 축복을 추가한다.</summary>
        /// <remarks>
        /// <b>같은 축복을 두 번 획득할 수 없다 (S-GD-VF67AG).</b> 중복이 오면 던진다 —
        /// <see cref="BlessingDraft.Begin"/> 가 이미 획득한 축복을 후보에서 빼기 때문에
        /// 정상 흐름에서는 여기 도달할 수 없다. 도달했다면 그 필터가 뚫린 것이므로
        /// 조용히 쌓아서 효과가 두 번 걸리게 두는 것보다 즉시 드러내는 편이 낫다.
        /// </remarks>
        public void Add(BlessingDefinition blessing)
        {
            if (blessing == null)
            {
                throw new ArgumentNullException(nameof(blessing));
            }

            if (Contains(blessing))
            {
                throw new InvalidOperationException(
                    $"이미 획득한 축복입니다: {blessing.Id}. " +
                    "중복 획득은 허용되지 않습니다 (S-GD-VF67AG). 후보 필터가 뚫렸는지 확인하세요.");
            }

            acquired.Add(blessing);
        }

        /// <summary>이미 획득한 축복인가. 후보에서 제외할 때 쓴다 (S-GD-VF67AG).</summary>
        public bool Contains(BlessingDefinition blessing)
        {
            if (blessing == null)
            {
                return false;
            }

            for (var i = 0; i < acquired.Count; i++)
            {
                if (blessing.Equals(acquired[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>새 매치를 시작할 때 비운다 (S-GD-RFXF8X).</summary>
        public void ResetForNewMatch() => acquired.Clear();
    }
}
