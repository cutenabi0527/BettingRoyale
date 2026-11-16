namespace BettingRoyale.Data.Combat
{
    /// <summary>씬 로컬 디버그 오브젝트와 Host 권위 네트워크 상태를 연결한다.</summary>
    public interface ICombatSandboxStateBridge
    {
        void BindNetworkPlayer(
            ICombatMotionDriver motion,
            ICombatActionDriver actions,
            ICombatHealthDriver health,
            ICombatVisualDriver visual,
            ICombatPlayerCommandSink commands);

        void ConfigureSharedNetworkState(bool isAuthority, ICombatHealthDriver hostPlayer);
        CombatSandboxStateSnapshot CaptureSharedState();
        void ApplySharedState(in CombatSandboxStateSnapshot snapshot);
    }
}
