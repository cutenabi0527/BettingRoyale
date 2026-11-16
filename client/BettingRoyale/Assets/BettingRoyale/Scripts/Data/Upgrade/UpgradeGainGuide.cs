using System;
using System.Collections.Generic;
using System.Text;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 부위 하나를 <b>한 단계 더 올리면 지금보다 얼마나 오르는가</b> — 강화 창 부위 카드가 쓴다 (S-UX-FBN9YW · S-UX-7M655J).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 강화 표의 값은 <b>0강 대비 누적</b>이라(S-GD-A2YDDJ) 오르는 양은 <c>다음 단계 − 지금 단계</c> 다. 수치를 여기 적지 않고
    /// 경제가 실제로 얹는 그 표(<see cref="UpgradeSetBonusGuide.DefaultCatalog"/>)에서 읽는다. 무기 표는 종류마다 달라 장착 무기를 받는다.
    /// </para>
    /// <para>
    /// 이번 강화로 <b>세트 단계가 새 세트에 닿으면</b> 그것도 알린다 — 세트는 두 부위 중 낮은 쪽이라(<see cref="UpgradeState.SetLevelOf"/>)
    /// 낮은 쪽을 올릴 때만 생긴다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class UpgradeGainGuide
    {
        private const float Epsilon = 0.0001f;

        /// <summary>
        /// 다음 단계에서 오르는 스탯. 변하지 않는 스탯은 뺀다. 최대 단계이거나 표가 없으면 빈 목록.
        /// </summary>
        public static IReadOnlyList<StatContribution> NextGains(
            UpgradeCatalog catalog, UpgradeSlot slot, WeaponType weapon, int currentLevel)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!TryGetTrack(catalog, slot, weapon, out UpgradeTrack track)
                || !track.TryGet(currentLevel + 1, out UpgradeLevel next))
            {
                return Array.Empty<StatContribution>();
            }

            var now = new Dictionary<string, float>(StringComparer.Ordinal);
            if (currentLevel > 0 && track.TryGet(currentLevel, out UpgradeLevel current))
            {
                for (int i = 0; i < current.Stats.Count; i++)
                {
                    now.TryGetValue(current.Stats[i].StatId, out float value);
                    now[current.Stats[i].StatId] = value + current.Stats[i].Value;
                }
            }

            var gains = new List<StatContribution>(next.Stats.Count);
            for (int i = 0; i < next.Stats.Count; i++)
            {
                StatContribution stat = next.Stats[i];
                now.TryGetValue(stat.StatId, out float before);
                float delta = stat.Value - before;
                if (Math.Abs(delta) > Epsilon)
                {
                    gains.Add(new StatContribution(stat.StatId, delta, stat.Stack));
                }
            }

            return gains;
        }

        /// <summary>
        /// 이번 강화로 새로 닿는 세트 단계. 없으면 0. 세트는 누적이라 여러 단계를 한꺼번에 건너뛸 일은 없지만, 그래도 가장 높은 것을 준다.
        /// </summary>
        public static int SetTierReachedBy(UpgradeCatalog catalog, int currentLevel, int otherSlotLevel)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            int before = UpgradeState.SetLevelOf(currentLevel, otherSlotLevel);
            int after = UpgradeState.SetLevelOf(currentLevel + 1, otherSlotLevel);
            int reached = 0;
            for (int i = 0; i < catalog.SetBonuses.Count; i++)
            {
                int level = catalog.SetBonuses[i].Level;
                if (level > before && level <= after)
                {
                    reached = level;
                }
            }

            return reached;
        }

        /// <summary>
        /// 부위 카드에 적을 줄 — 오르는 스탯 한 줄에 하나, 새 세트에 닿으면 끝에 한 줄. 올릴 수 없으면 빈 문자열.
        /// </summary>
        /// <param name="setLine">세트 줄의 모양. <c>{0}</c> 에 단계가 들어간다 — 색·기호는 화면이 정한다.</param>
        public static string Describe(UpgradeCatalog catalog, UpgradeSlot slot, WeaponType weapon, int currentLevel,
            int otherSlotLevel, string setLine = "{0}강 세트 달성")
        {
            IReadOnlyList<StatContribution> gains = NextGains(catalog, slot, weapon, currentLevel);
            if (gains.Count == 0)
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            for (int i = 0; i < gains.Count; i++)
            {
                if (i > 0) text.Append('\n');
                text.Append(UpgradeSetBonusGuide.DescribeStat(gains[i]));
            }

            int tier = SetTierReachedBy(catalog, currentLevel, otherSlotLevel);
            if (tier > 0)
            {
                text.Append('\n').AppendFormat(setLine, tier);
            }

            return text.ToString();
        }

        private static bool TryGetTrack(UpgradeCatalog catalog, UpgradeSlot slot, WeaponType weapon, out UpgradeTrack track)
        {
            if (slot == UpgradeSlot.Armor)
            {
                track = catalog.Armor;
                return track != null;
            }

            if (slot == UpgradeSlot.Weapon)
            {
                return catalog.TryGetWeaponTrack(weapon, out track);
            }

            track = null;
            return false;
        }
    }
}
