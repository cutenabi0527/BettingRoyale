using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// 한 전투 라운드의 <b>상자 필드</b> 설정 (S-GD-MQP6AS, S-GD-6DNSGY).
    ///
    /// <para>
    /// ⚠️ <b><see cref="ChestConfig"/> 와 같은 이유로 <c>Default</c> 를 두지 않는다.</b>
    /// 상자 개수만 확정이고(2개, S-GD-MQP6AS), 보상량·재생성 지연은 「MVP 2차 기획」의
    /// <b>권장안</b>일 뿐이다 — S-GD-6DNSGY 본문이 *"이 권장안은 확정으로 가는 중이 아니라
    /// 바뀔 가능성이 높다"* 고 스스로 적었다. 기본값을 두면 그 숫자가 아무도 승인하지 않은 채
    /// 사실상의 스펙으로 굳는다. Jira S15P21A606-32 도 *"미확정 개봉 시간·보상량·재시도 값은
    /// 설정으로 유지하고 confirmed 처럼 고정하지 않음"* 을 범위에 못박았다.
    /// </para>
    ///
    /// <para>
    /// <b>앵커 후보는 밖에서 받는다.</b> 좌표의 정본은 전장 프리팹(<c>ChestAnchor_01~08</c>)이고
    /// 도메인은 그 목록을 알 이유가 없다. 어댑터가 이름으로 찾아 순서대로 넘긴다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class ChestFieldConfig
    {
        private readonly WorldPosition[] anchors;

        /// <param name="chest">상자 하나의 개봉·재생성 시간. ⚠️ 값은 스펙 미정이다.</param>
        /// <param name="anchorCandidates">
        /// 상자가 놓일 수 있는 위치 후보. <b>순서가 곧 앵커 인덱스</b>다 — 어댑터가
        /// <c>ChestAnchor_01</c> 부터 이름 순으로 넘긴다. 재현성이 여기에 달려 있다.
        /// </param>
        /// <param name="chestCount">한 라운드에 놓을 상자 수. <b>2개가 확정</b>이다 (S-GD-MQP6AS).</param>
        /// <param name="reward">
        /// 개봉 성공 시 주는 것. ⚠️ 골드 권장안은 라운드 기본 지급의 50% 다.
        /// <see cref="ChestReward"/> 로 감싼 이유는 나중에 소모품·장신구를 더할 때
        /// 이 생성자를 안 고치기 위해서다.
        /// </param>
        public ChestFieldConfig(
            ChestConfig chest,
            IReadOnlyList<WorldPosition> anchorCandidates,
            int chestCount,
            ChestReward reward)
        {
            Chest = chest ?? throw new ArgumentNullException(nameof(chest));

            if (anchorCandidates == null)
            {
                throw new ArgumentNullException(nameof(anchorCandidates));
            }

            if (chestCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chestCount), chestCount, "상자는 1개 이상이어야 합니다.");
            }

            if (anchorCandidates.Count < chestCount)
            {
                // 후보가 모자라면 같은 자리에 두 개가 겹친다. 조용히 겹치게 두면
                // "재생성 전후 중복 상자가 없음"(완료 조건 4) 을 판정할 수 없다.
                throw new ArgumentException(
                    $"앵커 후보가 {anchorCandidates.Count}개뿐인데 상자를 {chestCount}개 놓을 수 없습니다. " +
                    "같은 자리에 겹치면 중복 판정이 불가능합니다.",
                    nameof(anchorCandidates));
            }

            anchors = new WorldPosition[anchorCandidates.Count];
            for (int i = 0; i < anchorCandidates.Count; i++)
            {
                anchors[i] = anchorCandidates[i];
            }

            ChestCount = chestCount;
            Reward = reward;
        }

        /// <summary>상자 하나의 시간 설정.</summary>
        public ChestConfig Chest { get; }

        /// <summary>앵커 후보. 순서가 인덱스다.</summary>
        public IReadOnlyList<WorldPosition> AnchorCandidates => anchors;

        /// <summary>한 라운드에 놓는 상자 수 (S-GD-MQP6AS — 2개 확정).</summary>
        public int ChestCount { get; }

        /// <summary>⚠️ 스펙 미정값. 개봉 성공 시 주는 것. 지금은 골드만 담는다.</summary>
        public ChestReward Reward { get; }

        public override string ToString() =>
            $"상자 {ChestCount}개 / 앵커 후보 {anchors.Length}곳 / 보상 {Reward} / {Chest}";
    }
}
