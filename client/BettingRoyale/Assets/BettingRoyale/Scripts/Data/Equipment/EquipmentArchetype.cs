namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 게임플레이 무기 타입과 독립인 장비 외형 분류.
    /// Sword/Bow 외 항목은 현재 공격 수치를 추가하지 않는 표현 확장이다 (S-ART-XTV89G).
    /// </summary>
    public enum EquipmentArchetype
    {
        None = 0,
        Sword = 1,
        Shield = 2,
        GreatSword = 3,
        Bow = 4,
        Firearm = 5,
        Spear = 6,
        Mace = 7,
        Staff = 8,
        Accessory = 9,
    }
}
