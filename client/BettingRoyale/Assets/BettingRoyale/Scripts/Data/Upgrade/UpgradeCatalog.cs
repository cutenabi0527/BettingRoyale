using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 세트 효과 한 줄 (S-GD-A2YDDJ). <c>upgrades.json</c> 의 <c>set_bonus</c> 항목이다.
    /// </summary>
    public sealed class UpgradeSetBonus
    {
        private readonly StatContribution[] stats;
        private readonly WeaponType[] weaponFilters;

        /// <param name="level">이 세트가 붙기 시작하는 단계. 기획서는 1·3·5 를 쓴다.</param>
        public UpgradeSetBonus(int level, IReadOnlyList<StatContribution> stats)
            : this(level, stats, null)
        {
        }

        /// <param name="weaponFilters">
        /// 스탯마다 <b>어느 무기를 들었을 때만</b> 붙는가. <see cref="WeaponType.None"/> = 모든 무기.
        /// 검 사거리·활 화살 반경처럼 무기 전용 세트 효과가 여기 온다 (S-GD-G3D2WP). <c>null</c> 이면 전부 공용이다.
        /// </param>
        public UpgradeSetBonus(int level, IReadOnlyList<StatContribution> stats, IReadOnlyList<WeaponType> weaponFilters)
        {
            if (level < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "세트 단계는 1부터입니다.");
            }

            Level = level;

            if (stats == null || stats.Count == 0)
            {
                this.stats = Array.Empty<StatContribution>();
                this.weaponFilters = Array.Empty<WeaponType>();
                return;
            }

            if (weaponFilters != null && weaponFilters.Count != stats.Count)
            {
                throw new ArgumentException("세트 스탯과 무기 필터의 개수가 다릅니다.", nameof(weaponFilters));
            }

            this.stats = new StatContribution[stats.Count];
            this.weaponFilters = new WeaponType[stats.Count];

            for (int i = 0; i < stats.Count; i++)
            {
                this.stats[i] = stats[i];
                this.weaponFilters[i] = weaponFilters != null ? weaponFilters[i] : WeaponType.None;
            }
        }

        public int Level { get; }

        /// <summary>모든 스탯(무기 필터 포함). 실제 적용은 <see cref="StatsFor"/> 로 무기에 맞는 것만 얹는다.</summary>
        public IReadOnlyList<StatContribution> Stats => stats;

        /// <summary><see cref="Stats"/> 와 같은 순서의 무기 필터. <see cref="WeaponType.None"/> = 공용.</summary>
        public IReadOnlyList<WeaponType> WeaponFilters => weaponFilters;

        /// <summary>이 스탯이 <paramref name="weapon"/> 을 들었을 때 붙는가.</summary>
        public bool AppliesTo(int statIndex, WeaponType weapon) =>
            statIndex >= 0 && statIndex < weaponFilters.Length
            && (weaponFilters[statIndex] == WeaponType.None || weaponFilters[statIndex] == weapon);

        /// <summary>장착 무기에 붙는 스탯만 <paramref name="into"/> 에 더한다.</summary>
        public void StatsFor(WeaponType weapon, List<StatContribution> into)
        {
            if (into == null) return;
            for (int i = 0; i < stats.Length; i++)
                if (AppliesTo(i, weapon)) into.Add(stats[i]);
        }

        public override string ToString() => $"{Level}강 세트 · 스탯 {stats.Length}종";
    }

    /// <summary>
    /// 강화 데이터 전체 (S-GD-A2YDDJ). <c>upgrades.json</c> 이 그대로 들어온다.
    ///
    /// <code>
    /// weapon.sword   1~5강 표
    /// weapon.bow     1~5강 표     ← 무기 종류마다 다르다
    /// armor          1~5강 표     ← 종류 구분 없음
    /// set_bonus      1·3·5강      ← 낮은 쪽 단계로 판정, 누적
    /// </code>
    ///
    /// <para>
    /// <b>여기에 수치가 없다.</b> 표를 받아 들고 있을 뿐이다(D-041).
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class UpgradeCatalog
    {
        private readonly Dictionary<WeaponType, UpgradeTrack> weaponTracks;
        private readonly UpgradeSetBonus[] setBonuses;

        public UpgradeCatalog(
            IReadOnlyDictionary<WeaponType, UpgradeTrack> weaponTracks,
            UpgradeTrack armorTrack,
            IReadOnlyList<UpgradeSetBonus> setBonuses)
        {
            if (weaponTracks == null)
            {
                throw new ArgumentNullException(nameof(weaponTracks));
            }

            this.weaponTracks = new Dictionary<WeaponType, UpgradeTrack>();

            foreach (var pair in weaponTracks)
            {
                if (pair.Value == null)
                {
                    throw new ArgumentException($"{pair.Key} 의 강화 표가 null 입니다.", nameof(weaponTracks));
                }

                this.weaponTracks[pair.Key] = pair.Value;
            }

            Armor = armorTrack ?? throw new ArgumentNullException(nameof(armorTrack));

            if (setBonuses == null || setBonuses.Count == 0)
            {
                this.setBonuses = Array.Empty<UpgradeSetBonus>();
                return;
            }

            this.setBonuses = new UpgradeSetBonus[setBonuses.Count];

            for (int i = 0; i < setBonuses.Count; i++)
            {
                var bonus = setBonuses[i];

                if (bonus == null)
                {
                    throw new ArgumentException("세트 효과에 null 이 있습니다.", nameof(setBonuses));
                }

                if (i > 0 && bonus.Level <= this.setBonuses[i - 1].Level)
                {
                    // 오름차순이 아니면 "5강인데 3강 세트가 빠지는" 일이 조용히 생긴다.
                    throw new ArgumentException(
                        $"세트 효과는 단계 오름차순이어야 합니다. {this.setBonuses[i - 1].Level} 다음이 {bonus.Level} 입니다.",
                        nameof(setBonuses));
                }

                this.setBonuses[i] = bonus;
            }
        }

        /// <summary>방어구 표. 무기와 달리 종류 구분이 없다.</summary>
        public UpgradeTrack Armor { get; }

        /// <summary>세트 효과. 단계 오름차순이다.</summary>
        public IReadOnlyList<UpgradeSetBonus> SetBonuses => setBonuses;

        /// <summary>
        /// 이 무기 종류의 강화 표.
        ///
        /// <b>없으면 <c>false</c> 다</b> — 둔기처럼 아직 표가 없는 무기가 실제로 있다.
        /// 그때는 무기 강화를 못 할 뿐이고 예외를 던지지 않는다.
        /// </summary>
        public bool TryGetWeaponTrack(WeaponType weapon, out UpgradeTrack track) =>
            weaponTracks.TryGetValue(weapon, out track);

        public bool HasWeapon(WeaponType weapon) => weaponTracks.ContainsKey(weapon);

        /// <summary>강화 표가 있는 무기 종류. 진단과 UI 가 쓴다.</summary>
        public IReadOnlyCollection<WeaponType> WeaponTypes => weaponTracks.Keys;

        /// <summary>
        /// <b>데이터의 무기 이름을 <see cref="WeaponType"/> 으로 옮긴다.</b>
        /// <c>upgrades.json</c> 의 <c>"sword"</c>·<c>"bow"</c> 같은 키가 들어온다.
        ///
        /// <para>
        /// <b>무기가 늘어나도 이 함수는 안 고친다.</b> 열거형에 값을 더하면 그 이름이 자동으로
        /// 인식된다 — switch 로 짜면 무기를 추가할 때마다 여기도 고쳐야 하고, 빠뜨리면
        /// <b>그 무기만 조용히 강화가 안 된다.</b>
        /// </para>
        ///
        /// <para>대소문자를 가리지 않는다. <c>None</c> 은 무기가 아니므로 거부한다.</para>
        /// </summary>
        public static bool TryParseWeapon(string name, out WeaponType weapon)
        {
            weapon = WeaponType.None;

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (!Enum.TryParse(name.Trim(), ignoreCase: true, out WeaponType parsed))
            {
                return false;
            }

            if (parsed == WeaponType.None)
            {
                return false;
            }

            weapon = parsed;
            return true;
        }

        /// <summary>
        /// <b>강화 표가 없는 무기를 찾아낸다.</b> 매치를 시작할 때 한 번 불러 로그로 남기면,
        /// 무기를 추가하고 <c>upgrades.json</c> 에 표를 빠뜨린 것을 <b>플레이 중이 아니라
        /// 시작할 때</b> 알 수 있다.
        ///
        /// <para>
        /// 예외를 던지지 않는다 — 표가 없는 무기는 <b>강화만 못 할 뿐 게임은 돌아간다</b>
        /// (<see cref="UpgradeAttemptResult.NoTrack"/>). 다만 조용히 넘어가면 안 된다.
        /// </para>
        /// </summary>
        /// <param name="playableWeapons">지금 매치에서 쓸 수 있는 무기 전부.</param>
        public IReadOnlyList<WeaponType> FindWeaponsWithoutTrack(IEnumerable<WeaponType> playableWeapons)
        {
            var missing = new List<WeaponType>();

            if (playableWeapons == null)
            {
                return missing;
            }

            foreach (var weapon in playableWeapons)
            {
                if (weapon != WeaponType.None && !weaponTracks.ContainsKey(weapon))
                {
                    missing.Add(weapon);
                }
            }

            return missing;
        }

        public override string ToString() =>
            $"강화 데이터 · 무기 {weaponTracks.Count}종 · 방어구 최대 {Armor.MaxLevel}강 · 세트 {setBonuses.Length}단계";
    }
}
