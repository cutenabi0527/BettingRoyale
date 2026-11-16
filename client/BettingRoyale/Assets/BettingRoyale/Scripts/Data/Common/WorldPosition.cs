namespace BettingRoyale.Data.Common
{
    /// <summary>
    /// 월드 좌표. UI·미니맵이 위치를 그리는 데 쓴다.
    ///
    /// <b>왜 <c>Vector3</c> 가 아닌가</b> — <c>Scripts/Data/</c> 는 <c>UnityEngine</c> 을 참조하지 않는다(D-060).
    /// 참조하는 순간 Unity 밖 dotnet 테스트에서 이 레이어가 컴파일되지 않는다.
    /// <c>Vector3</c> 로의 변환은 경계(<c>Scripts/UI/</c>, <c>Scripts/Gameplay/</c>)에서 한다 —
    /// Photon 타입을 <c>PlayerSlot</c> 으로 바꿔 내보내는 것(D-009)과 같은 방식이다.
    ///
    /// <c>new Vector3(p.X, p.Y, p.Z)</c> 한 줄이면 된다.
    /// </summary>
    public readonly struct WorldPosition
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public WorldPosition(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}
