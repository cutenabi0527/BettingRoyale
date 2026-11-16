namespace BettingRoyale.Core
{
    /// <summary>
    /// 모든 게임 이벤트가 구현하는 마커 인터페이스. struct 로 정의해 GC 부담을 줄인다.
    ///
    /// <b>왜 <see cref="EventBus"/> 와 다른 파일에 있는가</b> — <c>EventBus</c> 는 <c>UnityEngine</c> 을
    /// 참조하지만 이 마커는 참조하지 않는다. 순수 도메인 계약(<c>Scripts/Data/Contracts/</c>)이 이 인터페이스를
    /// 구현하는데, 그 계약은 Unity 밖 dotnet 테스트에서도 컴파일돼야 한다 (D-058).
    /// 한 파일에 두면 테스트가 <c>UnityEngine</c> 을 끌어오게 되어 컴파일되지 않는다.
    /// </summary>
    public interface IGameEvent { }
}
