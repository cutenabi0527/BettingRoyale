namespace BettingRoyale.Data.Equipment
{
    /// <summary>현재 전투자의 가까운 월드 드롭 획득을 요청하는 경계.</summary>
    public interface IEquipmentPickupCommandSink
    {
        void RequestPickup();
    }
}
