using BettingRoyale.Core;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// <b>부위 하나의 다음 단계를 요청한다</b> (S-GD-A2YDDJ · S-GD-Z9CJ4H).
    ///
    /// <para>
    /// 기존 <see cref="WeaponUpgradeCommand"/> 는 부위가 없던 시절의 계약이다. 지우지 않고
    /// <b>무기 부위 별칭으로 남겼다</b> — 개발 HUD(S-QA-Q4MFVT)가 그것을 발행하고 있고,
    /// 원문 보존 규칙상 의미를 바꿔 덮어쓰지 않는다.
    /// </para>
    ///
    /// <para>보내는 사람은 전송 계층이 식별한다 — <c>PlayerId</c> 를 담지 않는다 (규약 §5).</para>
    /// </summary>
    public readonly struct UpgradeSlotCommand : IGameEvent
    {
        public readonly UpgradeSlot Slot;

        public UpgradeSlotCommand(UpgradeSlot slot)
        {
            Slot = slot;
        }
    }

    /// <summary>
    /// 강화 요청이 거절된 사유.
    ///
    /// <para>
    /// <b>도메인이 실제로 만들어내는 값만 있다</b> (규약 §6). <b>확률 실패는 여기 없다</b> — 거절이 아니라
    /// 시도가 이루어진 결과이기 때문이다(S15P21A606-304). 확률 실패는 <see cref="UpgradeStateChanged"/> 의
    /// "단계 그대로 · 잔액 감소" 로 드러난다(S-UX-HQ92B5).
    /// </para>
    ///
    /// <para><b>⚠️ 값은 뒤에만 붙인다.</b> 회선을 넘는다.</para>
    /// </summary>
    public enum UpgradeRejectReason
    {
        None = 0,

        /// <summary>준비 단계가 아니다. 전투 중이거나 매치가 끝났다.</summary>
        NotPreparation = 1,

        /// <summary>모루 구역 밖이다 (S-GD-66GTWA · S-CLI-9QYFCT). 위치를 모를 때도 이 값이다.</summary>
        NotInRange = 2,

        /// <summary>골드가 모자란다. <b>단계도 골드도 그대로다.</b></summary>
        NotEnoughGold = 3,

        /// <summary>이미 최대 단계다.</summary>
        MaxLevelReached = 4,

        /// <summary>
        /// 이 무기 종류의 강화 표가 없다 — 둔기처럼 표를 아직 안 만든 무기이거나,
        /// 전투원이 없어 무기를 모르는 상태다. <b>데이터가 없는 것이지 조작이 잘못된 것이 아니다.</b>
        /// </summary>
        NoTrack = 5,

        /// <summary>모르는 부위다. 정상 클라이언트는 만들 수 없는 값이다.</summary>
        UnknownSlot = 6,

        /// <summary>먼저 이번 라운드의 축복 선택을 확정해야 한다.</summary>
        SelectionRequired = 7,
    }

    /// <summary>강화 요청이 판정 전에 거절됐다. 골드와 단계는 변하지 않는다.</summary>
    public readonly struct UpgradeSlotRejected : IGameEvent
    {
        public readonly int PlayerId;
        public readonly bool IsMine;
        public readonly UpgradeSlot Slot;
        public readonly UpgradeRejectReason Reason;

        public UpgradeSlotRejected(int playerId, bool isMine, UpgradeSlot slot, UpgradeRejectReason reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Slot = slot;
            Reason = reason;
        }
    }

    /// <summary>
    /// <b>두 부위의 강화 상태 전부</b> (S-GD-A2YDDJ). 이벤트를 놓쳐도 다음 상태 하나로 화면이 복구된다
    /// (규약 §9).
    ///
    /// <para>
    /// <b>성공 이벤트를 따로 두지 않는다.</b> 성공은 이 상태(단계·잔액이 바뀐 것)와
    /// <see cref="GoldChanged"/> 로 드러난다 — 상점 구매가 같은 방식이다. 거절은
    /// <see cref="UpgradeSlotRejected"/> 로 오므로 규약 §6 의 "성공·거부 짝" 은 지켜진다.
    /// <b>확률 실패</b>(S15P21A606-304)는 단계가 그대로인 채 잔액만 줄어든 상태로 온다 — 화면이 앞뒤를 견준다
    /// (S-UX-HQ92B5).
    /// </para>
    /// </summary>
    public readonly struct UpgradeStateChanged : IGameEvent
    {
        public readonly int PlayerId;
        public readonly bool IsMine;
        public readonly int Round;

        /// <summary>이 사람이 실제로 들고 있는 무기. <b>강화 표가 이 값으로 갈린다.</b></summary>
        public readonly WeaponType EquippedWeapon;

        public readonly int WeaponLevel;
        public readonly int WeaponMaxLevel;
        public readonly int ArmorLevel;
        public readonly int ArmorMaxLevel;

        /// <summary>세트 단계 — <b>두 부위 중 낮은 쪽</b>이다. 세트는 누적된다.</summary>
        public readonly int SetLevel;

        public readonly int Gold;

        public readonly bool WeaponHasNextLevel;
        public readonly int WeaponNextCostGold;
        public readonly UpgradeRejectReason WeaponBlockedBy;

        public readonly bool ArmorHasNextLevel;
        public readonly int ArmorNextCostGold;
        public readonly UpgradeRejectReason ArmorBlockedBy;

        /// <summary>
        /// 무기 <b>다음 단계</b> 성공률(0~100). 표(<c>upgrade_levels.success_rate</c>)에서 온다 (S15P21A606-304).
        /// 다음 단계가 없으면 0 이다.
        /// </summary>
        public readonly int WeaponNextSuccessRatePercent;

        /// <summary>방어구 <b>다음 단계</b> 성공률(0~100). 다음 단계가 없으면 0 이다.</summary>
        public readonly int ArmorNextSuccessRatePercent;

        public bool CanUpgradeWeapon => WeaponBlockedBy == UpgradeRejectReason.None;

        public bool CanUpgradeArmor => ArmorBlockedBy == UpgradeRejectReason.None;

        public UpgradeStateChanged(
            int playerId,
            bool isMine,
            int round,
            WeaponType equippedWeapon,
            int weaponLevel,
            int weaponMaxLevel,
            int armorLevel,
            int armorMaxLevel,
            int setLevel,
            int gold,
            bool weaponHasNextLevel,
            int weaponNextCostGold,
            UpgradeRejectReason weaponBlockedBy,
            bool armorHasNextLevel,
            int armorNextCostGold,
            UpgradeRejectReason armorBlockedBy,
            int weaponNextSuccessRatePercent,
            int armorNextSuccessRatePercent)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Round = round;
            EquippedWeapon = equippedWeapon;
            WeaponLevel = weaponLevel;
            WeaponMaxLevel = weaponMaxLevel;
            ArmorLevel = armorLevel;
            ArmorMaxLevel = armorMaxLevel;
            SetLevel = setLevel;
            Gold = gold;
            WeaponHasNextLevel = weaponHasNextLevel;
            WeaponNextCostGold = weaponNextCostGold;
            WeaponBlockedBy = weaponBlockedBy;
            ArmorHasNextLevel = armorHasNextLevel;
            ArmorNextCostGold = armorNextCostGold;
            ArmorBlockedBy = armorBlockedBy;
            WeaponNextSuccessRatePercent = weaponNextSuccessRatePercent;
            ArmorNextSuccessRatePercent = armorNextSuccessRatePercent;
        }

        /// <summary>그 부위의 다음 단계 성공률(0~100). 올릴 수 없으면 0 이다.</summary>
        public int NextSuccessRateOf(UpgradeSlot slot) =>
            slot == UpgradeSlot.Armor ? ArmorNextSuccessRatePercent : WeaponNextSuccessRatePercent;

        /// <summary>그 부위의 지금 단계.</summary>
        public int LevelOf(UpgradeSlot slot) => slot == UpgradeSlot.Armor ? ArmorLevel : WeaponLevel;

        /// <summary>그 부위의 최대 단계.</summary>
        public int MaxLevelOf(UpgradeSlot slot) => slot == UpgradeSlot.Armor ? ArmorMaxLevel : WeaponMaxLevel;

        /// <summary>그 부위의 다음 단계 비용. 올릴 수 없으면 0 이다.</summary>
        public int NextCostOf(UpgradeSlot slot) =>
            slot == UpgradeSlot.Armor ? ArmorNextCostGold : WeaponNextCostGold;

        /// <summary>그 부위를 지금 올릴 수 있는가.</summary>
        public bool CanUpgrade(UpgradeSlot slot) =>
            slot == UpgradeSlot.Armor ? CanUpgradeArmor : CanUpgradeWeapon;

        /// <summary>그 부위가 막혀 있으면 사유.</summary>
        public UpgradeRejectReason BlockedBy(UpgradeSlot slot) =>
            slot == UpgradeSlot.Armor ? ArmorBlockedBy : WeaponBlockedBy;

        /// <summary>
        /// <b>아직 열려 있는 부위만</b> 이 사유로 막는다.
        ///
        /// <para>
        /// 도메인은 위치를 모른다 — 구역 판정은 게이트가 한다(S-CLI-9QYFCT). 그래서 게이트가
        /// 도메인이 만든 상태를 받아 <b>덧씌우는</b> 자리가 필요하다. <b>이미 막힌 부위의 사유는
        /// 덮지 않는다</b> — 골드가 모자란 것을 "구역 밖" 으로 바꿔 보여주면 화면이 거짓말을 한다.
        /// </para>
        /// </summary>
        public UpgradeStateChanged WithBlockedIfOpen(UpgradeRejectReason reason) =>
            new UpgradeStateChanged(
                PlayerId,
                IsMine,
                Round,
                EquippedWeapon,
                WeaponLevel,
                WeaponMaxLevel,
                ArmorLevel,
                ArmorMaxLevel,
                SetLevel,
                Gold,
                WeaponHasNextLevel,
                WeaponNextCostGold,
                WeaponBlockedBy == UpgradeRejectReason.None ? reason : WeaponBlockedBy,
                ArmorHasNextLevel,
                ArmorNextCostGold,
                ArmorBlockedBy == UpgradeRejectReason.None ? reason : ArmorBlockedBy,
                WeaponNextSuccessRatePercent,
                ArmorNextSuccessRatePercent);
    }
}
