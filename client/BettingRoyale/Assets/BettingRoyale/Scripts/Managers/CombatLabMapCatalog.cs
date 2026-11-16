using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BettingRoyale.Data.Scenes;
using UnityEngine.SceneManagement;

namespace BettingRoyale.Managers
{
    /// <summary>Dev-only discovery; never registers maps in the product catalog or modifies Build Profiles.</summary>
    public static class CombatLabMapCatalog
    {
        public const string MapsPath = "Assets/BettingRoyale/Scenes/Maps/";
        private static readonly Regex CombatName = new(@"^Map_(.+)_Combat_([0-9]+)$", RegexOptions.CultureInvariant);

        public static IReadOnlyList<MatchSceneDescriptor> Discover()
        {
            var paths = new List<string>();
#if UNITY_EDITOR
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Scene", new[] { MapsPath.TrimEnd('/') }))
                paths.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
#else
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                paths.Add(SceneUtility.GetScenePathByBuildIndex(i));
#endif
            return FromPaths(paths);
        }

        public static IReadOnlyList<MatchSceneDescriptor> FromPaths(IEnumerable<string> paths)
        {
            var maps = new List<MatchSceneDescriptor>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in paths)
            {
                string path = raw?.Replace('\\', '/');
                if (path == null || !path.StartsWith(MapsPath, StringComparison.Ordinal)
                    || !path.EndsWith(".unity", StringComparison.Ordinal) || !unique.Add(path)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                Match match = CombatName.Match(name);
                if (!match.Success) continue;
                MatchSceneDescriptor? known = null;
                foreach (MatchSceneDescriptor descriptor in MatchSceneCatalog.Combat)
                    if (name == descriptor.BuiltInSceneName && path == MapsPath + "Default/" + name + ".unity") known = descriptor;
                if (known.HasValue) { maps.Add(known.Value); continue; }
                // Full asset path is collision-free even when two folders contain the same filename.
                string relative = path.Substring(MapsPath.Length);
                string keySuffix = BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(relative)).Replace("-", string.Empty).ToLowerInvariant();
                maps.Add(new MatchSceneDescriptor(new MatchSceneKey("lab/combat/" + keySuffix), path,
                    relative.Substring(0, relative.Length - ".unity".Length), MatchScenePhase.Combat));
            }
            // 제품 기본 전투 맵이 항상 첫 항목이다. Dev/회귀 fixture(Combat_01)는 그 다음에만 오며
            // Dev Lab에서 명시적으로 고를 때에만 사용한다 — 기본 선택으로 승격하지 않는다.
            maps.Sort((a, b) => a.Key == MatchSceneCatalog.DefaultCombat ? -1
                : b.Key == MatchSceneCatalog.DefaultCombat ? 1
                : MatchSceneCatalog.IsDevRegressionFixture(a.Key)
                    != MatchSceneCatalog.IsDevRegressionFixture(b.Key)
                    ? (MatchSceneCatalog.IsDevRegressionFixture(a.Key) ? -1 : 1)
                : string.CompareOrdinal(a.BuiltInSceneName, b.BuiltInSceneName));
            return maps.AsReadOnly();
        }

        public static bool TryGet(IReadOnlyList<MatchSceneDescriptor> maps, MatchSceneKey key, out MatchSceneDescriptor descriptor)
        {
            foreach (MatchSceneDescriptor candidate in maps)
                if (candidate.Key == key) { descriptor = candidate; return true; }
            descriptor = default; return false;
        }
    }
}
