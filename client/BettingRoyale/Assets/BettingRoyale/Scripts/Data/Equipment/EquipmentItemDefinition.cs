using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 장비 항목의 불변 데이터. 실제 카탈로그 공급 방식은 Inspector 어댑터에서 시작해 Addressables로 교체한다.
    /// </summary>
    public readonly struct EquipmentItemDefinition
    {
        public readonly int ItemId;
        public readonly EquipmentItemKind Kind;
        public readonly EquipmentArchetype Archetype;
        public readonly int VisualId;
        public readonly WeaponType GameplayWeapon;
        public readonly int MaxStack;

        public EquipmentItemDefinition(
            int itemId,
            EquipmentItemKind kind,
            EquipmentArchetype archetype,
            int visualId,
            WeaponType gameplayWeapon,
            int maxStack)
        {
            ItemId = itemId;
            Kind = kind;
            Archetype = archetype;
            VisualId = visualId;
            GameplayWeapon = gameplayWeapon;
            MaxStack = maxStack;
        }

        public bool IsValid => ItemId > 0 && Kind != EquipmentItemKind.None && VisualId > 0;
        public bool IsStackableConsumable => Kind == EquipmentItemKind.Consumable && MaxStack > 0;
    }
}
