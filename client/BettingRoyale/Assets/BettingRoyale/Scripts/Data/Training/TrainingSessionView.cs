using System;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Contracts;

namespace BettingRoyale.Data.Training
{
    /// <summary>Host가 보낸 자기 연습 상태. 클라이언트가 구매·강화 결과를 미리 계산하지 않는다.</summary>
    public sealed class TrainingSessionView
    {
        public string SessionId { get; }
        public int Version { get; }
        public int PlayerId { get; }
        public int AcknowledgedSequence { get; }
        public bool PlayerDamageEnabled { get; }

        /// <summary>
        /// 지금 봇을 세워 둔 훈련장 <b>구역 조합</b> (S-GD-V9ZMN3). <see cref="PlayerDamageEnabled"/> 와 같은
        /// <b>방 전체 설정</b>이라 개인 스냅샷에 실어 보낸다 — 방마다 하나뿐이므로 누가 받아도 같은 값이다.
        /// 해석은 <see cref="TrainingBotRule"/> 이 한다.
        /// </summary>
        public int BotZones { get; }
        public int Gold { get; }
        public WeaponType Weapon { get; }
        public int WeaponLevel { get; }
        public int ArmorLevel { get; }
        public int WeaponNextCost { get; }
        public int ArmorNextCost { get; }
        public bool CanUpgradeWeapon { get; }
        public bool CanUpgradeArmor { get; }
        public string[] AcquiredIds { get; }
        public string Message { get; }
        public bool RequestRejected { get; }
        public ShopOfferView[] Offers { get; }

        /// <summary>
        /// 경제형 축복 시험 주기. <b>절대 null 이 아니다</b> — 주기가 없으면
        /// <see cref="TrainingBlessingTrialView.Empty"/> 다. 화면이 null 검사를 하지 않게 한다.
        /// </summary>
        public TrainingBlessingTrialView Trial { get; }

        /// <summary>
        /// 룰렛 스냅샷. <b>절대 null 이 아니다</b> — 바퀴가 없으면
        /// <see cref="TrainingRouletteView.Empty"/> 다.
        /// </summary>
        public TrainingRouletteView Roulette { get; }

        /// <remarks>
        /// <paramref name="trial"/>·<paramref name="roulette"/> 은 <b>마지막 선택 인자</b>다 —
        /// 그 칸을 모르는 기존 호출부가 그대로 컴파일되고, 그때는 빈 값이 들어간다.
        /// </remarks>
        public TrainingSessionView(string sessionId, int version, int playerId, int acknowledgedSequence,
            bool playerDamageEnabled, int gold, WeaponType weapon, int weaponLevel, int armorLevel,
            int weaponNextCost, int armorNextCost, bool canUpgradeWeapon, bool canUpgradeArmor,
            string[] acquiredIds, string message, bool requestRejected = false, ShopOfferView[] offers = null,
            TrainingBlessingTrialView trial = null, TrainingRouletteView roulette = null,
            int botZones = TrainingBotRule.Default)
        {
            SessionId = sessionId ?? string.Empty;
            Version = version;
            PlayerId = playerId;
            AcknowledgedSequence = acknowledgedSequence;
            PlayerDamageEnabled = playerDamageEnabled;
            Gold = gold;
            Weapon = weapon;
            WeaponLevel = weaponLevel;
            ArmorLevel = armorLevel;
            WeaponNextCost = weaponNextCost;
            ArmorNextCost = armorNextCost;
            CanUpgradeWeapon = canUpgradeWeapon;
            CanUpgradeArmor = canUpgradeArmor;
            AcquiredIds = acquiredIds == null ? Array.Empty<string>() : (string[])acquiredIds.Clone();
            Message = message ?? string.Empty;
            RequestRejected = requestRejected;
            Offers = offers == null ? Array.Empty<ShopOfferView>() : (ShopOfferView[])offers.Clone();
            Trial = trial ?? TrainingBlessingTrialView.Empty;
            Roulette = roulette ?? TrainingRouletteView.Empty;
            BotZones = TrainingBotRule.Clamp(botZones);
        }

        public static TrainingSessionView Capture(TrainingPracticeState state, int version,
            int acknowledgedSequence, bool playerDamageEnabled, string message, bool requestRejected = false,
            int botZones = TrainingBotRule.Default)
        {
            UpgradeStateChanged upgrade = state.BuildUpgrade();
            var acquired = new string[state.Acquired.Count];
            for (int i = 0; i < acquired.Length; i++) acquired[i] = state.Acquired[i].Id;
            return new TrainingSessionView(state.SessionId, version, state.PlayerId, acknowledgedSequence,
                playerDamageEnabled, state.Gold, state.Weapon, upgrade.WeaponLevel, upgrade.ArmorLevel,
                upgrade.WeaponNextCostGold, upgrade.ArmorNextCostGold, upgrade.CanUpgradeWeapon,
                upgrade.CanUpgradeArmor, acquired, message, requestRejected, state.BuildOffers().Offers,
                state.BuildTrialView(), state.BuildRouletteView(), botZones);
        }
    }
}
