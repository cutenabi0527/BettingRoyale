namespace BettingRoyale.Data.Combat
{
    /// <summary>대시 명령을 처리한다. 다른 명령 핸들러와 독립적으로 구현·조합할 수 있다 (S-GD-7DRYPY).</summary>
    public interface IDashCommandHandler
    {
        void Handle(in DashCommand command);
    }
}
