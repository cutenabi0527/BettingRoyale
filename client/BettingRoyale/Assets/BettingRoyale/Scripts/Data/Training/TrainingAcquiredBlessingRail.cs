using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Contracts;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// 훈련장에서 고른 축복을 <b>좌측 보유 축복 레일</b>이 읽는 계약으로 바꾼다 (Jira 182).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>무엇이 깨져 있었나.</b> 레일(<c>AcquiredBlessingsHudView</c>)은 <see cref="BlessingOfferChanged"/>
    /// 만 듣는데, 그 이벤트를 발행하는 곳이 온라인 매치·Local Play·가짜 드라이버 셋뿐이었다.
    /// <b>훈련 경로에는 발행자가 하나도 없었다.</b> 그래서 축복을 골라도 레일이 빈 액자로 남았고,
    /// 이건 Client 뿐 아니라 <b>Host 에서도 똑같았다</b> (2026-09-16 5인 실측).
    /// </para>
    /// <para>
    /// <b>왜 여기(Data)에 있나.</b> 원래는 <c>TrainingSessionCoordinator</c> 안에 있었는데, 그러면
    /// 이 판단이 <c>MonoBehaviour</c> 에 갇혀 <b>Unity 를 켜야만 검증할 수 있다.</b> 훈련장 회귀
    /// 검증은 머지 전에 돌아야 하므로(EditMode 게이트는 노트북 러너가 꺼져 있으면 건너뛴다 —
    /// D-REL-K73ME8) 순수 계층으로 내렸다. <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </para>
    /// <para>
    /// <b>훈련 전용 이벤트를 새로 만들지 않는다.</b> 그러면 HUD 가 훈련 경로를 알게 되고, 같은 정보를
    /// 쓰는 다른 화면들은 여전히 못 받는다. 대신 <b>제품 계약을 그대로</b> 만든다 — 계약 정의는 이미
    /// <c>Data/Contracts</c> 한 곳에 있다 (D-065).
    /// </para>
    /// </remarks>
    public sealed class TrainingAcquiredBlessingRail
    {
        /// <summary>
        /// 마지막으로 알린 보유 축복. <b>스냅샷은 주기적으로 오므로</b> 같은 내용을 다시 알리지
        /// 않게 여기서 비교한다 — 안 그러면 초당 몇 번씩 같은 이벤트가 나가 레일이 계속 다시 그려진다.
        /// </summary>
        private string[] lastPublished = Array.Empty<string>();

        /// <summary>
        /// 발행할 것이 있으면 <c>true</c> 와 함께 계약을 준다.
        /// </summary>
        /// <param name="playerId">훈련 상태의 주인. 훈련 상태는 본인 것만 도착한다.</param>
        /// <param name="acquiredIds">회선으로 온 보유 축복 id. <c>null</c> 이면 빈 목록으로 본다.</param>
        /// <param name="catalog">id 를 이름·설명·등급으로 되돌릴 카탈로그.</param>
        /// <param name="offer">발행할 계약. <c>false</c> 일 때는 의미 없다.</param>
        /// <param name="unresolvedCount">
        /// 카탈로그에서 찾지 못한 id 수. <b>0 이 아니면 부르는 쪽이 알려야 한다</b> —
        /// 말없이 적게 그리면 "축복이 하나 사라졌다" 를 아무도 설명하지 못한다.
        /// </param>
        public bool TryBuild(
            int playerId,
            string[] acquiredIds,
            IReadOnlyList<BlessingDefinition> catalog,
            out BlessingOfferChanged offer,
            out int unresolvedCount)
        {
            offer = default;
            unresolvedCount = 0;

            string[] ids = acquiredIds ?? Array.Empty<string>();

            if (IsSameAsLastPublished(ids))
            {
                return false;
            }

            lastPublished = (string[])ids.Clone();

            var acquired = new List<BlessingCandidateView>(ids.Length);
            foreach (string id in ids)
            {
                if (TryResolve(id, catalog, out BlessingCandidateView view)) { acquired.Add(view); }
            }

            unresolvedCount = ids.Length - acquired.Count;

            offer = new BlessingOfferChanged(
                playerId: playerId,
                isMine: true,
                round: 0,
                candidates: Array.Empty<BlessingCandidateView>(),
                rerollCount: 0,
                remainingLockCount: 0,
                currentRerollCost: 0,
                nextRerollCostPreview: 0,
                hasRerollCost: false,
                // ⚠️ 훈련장에는 추첨 자체가 없다. AlreadySelected 를 넣는 것은
                //    CanReroll 을 false 로 만들기 위해서다 — 계약이 그 값에서 유도한다.
                rerollBlockedBy: RerollResult.AlreadySelected,
                acquired: acquired.ToArray());

            return true;
        }

        /// <summary>세션이 끝났다. 다음 세션의 첫 스냅샷이 같은 내용이어도 다시 알리게 한다.</summary>
        public void Reset() => lastPublished = Array.Empty<string>();

        private bool IsSameAsLastPublished(string[] ids)
        {
            if (ids.Length != lastPublished.Length)
            {
                return false;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] != lastPublished[i]) { return false; }
            }

            return true;
        }

        private static bool TryResolve(
            string id, IReadOnlyList<BlessingDefinition> catalog, out BlessingCandidateView view)
        {
            view = default;

            if (catalog == null)
            {
                return false;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                BlessingDefinition definition = catalog[i];
                if (definition == null || definition.Id != id) { continue; }

                view = new BlessingCandidateView(
                    // 획득분은 후보가 아니다 — 계약이 정한 자리값 (S-GD-RFXF8X).
                    candidateIndex: -1,
                    blessingId: definition.Id,
                    displayName: definition.DisplayName,
                    description: definition.Description,
                    tag: definition.Tag,
                    tier: definition.Tier,
                    isLocked: false);
                return true;
            }

            return false;
        }
    }
}
