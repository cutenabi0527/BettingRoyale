using System.Collections.Generic;

namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// <c>gamedata.bin</c> 을 도메인 타입으로 옮긴 결과 (S15P21A606-201).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>실패를 예외로 던지지 않는다.</b> 기동 경로에서 쓰이므로 호출부가 "게임에 들어갈 수 있는가" 를
    /// 판정해야 하는데, 예외로 만들면 그 판정이 try/catch 모양으로 흩어진다.
    /// <see cref="RemoteContentBootstrap"/> 의 <c>Outcome</c> 과 같은 방식이다.
    /// </para>
    /// <para>
    /// <see cref="Warnings"/> 는 실패가 아니다 — <b>읽기는 성공했지만 일부를 버렸다</b>는 뜻이다.
    /// 조용히 버리면 "왜 이 축복이 안 뜨지" 를 런타임에 되묻게 되므로 목록으로 남긴다.
    /// </para>
    /// </remarks>
    public sealed class GameDataLoadResult
    {
        private GameDataLoadResult(
            bool success,
            string failure,
            GameContent content,
            IReadOnlyList<string> warnings)
        {
            Success = success;
            Failure = failure ?? string.Empty;
            Content = content;
            Warnings = warnings ?? System.Array.Empty<string>();
        }

        public bool Success { get; }

        /// <summary>실패 사유. 성공이면 빈 문자열이다.</summary>
        public string Failure { get; }

        /// <summary>성공했을 때만 채워진다.</summary>
        public GameContent Content { get; }

        /// <summary>버린 행에 대한 설명. 성공해도 비어 있지 않을 수 있다.</summary>
        public IReadOnlyList<string> Warnings { get; }

        public static GameDataLoadResult Ok(GameContent content, IReadOnlyList<string> warnings) =>
            new GameDataLoadResult(true, null, content, warnings);

        public static GameDataLoadResult Fail(string reason) =>
            new GameDataLoadResult(false, reason, null, null);
    }
}
