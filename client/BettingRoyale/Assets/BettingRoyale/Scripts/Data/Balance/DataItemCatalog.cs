using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Throwing;

namespace BettingRoyale.Data.Balance
{
    /// <summary>
    /// 엑셀 <c>items</c>·<c>throwables</c> 시트로 만든 아이템 정의 카탈로그 (S-CLI-QXRTWX).
    /// 인벤토리·상점 게이트·투척 경계가 <b>같은 인스턴스</b>를 받는다 (D-CLI-R69V68).
    /// </summary>
    public sealed class DataItemCatalog : IEquipmentItemCatalog, IEquipmentItemCarryLimitCatalog, IThrowingItemCatalog
    {
        private readonly Dictionary<int, EquipmentItemDefinition> definitions = new Dictionary<int, EquipmentItemDefinition>();
        private readonly Dictionary<int, int> carryLimits = new Dictionary<int, int>();
        private readonly Dictionary<int, ThrowingItemDefinition> throwables = new Dictionary<int, ThrowingItemDefinition>();
        private readonly Dictionary<int, string> names = new Dictionary<int, string>();
        private readonly List<int> shelfItemIds = new List<int>();
        private readonly List<ThrowingItemDefinition> allThrowables = new List<ThrowingItemDefinition>();

        public void AddItem(in EquipmentItemDefinition definition, string name, int carryLimit, bool onShelf)
        {
            if (!definition.IsValid) throw new ArgumentException($"아이템 {definition.ItemId} 정의가 유효하지 않습니다.", nameof(definition));
            if (definitions.ContainsKey(definition.ItemId)) throw new ArgumentException($"아이템 {definition.ItemId} 이 중복입니다.", nameof(definition));
            definitions[definition.ItemId] = definition;
            names[definition.ItemId] = name ?? string.Empty;
            if (carryLimit > 0) carryLimits[definition.ItemId] = carryLimit;
            if (onShelf) shelfItemIds.Add(definition.ItemId);
        }

        public void AddThrowable(in ThrowingItemDefinition definition)
        {
            throwables[definition.ItemId] = definition;
            allThrowables.Add(definition);
        }

        public int Count => definitions.Count;

        /// <summary>상점 진열 ↔ 정의 정합 검사가 쓰는 진열 대상(구매 가능 소모품·장신구).</summary>
        public IReadOnlyList<int> ShelfItemIds => shelfItemIds;

        public IReadOnlyList<ThrowingItemDefinition> AllThrowables => allThrowables;

        public IEnumerable<int> ItemIds => definitions.Keys;

        public string NameOf(int itemId) => names.TryGetValue(itemId, out string name) ? name : string.Empty;

        public bool TryGet(int itemId, out EquipmentItemDefinition definition) => definitions.TryGetValue(itemId, out definition);

        public bool TryGetCarryLimit(int itemId, out int totalCarryLimit) => carryLimits.TryGetValue(itemId, out totalCarryLimit);

        public bool TryGetThrowing(int itemId, out ThrowingItemDefinition definition)
        {
            if (throwables.TryGetValue(itemId, out ThrowingItemDefinition raw))
            {
                // CombatLab 실측 조정 층은 데이터 위에도 그대로 얹힌다 (ThrowTuning).
                definition = ThrowTuning.Apply(raw);
                return true;
            }

            definition = default;
            return false;
        }
    }

    /// <summary>
    /// 엑셀 <c>item_effects</c> 시트 — 소모품 효과 묶음과 장신구 스탯 (S-GD-Q75D75 · S-GD-YMKKDJ).
    /// </summary>
    public sealed class ConsumableEffectTable
    {
        private readonly Dictionary<int, List<ConsumableEffect>> effectsByItem = new Dictionary<int, List<ConsumableEffect>>();
        private readonly Dictionary<int, List<AccessoryStat>> accessoryStats = new Dictionary<int, List<AccessoryStat>>();

        public readonly struct AccessoryStat
        {
            public AccessoryStat(string statId, float value, string weaponFilter)
            {
                StatId = statId;
                Value = value;
                WeaponFilter = weaponFilter ?? string.Empty;
            }

            public string StatId { get; }
            public float Value { get; }

            /// <summary>빈칸 = 모든 무기, <c>melee</c>/<c>ranged</c>/<c>sword</c>/<c>bow</c>.</summary>
            public string WeaponFilter { get; }

            public bool AppliesTo(WeaponType weapon, bool isRanged)
            {
                switch (WeaponFilter)
                {
                    case "": return true;
                    case "melee": return !isRanged;
                    case "ranged": return isRanged;
                    case "sword": return weapon == WeaponType.Sword;
                    case "bow": return weapon == WeaponType.Bow;
                    default: return false;
                }
            }
        }

        public void AddEffect(int itemId, in ConsumableEffect effect)
        {
            if (!effectsByItem.TryGetValue(itemId, out List<ConsumableEffect> list)) effectsByItem[itemId] = list = new List<ConsumableEffect>(2);
            list.Add(effect);
        }

        public void AddAccessoryStat(int itemId, in AccessoryStat stat)
        {
            if (!accessoryStats.TryGetValue(itemId, out List<AccessoryStat> list)) accessoryStats[itemId] = list = new List<AccessoryStat>(2);
            list.Add(stat);
        }

        public bool TryGetConsumableEffects(int itemId, out IReadOnlyList<ConsumableEffect> effects)
        {
            if (effectsByItem.TryGetValue(itemId, out List<ConsumableEffect> list) && list.Count > 0)
            {
                effects = list;
                return true;
            }

            effects = null;
            return false;
        }

        /// <summary>상자 과잉 보상 묶음 (item_id = -1). 없으면 빈 목록.</summary>
        public IReadOnlyList<ConsumableEffect> ChestOverflowEffects =>
            effectsByItem.TryGetValue(BettingRoyale.Data.Remote.BalanceTableSchema.ChestOverflowItemId, out List<ConsumableEffect> list)
                ? list
                : (IReadOnlyList<ConsumableEffect>)Array.Empty<ConsumableEffect>();

        public bool HasAccessory(int itemId) => accessoryStats.ContainsKey(itemId);

        /// <summary>장신구 하나가 시트에 넣을 기여. 정의가 없으면 빈 목록.</summary>
        public IReadOnlyList<StatContribution> BuildAccessoryStats(int itemId, WeaponType weapon, bool isRanged, string sourceId)
        {
            if (!accessoryStats.TryGetValue(itemId, out List<AccessoryStat> stats)) return Array.Empty<StatContribution>();
            var result = new List<StatContribution>(stats.Count);
            for (int i = 0; i < stats.Count; i++)
            {
                if (!stats[i].AppliesTo(weapon, isRanged)) continue;
                result.Add(new StatContribution(stats[i].StatId, stats[i].Value, StatStackMode.Add, sourceId));
            }

            return result;
        }
    }
}
