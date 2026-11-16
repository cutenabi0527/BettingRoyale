using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>읽기 전용 표시 캐시. 로컬/복제 어댑터만 갱신하며 전투 판정은 소유하지 않는다.</summary>
    public readonly struct HudVitals : IEquatable<HudVitals>
    {
        public readonly CombatantId Combatant;
        public readonly float Health, MaxHealth, RechargeDuration, RechargeRemaining;
        public readonly int Charges, MaxCharges;
        public readonly bool Downed, DamageSuppressed;
        public readonly float InvulnerabilityRemaining;
        public readonly float Shield;
        public readonly int RageTarget;
        public readonly IReadOnlyList<HudStatusEffect> Effects;
        public HudVitals(CombatantId combatant, float health, float maxHealth, int charges,
            int maxCharges, float rechargeDuration, float rechargeRemaining, bool downed, bool damageSuppressed=false,
            float invulnerabilityRemaining=0, IReadOnlyList<HudStatusEffect> effects=null, float shield=0f, int rageTarget=0)
        {
            RageTarget = rageTarget;
            Combatant = combatant; Health = health; MaxHealth = maxHealth; Shield = Math.Max(0f, shield);
            Charges = charges; MaxCharges = maxCharges; RechargeDuration = rechargeDuration;
            RechargeRemaining = rechargeRemaining; Downed = downed;
            DamageSuppressed=damageSuppressed;InvulnerabilityRemaining=invulnerabilityRemaining;Effects=effects;
        }
        public bool Equals(HudVitals v) => Combatant.Equals(v.Combatant) && Health == v.Health
            && RageTarget == v.RageTarget && Shield == v.Shield && MaxHealth == v.MaxHealth && Charges == v.Charges && MaxCharges == v.MaxCharges
            && RechargeDuration == v.RechargeDuration && RechargeRemaining == v.RechargeRemaining && Downed == v.Downed
            && DamageSuppressed==v.DamageSuppressed && InvulnerabilityRemaining==v.InvulnerabilityRemaining
            && ReferenceEquals(Effects, v.Effects);
    }

    public static class LocalHudVitals
    {
        private static int owner;
        public static bool Available { get; private set; }
        public static HudVitals Latest { get; private set; }
        public static void Set(int source, in HudVitals value) { owner = source; Latest = value; Available = true; }
        public static void Clear(int source) { if (owner == source) Reset(); }
        public static void Reset() { Available = false; Latest = default; owner = 0; }
    }

    /// <summary>표시 국면을 공유한다. 게임 상태를 변경하지 않는다. 단독 Lab은 전투 표시를 사용한다.</summary>
    public static class MatchHudVisibility
    {
        public static bool Visible { get; set; } = true;
        /// <summary>월드 캐릭터 위 표시는 정산 창을 덮지 않는다.</summary>
        public static bool WorldActorsVisible { get; set; } = true;
        public static bool Tactical { get; set; } = true;

        /// <summary>
        /// 자기장 표시. <b><see cref="Tactical"/> 과 따로 둔다</b> — 훈련장은 미니맵을 켠 채
        /// 자기장만 내려야 하는데, 한 스위치로 묶으면 둘 중 하나를 포기해야 한다 (S-UX-4PCQAQ).
        /// </summary>
        public static bool Zone { get; set; } = true;

        /// <summary>
        /// 훈련 세션 표시 모드. 켜져 있는 동안 중앙 배너는 라운드·타이머 대신 훈련 표시를 쓰고,
        /// 매치 국면 스냅샷은 가시성을 바꾸지 못한다 (S-UX-4PCQAQ · S-GD-YQ2RER).
        /// </summary>
        private static bool networkTraining, localTraining;
        public static bool Training
        {
            get => networkTraining || localTraining;
            set { bool before = Training; networkTraining = value; UpdateTrainingPresentation(before); }
        }
        // 오프라인 세션의 수명은 온라인 코디네이터가 덮어쓰지 않는다.
        public static bool LocalTraining
        {
            get => localTraining;
            set { bool before = Training; localTraining = value; UpdateTrainingPresentation(before); }
        }
        private static void UpdateTrainingPresentation(bool before)
        {
            if (before == Training) return;
            if (Training) { Visible = true; WorldActorsVisible = true; Tactical = true; Zone = false; }
            else { TrainingPlayerCount = 0; Zone = Tactical; }
        }

        /// <summary>훈련 배너에 쓸 현재 접속 인원. 값의 주인은 훈련 세션 계약이다.</summary>
        public static int TrainingPlayerCount { get; set; }

        public static void Reset()
        {
            networkTraining = localTraining = false;
            Visible = true; WorldActorsVisible = true; Tactical = true; Zone = true; TrainingPlayerCount = 0;
        }
    }
}
