using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>강화를 시도한 결과 (S-GD-A2YDDJ). <b>값은 뒤에만 붙인다</b> — 회선을 넘는다.</summary>
    public enum UpgradeAttemptResult
    {
        Success = 0,

        /// <summary>골드가 모자란다. <b>단계도 골드도 그대로다.</b></summary>
        NotEnoughGold = 1,

        /// <summary>이미 최대 단계다.</summary>
        MaxLevelReached = 2,

        /// <summary>
        /// 이 무기 종류의 강화 표가 없다. 둔기처럼 아직 표를 안 만든 무기가 있다.
        /// <b>데이터가 없는 것이지 조작이 잘못된 것이 아니다.</b>
        /// </summary>
        NoTrack = 3,

        /// <summary>
        /// 확률 판정에서 떨어졌다 (S15P21A606-304). <b>비용은 차감됐고 단계는 그대로다</b> —
        /// 하락·파괴는 없다(S-GD-3PGNS5 · S-GD-A2YDDJ). 거절이 아니다: 시도는 이루어졌다.
        /// </summary>
        Failed = 4,
    }

    /// <summary>
    /// 강화 성공 판정 한 번 (S15P21A606-304). <b>권위 쪽(Host·Local Play·훈련 로컬 세션)만 굴린다</b> —
    /// 클라이언트는 결과 상태를 받을 뿐 스스로 굴리지 않는다.
    /// </summary>
    public interface IUpgradeSuccessRoll
    {
        /// <summary><paramref name="successRatePercent"/>(0~100) 확률로 성공하면 <c>true</c>.</summary>
        bool Succeeds(int successRatePercent);
    }

    /// <summary>
    /// <see cref="DeterministicRandom"/> 으로 굴리는 강화 판정 (<c>WeaponUpgradeState</c> 와 같은 방식).
    ///
    /// <para>
    /// <b>100% 이상은 굴리지 않고 성공, 0% 이하는 굴리지 않고 실패</b>한다 — 확정 결과가 난수를 먹으면
    /// 1·2강을 사는 순서만으로 3강 이후의 결과가 바뀐다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ 시드가 곧 결과다. 온라인 Host 는 <see cref="CreateHostSeed"/> 처럼 <b>클라이언트가 알 수 없는 값</b>을
    /// 넣는다 — 모두가 아는 MatchId 에서 뽑으면 개조 클라이언트가 다음 판정을 미리 계산할 수 있다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class UpgradeSuccessRoll : IUpgradeSuccessRoll
    {
        /// <summary>언제나 성공한다. <b>테스트·도구 전용</b> — 제품 경로에 넣으면 확률이 사라진다.</summary>
        public static readonly IUpgradeSuccessRoll AlwaysSucceeds = new Fixed(true);

        /// <summary>언제나 실패한다(100% 는 제외). <b>테스트 전용.</b></summary>
        public static readonly IUpgradeSuccessRoll AlwaysFails = new Fixed(false);

        private readonly DeterministicRandom random;

        public UpgradeSuccessRoll(DeterministicRandom random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public UpgradeSuccessRoll(int seed)
            : this(new DeterministicRandom(seed))
        {
        }

        public bool Succeeds(int successRatePercent)
        {
            if (successRatePercent >= UpgradeLevel.MaxSuccessRatePercent)
            {
                return true;
            }

            if (successRatePercent <= 0)
            {
                return false;
            }

            return random.Next(UpgradeLevel.MaxSuccessRatePercent) < successRatePercent;
        }

        /// <summary>
        /// Host 가 매치마다 한 번 뽑는 판정 시드. <b>회선에 싣지 않는다</b> — 알려지면 결과를 미리 계산할 수 있다.
        /// </summary>
        public static int CreateHostSeed() => unchecked(Guid.NewGuid().GetHashCode() ^ Environment.TickCount);

        private sealed class Fixed : IUpgradeSuccessRoll
        {
            private readonly bool succeeds;

            public Fixed(bool succeeds)
            {
                this.succeeds = succeeds;
            }

            public bool Succeeds(int successRatePercent) =>
                successRatePercent >= UpgradeLevel.MaxSuccessRatePercent || (succeeds && successRatePercent > 0);
        }
    }

    /// <summary>
    /// 한 플레이어의 <b>강화 상태</b> — 무기 단계와 방어구 단계 (S-GD-A2YDDJ).
    ///
    /// <para>
    /// <b>부위마다 단계를 따로 가진다.</b> 무기 2강 + 방어구 1강 같은 상태가 성립하고,
    /// 세트 효과는 <b>낮은 쪽</b>을 본다 — 그것이 한쪽 몰빵을 막는 장치다.
    /// </para>
    ///
    /// <para>
    /// <b>단계마다 성공률이 있다</b> — 엑셀 <c>upgrade_levels.success_rate</c> (S15P21A606-304, 사용자 확정
    /// 2026-09-23: 1·2강 100% · 3강 90% · 4강 75% · 5강 60%). 실패하면 <b>비용만 나가고 단계는 그대로</b>다.
    /// 판정은 주입받은 <see cref="IUpgradeSuccessRoll"/> 이 하고, 그것을 쥔 권위 쪽만 이 객체를 돌린다.
    /// (2026-09-09 의 "100% 고정" 은 이 결정으로 끝났다.)
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 라운드가 끝나면 0강으로 돌아가고 골드는 환급되지 않는다.</b> 매 라운드 다시 산다 —
    /// 그래야 3라운드에도 골드를 쓸 이유가 생기고 재추첨·강화·룰렛 사이 배분 판단이 살아난다.
    /// </para>
    ///
    /// <para>
    /// <b>지갑을 직접 만진다.</b> 축복·상점·룰렛은 게이트가 경제를 만지지만 강화는 아직 게이트가
    /// 없다 — 어댑터가 붙을 때 같은 모양으로 옮긴다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class UpgradeState
    {
        /// <summary>스탯 시트에서 강화 기여를 걷어낼 때 쓰는 출처 이름.</summary>
        public const string WeaponSource = "upgrade:weapon";
        public const string ArmorSource = "upgrade:armor";
        public const string SetSource = "upgrade:set";

        private readonly UpgradeCatalog catalog;
        private readonly IUpgradeSuccessRoll successRoll;

        /// <param name="successRoll">
        /// 성공 판정. <b>권위 쪽이 쥔다</b> — <c>MatchEconomyState</c> 가 플레이어마다 시드를 갈라 넣는다.
        /// 테스트는 <see cref="UpgradeSuccessRoll.AlwaysSucceeds"/> 나 고정 시드를 넣는다.
        /// </param>
        public UpgradeState(UpgradeCatalog catalog, WeaponType equippedWeapon, IUpgradeSuccessRoll successRoll)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.successRoll = successRoll ?? throw new ArgumentNullException(nameof(successRoll));
            EquippedWeapon = equippedWeapon;
        }

        /// <summary>어떤 무기를 들고 있는가. 무기 강화 표가 이 값으로 갈린다.</summary>
        public WeaponType EquippedWeapon { get; set; }

        public int WeaponLevel { get; private set; }

        public int ArmorLevel { get; private set; }

        /// <summary>
        /// 세트 단계 — <b>무기와 방어구 중 낮은 쪽</b>이다.
        /// 한쪽만 5강이어도 다른 쪽이 1강이면 1강 세트만 붙는다.
        /// </summary>
        public int SetLevel => SetLevelOf(WeaponLevel, ArmorLevel);

        /// <summary>
        /// 세트 단계 규칙 한 곳 — <b>두 부위 중 낮은 쪽</b>. 단계만 받은 쪽(훈련 세션 화면)도 여기서 구한다 —
        /// 화면이 <c>Min</c> 을 따로 적으면 규칙이 두 곳에 생긴다.
        /// </summary>
        public static int SetLevelOf(int weaponLevel, int armorLevel) =>
            weaponLevel < armorLevel ? weaponLevel : armorLevel;

        public int LevelOf(UpgradeSlot slot) => RequireSlot(slot) == UpgradeSlot.Weapon ? WeaponLevel : ArmorLevel;

        /// <summary>
        /// 아는 부위인가.
        ///
        /// <para>
        /// <b>⚠️ 이 검사가 없으면 범위 밖 값이 조용히 통과한다.</b> <see cref="LevelOf"/> 는
        /// "무기가 아니면 방어구" 로, <see cref="TryGetTrack"/> 은 "방어구가 아니면 무기" 로
        /// 갈라서 <b>기본 분기가 서로 반대다</b> — <c>(UpgradeSlot)7</c> 을 넘기면
        /// <b>무기 가격을 내고 방어구 단계가 오른다.</b> 회선에서 온 값이라 실제로 올 수 있다.
        /// </para>
        /// </summary>
        private static UpgradeSlot RequireSlot(UpgradeSlot slot)
        {
            if (slot != UpgradeSlot.Weapon && slot != UpgradeSlot.Armor)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot), slot, "모르는 강화 부위입니다. 무기와 방어구뿐입니다.");
            }

            return slot;
        }

        /// <summary>
        /// 그 부위의 강화 표가 있는가. <b>없으면 그 부위는 이번 매치에서 올릴 수 없다</b> —
        /// 둔기처럼 표를 아직 안 만든 무기이거나 무기를 모르는 상태다.
        /// </summary>
        public bool HasTrack(UpgradeSlot slot) => TryGetTrack(slot, out _);

        /// <summary>그 부위의 최대 단계. <b>표가 없으면 0 이다</b> — 예외를 던지지 않는다.</summary>
        public int MaxLevelOf(UpgradeSlot slot) => TryGetTrack(slot, out var track) ? track.MaxLevel : 0;

        /// <summary>그 부위의 다음 단계 비용. 올릴 수 없으면 <c>false</c>.</summary>
        public bool TryGetNextCost(UpgradeSlot slot, out int costGold)
        {
            costGold = 0;

            if (!TryGetTrack(slot, out var track))
            {
                return false;
            }

            if (!track.TryGet(LevelOf(slot) + 1, out var next))
            {
                return false;
            }

            costGold = next.CostGold;
            return true;
        }

        /// <summary>
        /// 그 부위의 <b>다음 단계</b> 성공률(0~100). 표에서 온다 — 화면·회선이 이 값을 그대로 싣는다.
        /// 올릴 수 없으면(표 없음·최대 단계) 0 이다.
        /// </summary>
        public int NextSuccessRatePercent(UpgradeSlot slot)
        {
            if (!TryGetTrack(slot, out var track) || !track.TryGet(LevelOf(slot) + 1, out var next))
            {
                return 0;
            }

            return next.SuccessRatePercent;
        }

        /// <summary>
        /// 한 단계 올린다.
        ///
        /// <para>
        /// <b>순서: 골드 차감 → 확률 판정.</b> 실패해도 차감한 골드는 돌려주지 않는다(S-GD-3PGNS5).
        /// 골드가 모자라면 굴리지 않는다 — 판정 기회를 공짜로 쓰는 경로가 되면 안 된다.
        /// </para>
        /// </summary>
        public UpgradeAttemptResult TryUpgrade(UpgradeSlot slot, Wallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (!TryGetTrack(slot, out var track))
            {
                return UpgradeAttemptResult.NoTrack;
            }

            int next = LevelOf(slot) + 1;

            if (!track.TryGet(next, out var levelData))
            {
                return UpgradeAttemptResult.MaxLevelReached;
            }

            if (!wallet.TrySpend(levelData.CostGold))
            {
                return UpgradeAttemptResult.NotEnoughGold;
            }

            // 확률 판정 (S15P21A606-304). 권위 쪽만 이 객체를 돌리므로 여기서 굴리는 것이 곧 Host 판정이다.
            // ⚠️ 실패해도 위에서 차감한 골드는 돌려주지 않는다 — 단계만 그대로 둔다(하락·파괴 없음).
            if (!successRoll.Succeeds(levelData.SuccessRatePercent))
            {
                return UpgradeAttemptResult.Failed;
            }

            if (slot == UpgradeSlot.Weapon)
            {
                WeaponLevel = next;
            }
            else
            {
                ArmorLevel = next;
            }

            return UpgradeAttemptResult.Success;
        }

        /// <summary>
        /// 라운드가 끝났다. <b>두 부위 다 0강으로 돌아가고 골드는 환급되지 않는다</b> (S-GD-A2YDDJ).
        /// </summary>
        public void ResetForNextRound()
        {
            WeaponLevel = 0;
            ArmorLevel = 0;
        }

        /// <summary>
        /// 지금 단계의 스탯 기여를 시트에 얹는다.
        ///
        /// <para>
        /// <b>⚠️ 표의 값이 0강 대비 누적이라 현재 단계 한 줄만 얹는다.</b> 1~N강을 전부 더하면
        /// 값이 몇 배가 된다. 세트 효과는 반대로 <b>누적</b>이라 조건을 만족하는 것을 전부 얹는다
        /// — 5강이면 1·3·5강 세트가 모두 붙는다.
        /// </para>
        ///
        /// <para>
        /// 축복과 <b>같은 스탯 시트</b>에 얹는다. 강화는 별도 계산 체계가 아니라 또 하나의
        /// 공급원이고, 합산 규칙은 시트가 안다.
        /// </para>
        /// </summary>
        public void ApplyTo(StatSheet sheet)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            // ⚠️ 걷어내고 얹는 것을 ReplaceFrom 이 한 덩어리로 한다.
            //    따로 하면 중간에 던졌을 때 **부르기 전보다 나빠진 시트**가 남는다 —
            //    멀쩡하던 기여까지 사라진 채로 전투가 시작된다.

            IReadOnlyList<StatContribution> weaponStats = EmptyStats;

            if (WeaponLevel > 0
                && catalog.TryGetWeaponTrack(EquippedWeapon, out var weaponTrack)
                && weaponTrack.TryGet(WeaponLevel, out var weaponData))
            {
                weaponStats = weaponData.Stats;
            }

            IReadOnlyList<StatContribution> armorStats = EmptyStats;

            if (ArmorLevel > 0 && catalog.Armor.TryGet(ArmorLevel, out var armorData))
            {
                armorStats = armorData.Stats;
            }

            setBuffer.Clear();
            int setLevel = SetLevel;

            for (int i = 0; i < catalog.SetBonuses.Count; i++)
            {
                var bonus = catalog.SetBonuses[i];

                if (bonus.Level > setLevel)
                {
                    break;   // 오름차순이라 여기부터는 전부 조건 미달이다
                }

                // 무기 전용 세트(검 사거리·활 화살 반경)는 지금 든 무기 것만 붙는다 (S-GD-G3D2WP).
                bonus.StatsFor(EquippedWeapon, setBuffer);
            }

            sheet.ReplaceFrom(WeaponSource, weaponStats);
            sheet.ReplaceFrom(ArmorSource, armorStats);
            sheet.ReplaceFrom(SetSource, setBuffer);
        }

        private static readonly StatContribution[] EmptyStats = new StatContribution[0];

        /// <summary>세트 기여를 모으는 재사용 버퍼. 라운드마다 부르므로 매번 새로 만들지 않는다.</summary>
        private readonly List<StatContribution> setBuffer = new List<StatContribution>(8);

        private bool TryGetTrack(UpgradeSlot slot, out UpgradeTrack track)
        {
            if (RequireSlot(slot) == UpgradeSlot.Armor)
            {
                track = catalog.Armor;
                return true;
            }

            return catalog.TryGetWeaponTrack(EquippedWeapon, out track);
        }

        public override string ToString() => $"강화 무기 {WeaponLevel}강 · 방어구 {ArmorLevel}강 · 세트 {SetLevel}강";
    }
}
