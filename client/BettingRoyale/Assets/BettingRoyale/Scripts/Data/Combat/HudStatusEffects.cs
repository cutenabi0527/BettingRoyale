using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>HUD 전용 분류. 색은 이 분류로만 선택하고 게임플레이 판정에는 사용하지 않는다.</summary>
    public enum HudStatusEffectCategory
    {
        GeneralBuff,
        Ability,
        Debuff,
        Condition,
        Special,
    }

    public readonly struct HudStatusEffect
    {
        public readonly int Icon;
        public readonly bool IsDebuff;
        public readonly int SourceId;
        public readonly string EffectId;
        public readonly HudStatusEffectCategory Category;
        public readonly int Stacks;
        public readonly bool IsUnlimited;
        public readonly float NormalizedRemaining;
        // 하위 호출자는 음수 무한 표현을 계속 사용할 수 있다. 새 경로는 IsUnlimited을 함께 채운다.
        public readonly float Remaining;

        /// <summary>
        /// 이 효과를 켜려면 누를 입력 액션 id(<c>SettingsIds</c>). 준비된 액티브 축복이 싣는다 — 화면은 무한 표시 대신
        /// 지금 장치의 키(Q / Y)를 적는다(S-UX-AYM6K3). 없으면 null.
        /// </summary>
        public readonly string InputActionId;

        public HudStatusEffect(int icon, bool isDebuff, float remaining)
        {
            Icon = icon;
            IsDebuff = isDebuff;
            SourceId = 0;
            EffectId = null;
            Category = isDebuff ? HudStatusEffectCategory.Debuff : HudStatusEffectCategory.GeneralBuff;
            IsUnlimited = remaining < 0f;
            Remaining = remaining;
            NormalizedRemaining = IsUnlimited || remaining > 0f ? 1f : 0f;
            Stacks = 1;
            InputActionId = null;
        }

        public HudStatusEffect(int icon, int sourceId, string effectId, HudStatusEffectCategory category,
            float remaining, float normalizedRemaining, int stacks = 1, bool isUnlimited = false,
            string inputActionId = null)
        {
            Icon = icon;
            IsDebuff = category == HudStatusEffectCategory.Debuff;
            SourceId = sourceId;
            EffectId = effectId;
            Category = category;
            IsUnlimited = isUnlimited;
            Remaining = isUnlimited ? 0f : remaining;
            NormalizedRemaining = Clamp01(normalizedRemaining);
            Stacks = stacks < 1 ? 1 : stacks;
            InputActionId = inputActionId;
        }

        private static float Clamp01(float value) => float.IsNaN(value) || value <= 0f
            ? 0f
            : value >= 1f ? 1f : value;
    }

    /// <summary>
    /// 효과 인스턴스의 활성화 순서(FIFO)를 프레임 사이에 기억한다.
    /// 생산자가 매 프레임 표시 목록을 다시 만들어도 먼저 켜진 효과가 앞에 남고,
    /// 이미 켜져 있던 효과의 갱신(남은 시간·중첩 재발동)은 자리를 바꾸지 않는다.
    /// 새로 켜진 효과만 뒤에 붙고, 사라진 효과는 순서를 버려 다시 켜지면 맨 뒤로 간다.
    /// 표시 전용 보조 상태이며 게임플레이 판정·수치에는 쓰지 않는다.
    /// </summary>
    public sealed class HudStatusEffectOrder
    {
        private readonly Dictionary<(int Source, string Effect), int> sequences = new();
        private readonly List<(int Source, string Effect)> present = new();
        private readonly List<(int Source, string Effect)> removed = new();
        private int next;

        /// <summary>라운드 시작처럼 일시 효과가 전부 풀리는 지점에서 순서도 함께 비운다 (S-GD-AN554W).</summary>
        public void Clear() { sequences.Clear(); present.Clear(); removed.Clear(); next = 0; }

        /// <summary>받은 목록을 활성화 순서로 제자리 정렬한다. 항목을 더하거나 빼지 않는다.</summary>
        public void Arrange(List<HudStatusEffect> effects)
        {
            if (effects == null) return;
            present.Clear();
            for (int i = 0; i < effects.Count; i++)
            {
                var key = Key(effects[i]);
                present.Add(key);
                if (!sequences.ContainsKey(key)) sequences[key] = next++;
            }

            removed.Clear();
            foreach (var pair in sequences)
            {
                if (!present.Contains(pair.Key)) removed.Add(pair.Key);
            }
            for (int i = 0; i < removed.Count; i++) sequences.Remove(removed[i]);
            if (sequences.Count == 0) next = 0;

            // 목록이 짧고 대부분 이미 정렬돼 있어 할당 없는 삽입 정렬이 가장 싸고 안정적이다.
            for (int i = 1; i < effects.Count; i++)
            {
                HudStatusEffect effect = effects[i];
                int order = sequences[Key(effect)];
                int j = i - 1;
                while (j >= 0 && sequences[Key(effects[j])] > order) { effects[j + 1] = effects[j]; j--; }
                effects[j + 1] = effect;
            }
        }

        /// <summary>같은 근원의 같은 효과 키는 같은 인스턴스다. 갱신·재발동은 이 키로 합쳐진다.</summary>
        private static (int, string) Key(in HudStatusEffect effect)
            => (effect.SourceId, effect.EffectId ?? string.Empty);
    }

    public interface ICombatShieldSource
    {
        float CurrentShield { get; }
    }

    public interface ICombatShieldPresentationDriver
    {
        void ApplyShieldPresentation(float shield);
    }

    public interface ICombatHudEffectSource
    {
        bool DamageSuppressed { get; }
        float InvulnerabilityRemaining { get; }
    }
    /// <summary>지금 조작을 붙들고 있는 화면. 하나라도 있으면 전투 입력이 멈춘다.</summary>
    /// <remarks>
    /// ⚠️ <b>창을 띄우는 화면은 반드시 여기 등록한다.</b> 등록하지 않으면 창이 열린 채로 캐릭터가
    /// 움직이고, 패드에서는 그 이동 입력이 창을 닫아 버린다 — 준비맵의 상점·강화·룰렛이 정확히
    /// 그랬다 (S15P21A606-215, 박하민 제보).
    /// <para>
    /// 소유자를 따로 두는 이유는 <b>겹쳐 열려도 각자 자기 것만 내리게</b> 하기 위해서다.
    /// 하나의 bool 이면 나중에 닫히는 창이 남의 잠금까지 풀어 버린다.
    /// </para>
    /// </remarks>
    [Flags] public enum HudInputOwner
    {
        None=0, Settings=1, Stats=2, Blessings=4, SelectionModal=8,
        /// <summary>준비맵 시설 창 — 상점·강화·룰렛 (S15P21A606-215).</summary>
        Facility=16,
        /// <summary>준비·전투 전투 현황 HUD의 행/요소 탐색.</summary>
        Dashboard=32,
    }
    public static class HudInputCapture
    {
        private static HudInputOwner owners;
        public static bool SelectionModal => (owners & HudInputOwner.SelectionModal)!=0;
        public static bool Active => owners!=HudInputOwner.None;
        public static void Set(HudInputOwner owner,bool value) { if (value) owners|=owner; else owners&=~owner; }
        public static void Reset() => owners=HudInputOwner.None;
    }
}
