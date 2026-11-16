using System.Threading.Tasks;
using BettingRoyale.Data.Scenes;

namespace BettingRoyale.Managers
{
    /// <summary>
    /// Additive 매치 맵 로딩 경계. 매치 흐름은 Built-in/Addressables API 대신 이 계약만 사용한다.
    /// </summary>
    public interface IMatchSceneProvider
    {
        bool IsLoaded(MatchSceneKey key);

        Task<MatchSceneLoadResult> LoadAsync(MatchSceneKey key);

        Task<MatchSceneLoadResult> UnloadAsync(MatchSceneKey key);
    }
}
