namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 로컬 장치 입력의 소비 경계. 네트워크 컴포넌트가 같은 오브젝트에 있으면
    /// LocalCombatInputDriver는 Gameplay 명령을 직접 실행하지 않고 이 경계로 전달한다.
    /// </summary>
    public interface ICombatMotionInputSink
    {
        bool AcceptsLocalInput { get; }
        void Submit(in CombatMotionInputFrame inputFrame);
    }
}
