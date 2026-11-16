using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화 세트 효과를 <b>사람이 읽는 글</b>로 옮긴다 — 강화 창의 세트 안내와 HUD 효과 칸이 함께 쓴다 (S-UX-JZXPV9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>수치를 여기 적지 않는다.</b> 세트가 무엇을 주는지는 강화 표(<see cref="UpgradeCatalog.SetBonuses"/>)에 있고,
    /// 이 클래스는 그 표를 글로 바꾸기만 한다 — 표가 바뀌면(<c>upgrades.json</c>, S-GD-A2YDDJ) 안내도 저절로 바뀐다.
    /// 스탯 이름은 축복과 같은 스탯 목록(<see cref="PlaceholderBlessingBalance"/>)의 이름을 쓴다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class UpgradeSetBonusGuide
    {
        /// <summary>
        /// 화면이 안내하는 표. 경제의 기본 강화 표(<c>MatchEconomyState</c> 의 기본값)와 <b>같은 원본</b>이다 —
        /// 지금은 제품·훈련·로컬 경로가 모두 이 기본 표를 쓴다. 표를 주입하는 경로가 생기면 안내도 그 표를 받아야 한다.
        /// </summary>
        public static UpgradeCatalog DefaultCatalog => BettingRoyale.Data.Balance.BalanceCatalogs.Current.Upgrades;

        /// <summary>스탯 표시 이름. 기획 데이터가 있으면 그 이름(엑셀 stats), 없으면 코드 대역 목록이다.</summary>
        private static StatRegistry StatNames => BlessingBalance.StatRegistry;

        /// <summary>
        /// 값이 <b>비율</b>인 스탯 — 0.03 을 3% 로 적는다. 나머지(공격력·방어력·최대 체력·방어력 관통)는 그대로 적는다.
        /// </summary>
        private static readonly HashSet<string> PercentStats = new HashSet<string>(StringComparer.Ordinal)
        {
            UpgradeStatKeys.MoveSpeed,
            UpgradeStatKeys.AttackSpeed,
            DamageStatKeys.DamageBonus,
            DamageStatKeys.DamageTakenMult,
            CombatStatKeys.AttackRange,
            CombatStatKeys.ProjectileRadius,
        };

        /// <summary>
        /// 지금 세트 단계에서 붙는 세트 효과의 <b>개수</b>. 세트는 누적이라 5강이면 1·3·5강 셋이다 (S-GD-A2YDDJ).
        /// </summary>
        public static int AchievedTierCount(UpgradeCatalog catalog, int setLevel)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            int count = 0;
            for (int i = 0; i < catalog.SetBonuses.Count; i++)
            {
                if (catalog.SetBonuses[i].Level > setLevel)
                {
                    break;   // 표는 단계 오름차순이다
                }

                count++;
            }

            return count;
        }

        /// <summary>지금 붙어 있는 <b>가장 높은</b> 세트. 하나도 없으면 <c>null</c> (강화 창 "현재 세트 효과").</summary>
        public static UpgradeSetBonus CurrentTier(UpgradeCatalog catalog, int setLevel)
        {
            int count = AchievedTierCount(catalog, setLevel);
            return count > 0 ? catalog.SetBonuses[count - 1] : null;
        }

        /// <summary>다음에 붙을 세트. 이미 마지막 세트까지 붙었으면 <c>null</c> (강화 창 "다음 세트 효과").</summary>
        public static UpgradeSetBonus NextTier(UpgradeCatalog catalog, int setLevel)
        {
            int count = AchievedTierCount(catalog, setLevel);
            return count < catalog.SetBonuses.Count ? catalog.SetBonuses[count] : null;
        }

        /// <summary>
        /// 지금 붙은 세트를 <b>모두 합쳐</b> 스탯마다 한 줄로 적는다 — 세트는 누적이고 같은 스탯은 더한다(합연산, S-GD-SH74SC).
        /// 붙은 세트가 없으면 빈 문자열이다.
        /// </summary>
        public static string DescribeAchieved(UpgradeCatalog catalog, int setLevel, string separator = "\n")
        {
            var totals = new List<MergedStat>();
            int count = AchievedTierCount(catalog, setLevel);
            for (int i = 0; i < count; i++)
            {
                Accumulate(totals, catalog.SetBonuses[i]);
            }

            var text = new StringBuilder();
            for (int i = 0; i < totals.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(separator);
                }

                AppendName(text, totals[i]);
                text.Append(' ').Append(FormatValue(totals[i].StatId, totals[i].Value));
            }

            return text.ToString();
        }

        /// <summary>
        /// 다음 세트에 닿으면 <b>무엇이 더해지고 무엇이 오르는가</b>. 이미 붙은 스탯은 <c>"이동 속도 +3% → +5%"</c>,
        /// 새로 생기는 스탯은 <c>"공격력 +2 추가"</c>. 다음 세트가 없으면 빈 문자열이다.
        /// </summary>
        public static string DescribeNextGain(UpgradeCatalog catalog, int setLevel, string separator = "\n")
        {
            UpgradeSetBonus next = NextTier(catalog, setLevel);
            if (next == null)
            {
                return string.Empty;
            }

            var current = new List<MergedStat>();
            int count = AchievedTierCount(catalog, setLevel);
            for (int i = 0; i < count; i++)
            {
                Accumulate(current, catalog.SetBonuses[i]);
            }

            var added = new List<MergedStat>();
            Accumulate(added, next);

            var text = new StringBuilder();
            for (int i = 0; i < added.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(separator);
                }

                MergedStat gain = added[i];
                AppendName(text, gain);
                int existing = IndexOf(current, gain.StatId, gain.Filter);
                if (existing >= 0)
                {
                    float before = current[existing].Value;
                    text.Append(' ').Append(FormatValue(gain.StatId, before))
                        .Append(" → ").Append(FormatValue(gain.StatId, before + gain.Value));
                }
                else
                {
                    text.Append(' ').Append(FormatValue(gain.StatId, gain.Value)).Append(" 추가");
                }
            }

            return text.ToString();
        }

        /// <summary>스탯·무기 필터가 같은 기여를 더해 한 칸으로 모은다. 처음 나온 순서를 지킨다.</summary>
        private static void Accumulate(List<MergedStat> into, UpgradeSetBonus bonus)
        {
            for (int i = 0; i < bonus.Stats.Count; i++)
            {
                WeaponType filter = i < bonus.WeaponFilters.Count ? bonus.WeaponFilters[i] : WeaponType.None;
                StatContribution stat = bonus.Stats[i];
                int index = IndexOf(into, stat.StatId, filter);
                if (index >= 0)
                {
                    into[index] = new MergedStat(stat.StatId, filter, into[index].Value + stat.Value);
                }
                else
                {
                    into.Add(new MergedStat(stat.StatId, filter, stat.Value));
                }
            }
        }

        private static int IndexOf(List<MergedStat> list, string statId, WeaponType filter)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Filter == filter && string.Equals(list[i].StatId, statId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AppendName(StringBuilder text, in MergedStat stat)
        {
            if (stat.Filter != WeaponType.None)
            {
                text.Append(WeaponLabel(stat.Filter)).Append(": ");
            }

            text.Append(StatNames.TryGet(stat.StatId, out StatDefinition definition)
                ? definition.DisplayName
                : stat.StatId);
        }

        private readonly struct MergedStat
        {
            public MergedStat(string statId, WeaponType filter, float value)
            {
                StatId = statId;
                Filter = filter;
                Value = value;
            }

            public string StatId { get; }
            public WeaponType Filter { get; }
            public float Value { get; }
        }

        /// <summary>세트 한 단계가 주는 것을 줄마다 하나씩 적는다. 예: <c>"이동 속도 +3%"</c>.</summary>
        public static string Describe(UpgradeSetBonus bonus, string separator = "\n")
        {
            if (bonus == null)
            {
                throw new ArgumentNullException(nameof(bonus));
            }

            var text = new StringBuilder();
            for (int i = 0; i < bonus.Stats.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(separator);
                }

                // 무기 전용 세트는 어느 무기 것인지 앞에 적는다 — "검: 공격 사거리 +10%".
                WeaponType filter = i < bonus.WeaponFilters.Count ? bonus.WeaponFilters[i] : WeaponType.None;
                if (filter != WeaponType.None)
                {
                    text.Append(WeaponLabel(filter)).Append(": ");
                }

                text.Append(DescribeStat(bonus.Stats[i]));
            }

            return text.ToString();
        }

        private static string WeaponLabel(WeaponType weapon) => weapon switch
        {
            WeaponType.Sword => "검",
            WeaponType.Bow => "활",
            WeaponType.Mace => "둔기",
            _ => weapon.ToString(),
        };

        /// <summary>스탯 기여 한 줄. 받는 피해 −5% 처럼 줄어드는 것도 부호 그대로 적는다.</summary>
        public static string DescribeStat(in StatContribution stat)
        {
            string name = StatNames.TryGet(stat.StatId, out StatDefinition definition)
                ? definition.DisplayName
                : stat.StatId;

            return name + " " + FormatValue(stat.StatId, stat.Value);
        }

        /// <summary>비율 스탯은 <c>+3%</c>, 그 밖은 <c>+2</c>. 0 은 <c>+0</c>.</summary>
        public static string FormatValue(string statId, float value)
        {
            bool percent = statId != null && PercentStats.Contains(statId);
            double shown = percent ? Math.Round(value * 100.0, 1) : Math.Round(value, 1);
            string sign = shown < 0 ? "-" : "+";
            string number = Math.Abs(shown).ToString("0.#", CultureInfo.InvariantCulture);
            return sign + number + (percent ? "%" : string.Empty);
        }
    }
}
