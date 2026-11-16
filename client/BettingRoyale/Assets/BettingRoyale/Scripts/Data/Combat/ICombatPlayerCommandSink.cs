namespace BettingRoyale.Data.Combat
{
    /// <summary>로컬 플레이어의 디버그 상태·외형 요청을 해당 NetworkObject의 Host 권위로 전달한다.</summary>
    public interface ICombatPlayerCommandSink
    {
        bool AcceptsLocalCommands { get; }

        void RequestCharacterStep(int step);
        void RequestOutfitStep(int step);
        void RequestToggleDownedState();
    }
}
