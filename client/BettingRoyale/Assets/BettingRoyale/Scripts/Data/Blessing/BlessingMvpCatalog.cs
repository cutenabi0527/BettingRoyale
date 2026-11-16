using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BettingRoyale.Data.Remote;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>CSV에서 생성한 등급별 축복. 기존 정의와 함께 로컬·온라인 추첨에 사용한다.</summary>
    public static class BlessingMvpCatalog
    {
        /// <summary>각 라운드(등급)에서 후보 3장을 뽑을 수 있도록 보장하는 최소 고유 정의 수.</summary>
        public const int MinimumPerTier = 20;
        public const string SourceSha256 = BlessingMvpData.SourceSha256;
        public const int GeneratedCount = BlessingMvpData.Count;
        private static readonly ReadOnlyCollection<BlessingMvpDefinition> Entries;
        private static readonly Dictionary<string, BlessingMvpDefinition> ById;
        private static readonly int[] TierCounts;
        private static readonly int MissingArtCount;

        static BlessingMvpCatalog()
        {
            var created = BlessingMvpData.Create();
            if (created.Length != BlessingMvpData.Count)
            {
                throw new InvalidOperationException(
                    $"{BlessingMvpData.SourcePath} 생성 항목 수가 헤더와 다릅니다 ({created.Length}/{BlessingMvpData.Count}).");
            }

            var index = new Dictionary<string, BlessingMvpDefinition>(created.Length, StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var icons = new HashSet<int>();
            var tierCounts = new int[3];
            var missingArt = 0;

            foreach (var entry in created)
            {
                if (entry == null)
                {
                    throw new InvalidOperationException(
                        $"{BlessingMvpData.SourcePath} 에서 생성된 표에 빈 항목이 있습니다.");
                }

                if (index.ContainsKey(entry.Definition.Id))
                {
                    // 생성기가 이미 막는 상황이다. 그래도 여기서 한 번 더 보는 이유는,
                    // 중복이 통과하면 TryGet 이 어느 쪽을 돌려줄지가 배열 순서에 달리게 되기 때문이다.
                    throw new InvalidOperationException(
                        $"축복 id '{entry.Definition.Id}' 가 중복입니다 ({BlessingMvpData.SourcePath}).");
                }

                if (!names.Add(entry.Definition.DisplayName))
                {
                    throw new InvalidOperationException(
                        $"축복 이름 '{entry.Definition.DisplayName}' 가 중복입니다 ({BlessingMvpData.SourcePath}).");
                }

                int tierIndex = (int)entry.Definition.Tier - (int)BlessingTier.Silver;
                if (tierIndex < 0 || tierIndex >= tierCounts.Length)
                {
                    throw new InvalidOperationException(
                        $"축복 '{entry.Definition.Id}' 의 등급 값이 범위를 벗어났습니다.");
                }

                tierCounts[tierIndex]++;
                if (entry.IconIndex == BlessingMvpDefinition.MissingIconIndex)
                {
                    missingArt++;
                }
                else if (!icons.Add(entry.IconIndex))
                {
                    throw new InvalidOperationException(
                        $"축복 아이콘 인덱스 {entry.IconIndex} 가 중복입니다 ({BlessingMvpData.SourcePath}).");
                }

                index.Add(entry.Definition.Id, entry);
            }

            for (int i = 0; i < tierCounts.Length; i++)
            {
                if (tierCounts[i] < MinimumPerTier)
                {
                    throw new InvalidOperationException(
                        $"축복 등급 {(BlessingTier)(i + (int)BlessingTier.Silver)} 가 {tierCounts[i]}종입니다. " +
                        $"최소 {MinimumPerTier}종이 필요합니다 ({BlessingMvpData.SourcePath}).");
                }
            }

            Entries = new ReadOnlyCollection<BlessingMvpDefinition>(created);
            ById = index;
            TierCounts = tierCounts;
            MissingArtCount = missingArt;
        }

        /// <summary>
        /// 축복 전부. <b>id 오름차순</b>이다 — 생성기가 정렬해서 내보내므로
        /// CSV 의 행 순서를 바꿔도 이 순서는 변하지 않는다.
        /// </summary>
        public static IReadOnlyList<BlessingMvpDefinition> All => Entries;

        /// <summary>Silver·Gold·Platinum 순서의 생성 CSV 등급별 수.</summary>
        public static IReadOnlyList<int> CountsByTier => TierCounts;

        /// <summary>최종 아이콘 아트가 없어 공용 '?' 폴백을 쓰는 정의 수.</summary>
        public static int MissingArtDefinitions => MissingArtCount;

        /// <summary>
        /// id 로 찾는다. <c>null</c> 이나 모르는 id 면 <c>false</c> 를 돌려주고 <paramref name="entry"/> 는 <c>null</c> 이다.
        ///
        /// <para>
        /// <b>반환값을 검사한다.</b> 네트워크로 오는 것은 id 뿐이므로(<see cref="BlessingDefinition.Id"/>),
        /// 구버전 클라이언트가 보낸 사라진 id 가 여기로 들어올 수 있다.
        /// </para>
        ///
        /// <para>
        /// <b>수치는 엑셀 것이다</b>(S-GD-RTB6H2). 게임 데이터가 올라와 있으면 이 축복의 수치를 엑셀 값으로 바꿔 준다
        /// (<see cref="BlessingMvpTuning"/>). 동작 코드가 전부 여기서 축복을 꺼내므로 한 곳만 바꾸면 된다.
        /// <see cref="All"/> 은 코드 원본 그대로다 — 아이콘 표·대역 목록처럼 수치를 읽지 않는 쪽이 쓴다.
        /// </para>
        /// </summary>
        public static bool TryGet(string blessingId, out BlessingMvpDefinition entry)
        {
            if (blessingId == null)
            {
                entry = null;
                return false;
            }

            if (!ById.TryGetValue(blessingId, out entry))
            {
                // 코드 목록에 없으면 엑셀 전용 축복의 동작 정의를 본다 — 엑셀이 올라와 있을 때만 있다.
                return GameContentProvider.IsLoaded
                    && GameContentProvider.Current.Behaviors.TryGetValue(blessingId, out entry);
            }

            entry = BlessingMvpTuning.Apply(entry);
            return true;
        }

        /// <summary>코드(MVP)로 동작을 구현한 축복인가. 엑셀 전용 축복은 거짓이다.</summary>
        public static bool ContainsCode(string blessingId) => blessingId != null && ById.ContainsKey(blessingId);
    }
}
