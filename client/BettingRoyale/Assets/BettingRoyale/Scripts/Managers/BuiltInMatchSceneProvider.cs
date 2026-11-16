using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BettingRoyale.Data.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BettingRoyale.Managers
{
    /// <summary>
    /// Player Build에 포함된 맵 씬을 Additive로 로드한다.
    /// Addressable Scene 이관 시 이 구현만 교체하고 매치 흐름의 씬 키는 유지한다.
    /// </summary>
    public sealed class BuiltInMatchSceneProvider : IMatchSceneProvider
    {
        private readonly Dictionary<MatchSceneKey, Scene> loadedScenes = new();
        private readonly Dictionary<MatchSceneKey, Task<MatchSceneLoadResult>> pendingLoads = new();
        private readonly Dictionary<MatchSceneKey, Task<MatchSceneLoadResult>> pendingUnloads = new();
        private readonly IReadOnlyList<MatchSceneDescriptor> labMaps;

        public BuiltInMatchSceneProvider() { }

        /// <summary>Explicit CombatLab scope, separate from the default product allowlist.</summary>
        public BuiltInMatchSceneProvider(IReadOnlyList<MatchSceneDescriptor> combatLabMaps) => labMaps = combatLabMaps;

        private bool TryGet(MatchSceneKey key, out MatchSceneDescriptor descriptor) => labMaps == null
            ? MatchSceneCatalog.TryGet(key, out descriptor) : CombatLabMapCatalog.TryGet(labMaps, key, out descriptor);

        /// <summary>
        /// 이 provider가 직접 로드한 씬만 소유 씬으로 보고한다. Unity 전역 씬 목록에 같은 이름이
        /// 있다는 이유로 다른 provider의 씬을 입양하면, 정리 단계에서 남의 씬을 내리게 된다.
        /// </summary>
        public bool IsLoaded(MatchSceneKey key)
        {
            if (loadedScenes.TryGetValue(key, out Scene ownedScene))
            {
                if (ownedScene.IsValid() && ownedScene.isLoaded)
                {
                    return true;
                }

                loadedScenes.Remove(key);
            }
            return false;
        }

        public Task<MatchSceneLoadResult> LoadAsync(MatchSceneKey key)
        {
            if (!TryGet(key, out MatchSceneDescriptor descriptor))
            {
                return Task.FromResult(new MatchSceneLoadResult(
                    key,
                    string.Empty,
                    MatchSceneLoadResult.ResultCode.UnknownKey,
                    $"등록되지 않은 매치 씬 키입니다: {key}"));
            }

            if (IsLoaded(key))
            {
                return Task.FromResult(new MatchSceneLoadResult(
                    key,
                    descriptor.BuiltInSceneName,
                    MatchSceneLoadResult.ResultCode.AlreadyLoaded));
            }

            if (pendingLoads.TryGetValue(key, out Task<MatchSceneLoadResult> pendingLoad))
            {
                return pendingLoad;
            }

            if (pendingUnloads.ContainsKey(key))
            {
                return Task.FromResult(CreateBusyResult(key, descriptor.BuiltInSceneName));
            }

            List<Scene> foreignScenes = FindLoadedScenes(descriptor.BuiltInSceneName);
            if (foreignScenes.Count > 0)
            {
                MatchSceneLoadResult.ResultCode code = foreignScenes.Count == 1
                    ? MatchSceneLoadResult.ResultCode.AlreadyLoaded
                    : MatchSceneLoadResult.ResultCode.Failed;
                return Task.FromResult(new MatchSceneLoadResult(
                    key,
                    descriptor.BuiltInSceneName,
                    code,
                    $"다른 소유자가 같은 맵 씬을 {foreignScenes.Count}개 로드했습니다. " +
                    DescribeScenes(foreignScenes)));
            }

            int buildRegistrationCount = CountBuildRegistrations(descriptor.BuiltInSceneName);
            if (buildRegistrationCount > 1)
            {
                return Task.FromResult(new MatchSceneLoadResult(
                    key,
                    descriptor.BuiltInSceneName,
                    MatchSceneLoadResult.ResultCode.Failed,
                    $"Build Settings에 맵 씬이 {buildRegistrationCount}회 중복 등록되어 있습니다: " +
                    descriptor.BuiltInSceneName));
            }

            bool canLoad = buildRegistrationCount == 1;
#if UNITY_EDITOR
            canLoad |= labMaps != null && descriptor.Phase == MatchScenePhase.Combat
                && descriptor.BuiltInSceneName.StartsWith(CombatLabMapCatalog.MapsPath, StringComparison.Ordinal)
                && UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(descriptor.BuiltInSceneName) != null;
#endif
            if (!canLoad)
            {
                return Task.FromResult(new MatchSceneLoadResult(
                    key,
                    descriptor.BuiltInSceneName,
                    MatchSceneLoadResult.ResultCode.MissingFromBuild,
                    $"Player Build에 매치 씬이 없습니다: {descriptor.BuiltInSceneName}"));
            }

            Task<MatchSceneLoadResult> operation = LoadTrackedAsync(descriptor);
            pendingLoads.Add(key, operation);
            return operation;
        }

        public Task<MatchSceneLoadResult> UnloadAsync(MatchSceneKey key)
        {
            if (!TryGet(key, out MatchSceneDescriptor descriptor))
            {
                return Task.FromResult(new MatchSceneLoadResult(
                    key,
                    string.Empty,
                    MatchSceneLoadResult.ResultCode.UnknownKey,
                    $"등록되지 않은 매치 씬 키입니다: {key}"));
            }

            if (pendingUnloads.TryGetValue(key, out Task<MatchSceneLoadResult> pendingUnload))
            {
                return pendingUnload;
            }

            if (pendingLoads.ContainsKey(key))
            {
                return Task.FromResult(CreateBusyResult(key, descriptor.BuiltInSceneName));
            }

            if (!IsLoaded(key))
            {
                List<Scene> foreignScenes = FindLoadedScenes(descriptor.BuiltInSceneName);
                return Task.FromResult(new MatchSceneLoadResult(
                    key,
                    descriptor.BuiltInSceneName,
                    foreignScenes.Count == 0
                        ? MatchSceneLoadResult.ResultCode.AlreadyUnloaded
                        : MatchSceneLoadResult.ResultCode.NotOwned,
                    foreignScenes.Count == 0
                        ? string.Empty
                        : $"이 provider가 소유하지 않은 맵 씬은 언로드하지 않습니다: {DescribeScenes(foreignScenes)}"));
            }

            Task<MatchSceneLoadResult> operation = UnloadTrackedAsync(descriptor);
            pendingUnloads.Add(key, operation);
            return operation;
        }

        private async Task<MatchSceneLoadResult> LoadTrackedAsync(MatchSceneDescriptor descriptor)
        {
            await Task.Yield();

            try
            {
                AsyncOperation operation;
#if UNITY_EDITOR
                if (labMaps != null && descriptor.BuiltInSceneName.StartsWith(CombatLabMapCatalog.MapsPath, StringComparison.Ordinal))
                    operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                        descriptor.BuiltInSceneName, new LoadSceneParameters(LoadSceneMode.Additive));
                else
#endif
                operation = SceneManager.LoadSceneAsync(
                    descriptor.BuiltInSceneName,
                    LoadSceneMode.Additive);
                if (operation == null)
                {
                    return new MatchSceneLoadResult(
                        descriptor.Key,
                        descriptor.BuiltInSceneName,
                        MatchSceneLoadResult.ResultCode.Failed,
                        "Unity가 씬 로드 작업을 만들지 못했습니다.");
                }

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                List<Scene> loadedMatches = FindLoadedScenes(descriptor.BuiltInSceneName);
                if (loadedMatches.Count != 1)
                {
                    return new MatchSceneLoadResult(
                        descriptor.Key,
                        descriptor.BuiltInSceneName,
                        MatchSceneLoadResult.ResultCode.Failed,
                        $"로드 작업 후 맵 씬 인스턴스가 {loadedMatches.Count}개라 소유권을 확정하지 못했습니다: " +
                        DescribeScenes(loadedMatches));
                }

                loadedScenes[descriptor.Key] = loadedMatches[0];
                return new MatchSceneLoadResult(
                    descriptor.Key,
                    descriptor.BuiltInSceneName,
                    MatchSceneLoadResult.ResultCode.Loaded);
            }
            catch (Exception exception)
            {
                return new MatchSceneLoadResult(
                    descriptor.Key,
                    descriptor.BuiltInSceneName,
                    MatchSceneLoadResult.ResultCode.Failed,
                    exception.Message);
            }
            finally
            {
                pendingLoads.Remove(descriptor.Key);
            }
        }

        private async Task<MatchSceneLoadResult> UnloadTrackedAsync(MatchSceneDescriptor descriptor)
        {
            await Task.Yield();

            try
            {
                Scene scene = loadedScenes[descriptor.Key];
                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                if (operation == null)
                {
                    return new MatchSceneLoadResult(
                        descriptor.Key,
                        descriptor.BuiltInSceneName,
                        MatchSceneLoadResult.ResultCode.Failed,
                        "Unity가 씬 언로드 작업을 만들지 못했습니다.");
                }

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                loadedScenes.Remove(descriptor.Key);
                return new MatchSceneLoadResult(
                    descriptor.Key,
                    descriptor.BuiltInSceneName,
                    MatchSceneLoadResult.ResultCode.Unloaded);
            }
            catch (Exception exception)
            {
                return new MatchSceneLoadResult(
                    descriptor.Key,
                    descriptor.BuiltInSceneName,
                    MatchSceneLoadResult.ResultCode.Failed,
                    exception.Message);
            }
            finally
            {
                pendingUnloads.Remove(descriptor.Key);
            }
        }

        private static MatchSceneLoadResult CreateBusyResult(MatchSceneKey key, string sceneName) =>
            new(
                key,
                sceneName,
                MatchSceneLoadResult.ResultCode.Busy,
                "같은 씬의 반대 로딩 작업이 진행 중입니다.");

        private static int CountBuildRegistrations(string sceneName)
        {
            int sceneCount = SceneManager.sceneCountInBuildSettings;
            int matches = 0;
            for (int buildIndex = 0; buildIndex < sceneCount; buildIndex++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                if (string.Equals(path, sceneName, StringComparison.Ordinal) || string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    sceneName,
                    StringComparison.Ordinal))
                {
                    matches++;
                }
            }

            return matches;
        }

        private static List<Scene> FindLoadedScenes(string configuredSceneName)
        {
            var matches = new List<Scene>();
            if (string.IsNullOrEmpty(configuredSceneName)) return matches;

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded) continue;

                bool matchesPath = configuredSceneName.EndsWith(".unity", StringComparison.Ordinal)
                    && string.Equals(scene.path, configuredSceneName, StringComparison.Ordinal);
                bool matchesName = !configuredSceneName.EndsWith(".unity", StringComparison.Ordinal)
                    && string.Equals(scene.name, configuredSceneName, StringComparison.Ordinal);
                if (matchesPath || matchesName) matches.Add(scene);
            }

            return matches;
        }

        private static string DescribeScenes(IReadOnlyList<Scene> scenes)
        {
            if (scenes == null || scenes.Count == 0) return "(없음)";

            var descriptions = new string[scenes.Count];
            for (int index = 0; index < scenes.Count; index++)
            {
                Scene scene = scenes[index];
                GameObject[] roots = scene.GetRootGameObjects();
                var rootNames = new string[roots.Length];
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                    rootNames[rootIndex] = roots[rootIndex].name;
                descriptions[index] =
                    $"name='{scene.name}', path='{scene.path}', roots=[{string.Join(", ", rootNames)}]";
            }

            return string.Join(" | ", descriptions);
        }
    }
}
