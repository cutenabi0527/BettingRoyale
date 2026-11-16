using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>재추첨 시도의 결과. 실패 사유를 구분해 UI 가 다른 메시지를 띄울 수 있게 한다.</summary>
    public enum RerollResult
    {
        Success = 0,

        /// <summary>이미 축복을 선택해 이번 준비 단계가 끝났다.</summary>
        AlreadySelected = 1,

        /// <summary>
        /// 이 라운드의 재추첨 비용이 스펙에 정의돼 있지 않다 (S-GD-SHJGNB).
        ///
        /// <b>지금은 발생할 수 없다.</b> S-GD-HE87JY 이후 <see cref="BlessingDraft.Begin"/> 가
        /// 등급이 정의되지 않은 라운드를 아예 거부하고, <see cref="BlessingConfig"/> 가
        /// 등급 표와 비용 표의 길이를 같게 강제하기 때문이다.
        /// 값을 남겨두는 것은 <c>BlessingOfferChanged.RerollBlockedBy</c> 가 이 enum 을 그대로
        /// 싣기 때문이다. 계약 통합(회의 안건)이 끝나기 전까지 모양을 흔들지 않는다.
        ///
        /// ⚠️ 2026-08-28 정정 — 전에는 "UI 파트와 합의한" 이라고 적었으나 사실이 아니다.
        /// UI 는 이 계약을 쓰지 않고 <c>Scripts/UI/MatchViewEvents.cs</c> 에 자기 것을 따로 갖고 있다.
        /// </summary>
        CostUndefined = 2,

        /// <summary>골드가 모자란다. 잔액은 변하지 않는다.</summary>
        NotEnoughGold = 3,

        /// <summary>잠기지 않은 후보가 없어 바뀔 것이 없다.</summary>
        NothingToReroll = 4,
    }

    /// <summary>후보 하나와 그 잠금 여부.</summary>
    public readonly struct BlessingCandidate
    {
        public readonly BlessingDefinition Blessing;
        public readonly bool IsLocked;

        public BlessingCandidate(BlessingDefinition blessing, bool isLocked)
        {
            Blessing = blessing;
            IsLocked = isLocked;
        }
    }

    /// <summary>
    /// 한 준비 단계의 축복 추첨 상태 (S-GD-S46S28, S-GD-SHJGNB, S-GD-SB84DQ).
    ///
    /// 흐름: <b>후보 제시 → 잠금(최대 2) → 재추첨(유료, 잠그지 않은 전체) → 1개 선택</b>.
    ///
    /// 이미 리롤 캐시백(<c>reroll-cashback</c>)을 갖고 시작했다면 <b>재추첨 비용이 항상 <c>cost_multiplier</c> 배</b>다
    /// (엑셀 개편 — 예전의 "최초 유료 전체 재추첨 환급" 은 없어졌다, S-GD-TMPBEF).
    ///
    /// 추첨은 <see cref="DeterministicRandom"/> 으로만 한다 — Host 가 뽑아 복제하는 값이라
    /// 같은 시드에서 어느 기기든 같은 결과가 나와야 한다 (Jira S15P21A606-31 "2PC 에서 동일하다").
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class BlessingDraft
    {
        /// <summary>리롤 캐시백 축복의 효과 키 (<c>reroll-cashback-t1</c>). 등급 접미사가 없다.</summary>
        private const string CashbackEffect = "reroll-cashback";
        private const string DealerChoiceEffect = "dealer-choice";

        /// <summary>재추첨 비용 배율 수치 이름. 카탈로그가 <see cref="BlessingMvpDefinition.Number"/> 로 준다.</summary>
        private const string CostMultiplierKey = "cost_multiplier";

        private readonly BlessingConfig config;
        private readonly List<BlessingDefinition> compatiblePool;
        private readonly BlessingDefinition[] candidates;
        private readonly bool[] locks;
        private readonly DeterministicRandom random;

        /// <summary>
        /// <b>이 매치에서 이미 후보로 뜬 축복 id</b> (S-GD-H78GRS). 한 번 노출되면 재추첨에도, 남은 준비 단계에도
        /// 다시 나오지 않는다. 추첨이 뽑을 때마다 여기에 더한다 — 그래서 <b>호출자가 매치 동안 살려 두는 집합</b>을 넘긴다.
        /// <c>null</c> 이면 노출 기록을 쓰지 않는다(예전 동작) — 연습 경로와 기존 테스트가 그렇다.
        /// </summary>
        private readonly ICollection<string> exposedIds;

        /// <summary>이 준비 단계를 시작할 때 이미 액티브 축복을 갖고 있었는가 (S-GD-NJQ5NM §2 · S-GD-H78GRS).</summary>
        private readonly bool holdsActive;

        private int freeRerollsRemaining;

        /// <summary>
        /// <b>이 준비 단계가 시작될 때</b> 이미 갖고 있던 재추첨 비용 배율(리롤 캐시백). 없으면 1 이다.
        ///
        /// <b>readonly 인 것이 계약이다</b> — 지금 리롤 캐시백을 고르는 것은 이번 판의 비용을 소급해 바꾸지 않는다
        /// (다음 준비 단계부터 적용된다). 값이 <see cref="Begin"/> 에서 한 번만 정해지므로
        /// "선택 중에 배율이 바뀌는" 경로 자체가 없다.
        /// </summary>
        private readonly float rerollCostMultiplier;

        private BlessingDraft(
            BlessingConfig config,
            List<BlessingDefinition> compatiblePool,
            int round,
            BlessingTier tier,
            DeterministicRandom random,
            float rerollCostMultiplier,
            int freeRerolls,
            ICollection<string> exposedIds = null,
            bool holdsActive = false)
        {
            this.config = config;
            this.compatiblePool = compatiblePool;
            this.random = random;
            this.exposedIds = exposedIds;
            this.holdsActive = holdsActive;
            this.rerollCostMultiplier = rerollCostMultiplier;
            freeRerollsRemaining = Math.Max(0, freeRerolls);

            Round = round;
            Tier = tier;
            candidates = new BlessingDefinition[config.CandidateCount];
            locks = new bool[config.CandidateCount];

            DrawInto(candidates, 0, config.CandidateCount, Array.Empty<BlessingDefinition>());
        }

        /// <summary>1부터 시작하는 라운드 번호. 재추첨 최초 비용이 여기서 갈린다 (S-GD-SHJGNB).</summary>
        public int Round { get; }

        /// <summary>
        /// 필터를 통과한 축복 수 — <b>후보를 뽑아 올 수 있는 전체 범위</b>다.
        /// 화면과 진단이 <see cref="CanRerollChangeCandidates"/> 를 판단하는 데 쓴다.
        /// </summary>
        public int PoolSize => compatiblePool.Count;

        /// <summary>
        /// <b>재추첨이 실제로 카드를 바꿀 수 있는가</b> (축복 시스템 기획 §6.2).
        ///
        /// <para>
        /// <b>풀이 후보 수와 같으면 재추첨은 골드만 먹고 같은 카드를 순서만 섞어 돌려준다.</b>
        /// 기획서가 이것을 "조용히 틀리는 것" 으로 지목했다 — 예외도 로그도 없어서
        /// 사용자는 <b>운이 나빴다고 생각한다.</b>
        /// </para>
        ///
        /// <para>
        /// 예외를 던지지 않는 이유: 풀이 정확히 3인 것은 <b>규칙 위반이 아니다.</b>
        /// 화면이 이 값으로 재추첨 버튼을 흐리게 하는 것이 맞는 대응이다.
        /// </para>
        /// </summary>
        public bool CanRerollChangeCandidates => compatiblePool.Count > config.CandidateCount;

        /// <summary>
        /// 이번 준비 단계에서 제시되는 등급 (S-GD-HE87JY). 라운드로 정해지므로 <b>후보 3개가 전부 이 등급</b>이고,
        /// 재추첨해도 등급은 바뀌지 않는다.
        /// </summary>
        public BlessingTier Tier { get; }

        /// <summary>이번 준비 단계에서 이미 재추첨한 횟수.</summary>
        public int RerollCount { get; private set; }

        /// <summary>딜러 초이스로 남은 무료 전체 재추첨 수. 준비 단계가 끝나면 사라진다.</summary>
        public int FreeRerollsRemaining => freeRerollsRemaining;

        /// <summary>선택이 끝났는가.</summary>
        public bool IsSelected => Selected != null;

        /// <summary>
        /// 이 준비 단계의 재추첨 비용 배율(리롤 캐시백의 <c>cost_multiplier</c>). 갖고 있지 않았으면 1 이다.
        /// 리롤 캐시백은 엑셀 개편으로 환급이 아니라 비용 할인이 됐다(S-GD-TMPBEF) — 옛 환급 값은 걷어냈다.
        /// </summary>
        public float RerollCostMultiplier => rerollCostMultiplier;

        /// <summary>선택된 축복. 선택 전에는 null.</summary>
        public BlessingDefinition Selected { get; private set; }

        /// <summary>현재 잠긴 후보 수.</summary>
        public int LockedCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < locks.Length; i++)
                {
                    if (locks[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 아직 잠글 수 있는 개수 (S-GD-S46S28). 화면의 "잠금 가능 N개" 가 이 값이다.
        ///
        /// <b>상한 계산을 밖으로 내보내지 않으려고 도메인이 준다.</b> 호출하는 쪽이
        /// <c>MaxFreeLocks - LockedCount</c> 를 하면 상한 규칙이 두 곳에 생긴다.
        /// </summary>
        public int RemainingLockCount => config.MaxFreeLocks - LockedCount;

        /// <summary>현재 후보 목록.</summary>
        public IReadOnlyList<BlessingCandidate> Candidates
        {
            get
            {
                var list = new BlessingCandidate[candidates.Length];
                for (var i = 0; i < candidates.Length; i++)
                {
                    list[i] = new BlessingCandidate(candidates[i], locks[i]);
                }

                return list;
            }
        }

        /// <summary>
        /// 준비 단계를 시작하고 첫 후보를 뽑는다.
        ///
        /// 후보는 세 가지 조건을 <b>모두</b> 만족하는 축복에서만 나온다.
        /// <list type="number">
        ///   <item>현재 무기와 호환된다 (S-GD-SB84DQ)</item>
        ///   <item>이번 라운드의 등급이다 (S-GD-HE87JY)</item>
        ///   <item>이번 매치에서 아직 획득하지 않았다 (S-GD-VF67AG)</item>
        /// </list>
        /// </summary>
        /// <param name="pool">전체 축복 정의.</param>
        /// <param name="equippedWeapon">현재 든 무기.</param>
        /// <param name="round">1부터 시작하는 라운드 번호. 등급과 재추첨 비용이 여기서 갈린다.</param>
        /// <param name="acquired">
        /// 이번 매치에서 이미 획득한 축복 (S-GD-RFXF8X). 여기 있는 것은 후보에 나오지 않는다 (S-GD-VF67AG).
        /// <b>선택 인자가 아니다</b> — 빠뜨리면 조용히 중복이 생기므로 1라운드에도 빈 목록을 넘긴다.
        /// </param>
        public static BlessingDraft Begin(
            BlessingConfig config,
            IReadOnlyList<BlessingDefinition> pool,
            WeaponTag equippedWeapon,
            int round,
            BlessingLoadout acquired,
            DeterministicRandom random,
            BlessingPoolRules poolRules = null,
            int matchSize = 0,
            Func<BlessingDefinition, bool> offerable = null,
            ICollection<string> exposedIds = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (acquired == null)
            {
                throw new ArgumentNullException(
                    nameof(acquired), "1라운드에도 빈 BlessingLoadout 을 넘깁니다. null 은 허용하지 않습니다.");
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (round < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(round), round, "라운드는 1부터 시작합니다.");
            }

            if (!config.TryGetTierForRound(round, out var tier))
            {
                // 한 매치는 3라운드다(S-GD-6H2Q5Q). 여기 오면 라운드 진행 쪽 버그이므로
                // 등급을 임의로 외삽하지 않고 드러낸다.
                throw new InvalidOperationException(
                    $"{round}라운드의 축복 등급이 정의돼 있지 않습니다. " +
                    $"등급이 정의된 라운드는 1 ~ {config.DefinedRoundCount} 입니다 (S-GD-6H2Q5Q, S-GD-HE87JY).");
            }

            var compatible = new List<BlessingDefinition>(pool.Count);
            bool holdsActive = acquired.HasActive;
            for (var i = 0; i < pool.Count; i++)
            {
                var definition = pool[i];

                if (definition == null || !definition.IsCompatibleWith(equippedWeapon))
                {
                    continue;
                }

                if (definition.Tier != tier)
                {
                    // S-GD-HE87JY — 이번 라운드의 등급이 아닌 것은 아예 후보가 되지 않는다.
                    continue;
                }

                if (acquired.Contains(definition))
                {
                    // S-GD-VF67AG — 이미 획득한 축복은 다시 나오지 않는다.
                    //
                    // 지금은 S-GD-HE87JY 때문에 이 줄에 걸릴 일이 사실상 없다(라운드마다 등급이 다르므로
                    // 이전 라운드에서 획득한 축복은 위의 등급 검사에서 이미 걸러진다).
                    // 그래도 남겨둔다 — "다음 축복의 등급을 올려주는 축복" 이 들어오면
                    // 라운드와 등급의 1:1 이 깨지고, 그 순간 이 줄만이 중복을 막는다.
                    continue;
                }

                if (holdsActive && definition.IsActive)
                {
                    // S-GD-NJQ5NM §2 — 직접 눌러 쓰는 축복은 한 번에 하나다. 게임패드에 빈 버튼이 Y 하나뿐이라서다.
                    // 빠진 자리는 나머지(패시브)가 채운다 — 후보 3개는 그대로 뜬다.
                    continue;
                }

                if (poolRules != null && !poolRules.Allows(definition.Id, matchSize))
                {
                    // 기획 §6.1 — min_match_size. 인원이 적으면 성립하지 않는 축복이 있다.
                    // matchSize 가 0 이면(인원을 안 넘겼으면) 아무것도 걸러지지 않는다.
                    continue;
                }

                if (offerable != null && !offerable(definition))
                {
                    // 사람마다 다른 자격 — 저점 매수는 순위 하위권만 받을 수 있다
                    // (BlessingEconomyState.IsOfferable, S-GD-TMPBEF). null 이면 아무것도 걸러지지 않는다.
                    continue;
                }

                compatible.Add(definition);
            }

            if (compatible.Count < config.CandidateCount)
            {
                // 풀 크기는 S-GD-S46S28 의 미정 항목이다. 부족한 상태를 조용히 넘기면
                // 후보가 2개만 뜨는 화면이 나오고 원인을 화면에서 알 수 없다.
                throw new InvalidOperationException(
                    $"{round}라운드({tier}) 에서 {equippedWeapon} 과 호환되고 아직 획득하지 않은 축복이 " +
                    $"{compatible.Count}개뿐입니다. 후보 {config.CandidateCount}개를 채울 수 없습니다. " +
                    $"등급마다 최소 {config.CandidateCount}개가 필요합니다 — 풀 크기는 S-GD-S46S28 의 미정 항목입니다.");
            }

            return new BlessingDraft(
                config, compatible, round, tier, random, CaptureRerollCostMultiplier(acquired.Acquired),
                CaptureFreeRerolls(acquired.Acquired), exposedIds, holdsActive);
        }

        private static int CaptureFreeRerolls(IReadOnlyList<BlessingDefinition> source)
        {
            var total = 0;
            for (var i = 0; i < source.Count; i++)
            {
                var definition = source[i];
                if (definition == null || !BlessingMvpCatalog.TryGet(definition.Id, out var entry)
                    || !string.Equals(entry.Effect, DealerChoiceEffect, StringComparison.Ordinal)) continue;
                total += Math.Max(0, (int)entry.Number("free_rerolls"));
            }
            return total;
        }

        /// <summary>
        /// <b>준비 단계가 시작되는 순간</b> 갖고 있는 재추첨 비용 배율(리롤 캐시백)을 읽는다. 없으면 1.
        ///
        /// <para>
        /// 배율은 0 ~ 1 로 잘라둔다. 리롤 캐시백은 <b>할인</b>이다 — 데이터가 1 을 넘게 들어와도 비용이 오르지 않고,
        /// 음수·NaN 이 와도 돈이 생기지 않는다. 여럿이면 가장 싼 쪽을 쓴다.
        /// </para>
        /// </summary>
        private static float CaptureRerollCostMultiplier(IReadOnlyList<BlessingDefinition> source)
        {
            var best = 1f;
            for (var i = 0; i < source.Count; i++)
            {
                var definition = source[i];

                if (definition == null || !BlessingMvpCatalog.TryGet(definition.Id, out var entry))
                {
                    continue;
                }

                if (!string.Equals(entry.Effect, CashbackEffect, StringComparison.Ordinal))
                {
                    continue;
                }

                var multiplier = entry.Number(CostMultiplierKey);
                if (float.IsNaN(multiplier) || multiplier < 0f)
                {
                    multiplier = 0f;
                }

                if (multiplier < best)
                {
                    best = multiplier;
                }
            }

            return best;
        }

        /// <summary>
        /// 후보의 잠금을 켜고 끈다. 잠금은 무료지만 최대 <see cref="BlessingConfig.MaxFreeLocks"/> 개다 (S-GD-S46S28).
        /// </summary>
        /// <returns>상태가 바뀌었으면 true. 상한에 걸려 켜지 못했으면 false.</returns>
        public bool TryToggleLock(int index)
        {
            if (IsSelected)
            {
                return false;
            }

            ValidateIndex(index);

            if (locks[index])
            {
                locks[index] = false;
                return true;
            }

            if (LockedCount >= config.MaxFreeLocks)
            {
                return false;
            }

            locks[index] = true;
            return true;
        }

        /// <summary>
        /// <b>지금 재추첨하면 차감되는 비용</b> (S-GD-SHJGNB). 이 라운드의 비용이 정의돼 있지 않으면 false.
        ///
        /// ⚠️ 이름을 <c>TryGetNextRerollCost</c> 에서 바꿨다(2026-08-28). "다음" 이라는 말이
        /// "한 번 더 눌렀을 때" 로 읽혀서 UI 쪽 같은 이름 필드와 뜻이 반대로 붙었다.
        /// 미리보기 값이 필요하면 <see cref="TryGetRerollCostPreview"/> 를 쓴다.
        /// </summary>
        public bool TryGetCurrentRerollCost(out int cost) => TryGetDiscountedRerollCost(RerollCount, out cost);

        /// <summary>
        /// <b>한 번 더 재추첨하면 들 비용</b> — 화면이 "재추첨 100G (다음 200G)" 를 그리는 데 쓴다.
        ///
        /// <b>호출한 쪽이 ×2 를 하지 않게 하려고 도메인이 계산한다.</b> 배수는 설정값이고(S-GD-SHJGNB),
        /// 밖에서 곱하면 배수가 바뀔 때 한쪽만 고쳐진다.
        /// </summary>
        public bool TryGetRerollCostPreview(out int cost) => TryGetDiscountedRerollCost(RerollCount + 1, out cost);

        /// <summary>
        /// 설정의 재추첨 비용에 리롤 캐시백 배율을 곱한다(S-GD-TMPBEF). 내림이다 — 할인이 비용을 올리는 일은 없다
        /// (S-GD-KH2AJ6).
        /// </summary>
        private bool TryGetDiscountedRerollCost(int rerollIndex, out int cost)
        {
            if (!config.TryGetRerollCost(Round, rerollIndex, out cost))
            {
                return false;
            }

            if (rerollCostMultiplier < 1f)
            {
                cost = (int)Math.Floor(cost * (double)rerollCostMultiplier);
            }

            return true;
        }

        /// <summary>
        /// 지금 재추첨할 수 있는가. <b>아무것도 바꾸지 않는다.</b>
        /// <see cref="RerollResult.Success"/> 면 가능하고, 아니면 그 값이 막고 있는 이유다.
        ///
        /// UI 가 재추첨 버튼을 <b>미리 비활성화</b>하는 데 쓴다 — 눌렀다가 거부당하는 대신
        /// 애초에 못 누르게 한다(UI 파트 결정, 2026-08-27). <see cref="TryReroll"/> 도 같은 판정을 쓰므로
        /// 화면과 실제가 어긋나지 않는다.
        /// </summary>
        public RerollResult GetRerollAvailability(Wallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (IsSelected)
            {
                return RerollResult.AlreadySelected;
            }

            if (LockedCount >= candidates.Length)
            {
                return RerollResult.NothingToReroll;
            }

            if (!TryGetCurrentRerollCost(out var cost))
            {
                return RerollResult.CostUndefined;
            }

            return freeRerollsRemaining > 0 || wallet.Gold >= cost
                ? RerollResult.Success : RerollResult.NotEnoughGold;
        }

        /// <summary>
        /// 잠그지 않은 후보를 전부 다시 뽑는다 (S-GD-S46S28). 비용을 지갑에서 차감한다 (S-GD-SHJGNB).
        /// 실패하면 <b>후보도 잔액도 바뀌지 않는다.</b>
        /// </summary>
        public RerollResult TryReroll(Wallet wallet)
        {
            var availability = GetRerollAvailability(wallet);
            if (availability != RerollResult.Success)
            {
                return availability;
            }

            TryGetCurrentRerollCost(out var cost);
            bool free = freeRerollsRemaining > 0;
            if (!free && !wallet.TrySpend(cost))
            {
                // GetRerollAvailability 가 잔액을 이미 확인했으므로 여기 오면 그 사이에 잔액이 바뀐 것이다.
                return RerollResult.NotEnoughGold;
            }

            var lockedCount = LockedCount;

            // 잠긴 것은 다시 뽑히지 않도록 후보에서 제외한다.
            var keep = new BlessingDefinition[lockedCount];
            var k = 0;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (locks[i])
                {
                    keep[k++] = candidates[i];
                }
            }

            for (var i = 0; i < candidates.Length; i++)
            {
                if (!locks[i])
                {
                    candidates[i] = null;
                }
            }

            DrawInto(candidates, 0, candidates.Length, keep);
            RerollCount++;
            if (free) freeRerollsRemaining--;
            return RerollResult.Success;
        }

        /// <summary>
        /// 시간이 끝났는데 고르지 않은 사람을 대신해 <b>무작위로</b> 선택한다 (S-GD-NK8ZJ7).
        ///
        /// <b>잠근 후보가 있으면 그 중에서만</b> 뽑는다. 잠금은 "이건 갖고 싶다" 는 의사 표시이므로
        /// 시간이 다 돼도 존중한다. 잠근 것이 없으면 전체 후보에서 뽑는다.
        ///
        /// <b>완전 무작위가 아니다</b> — 첫 후보를 고정으로 주면 1번 자리 축복만 계속 쌓이고,
        /// 완전 무작위면 잠금이 무의미해진다.
        ///
        /// 시계는 도메인에 없다. <b>"30초가 지났다" 를 판단하는 것은 어댑터·UI 몫</b>이고
        /// 여기는 그 결과로 불린다.
        /// </summary>
        public BlessingDefinition SelectAutomatically()
        {
            var pickable = new List<int>();

            for (int i = 0; i < candidates.Length; i++)
            {
                if (locks[i])
                {
                    pickable.Add(i);
                }
            }

            if (pickable.Count == 0)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    pickable.Add(i);
                }
            }

            // Host 가 뽑아 복제하는 값이라 재현 가능해야 한다 — DeterministicRandom 으로만 뽑는다.
            return Select(pickable[random.Next(pickable.Count)]);
        }

        /// <summary>후보 하나를 선택해 준비 단계를 끝낸다 (S-GD-S46S28). 최종 선택은 잠금 수와 무관하게 1개다.</summary>
        public BlessingDefinition Select(int index)
        {
            if (IsSelected)
            {
                throw new InvalidOperationException("이미 축복을 선택했습니다.");
            }

            ValidateIndex(index);
            Selected = candidates[index];
            return Selected;
        }

        /// <summary>
        /// 비어 있는(null) 자리를 풀에서 뽑아 채운다.
        /// <paramref name="exclude"/> 와 이미 채워진 후보는 다시 뽑지 않는다.
        /// </summary>
        /// <remarks>
        /// 한 준비 단계 안에서 같은 축복이 두 자리에 뜨지 않는다 — 잠근 축복이 옆자리에 또 뜨면
        /// 재추첨이 낭비된다. 라운드를 넘는 중복은 <see cref="Begin"/> 에서 막는다 (S-GD-VF67AG).
        /// </remarks>
        private void DrawInto(
            BlessingDefinition[] target,
            int start,
            int count,
            IReadOnlyList<BlessingDefinition> exclude)
        {
            // 액티브는 한 번에 하나다 (S-GD-NJQ5NM §2). 이미 갖고 있거나 이 판에 이미 한 장 뽑았으면 더 뽑지 않는다 —
            // 예전에는 Begin 의 보유 검사만 있어서, 같은 등급의 액티브가 늘면 한 판에 둘이 함께 뜰 수 있었다.
            bool activeTaken = holdsActive || HasActiveIn(target);

            // 한 번 노출된 축복은 다시 뽑지 않는다 (S-GD-H78GRS). fresh 가 모자랄 때만 stale 로 내려간다 —
            // 그 경우에도 예외를 던지지 않는다(예전에는 "재추첨할 축복이 부족합니다" 로 준비 화면이 멈췄다).
            var fresh = new List<BlessingDefinition>(compatiblePool.Count);
            var stale = new List<BlessingDefinition>(compatiblePool.Count);
            for (var i = 0; i < compatiblePool.Count; i++)
            {
                var candidate = compatiblePool[i];

                if (Contains(exclude, candidate) || Contains(target, candidate))
                {
                    continue;
                }

                if (activeTaken && candidate != null && candidate.IsActive)
                {
                    continue;
                }

                if (IsExposed(candidate)) stale.Add(candidate);
                else fresh.Add(candidate);
            }

            random.Shuffle(fresh);
            random.Shuffle(stale);

            var freshNext = 0;
            var staleNext = 0;
            for (var i = start; i < start + count; i++)
            {
                if (target[i] != null)
                {
                    continue;
                }

                BlessingDefinition picked = null;
                while (freshNext < fresh.Count)
                {
                    var candidate = fresh[freshNext++];
                    if (activeTaken && candidate.IsActive) continue;
                    picked = candidate;
                    break;
                }

                if (picked == null)
                {
                    // 남은 등급 풀을 전부 노출한 뒤의 재추첨이다. 화면을 멈추지 않고 이미 본 것 중에서 채운다.
                    while (staleNext < stale.Count)
                    {
                        var candidate = stale[staleNext++];
                        if (activeTaken && candidate.IsActive) continue;
                        picked = candidate;
                        break;
                    }
                }

                if (picked == null)
                {
                    throw new InvalidOperationException(
                        "재추첨할 축복이 부족합니다. 풀 크기는 S-GD-S46S28 의 미정 항목입니다.");
                }

                target[i] = picked;
                activeTaken |= picked.IsActive;
                exposedIds?.Add(picked.Id);
            }
        }

        private bool IsExposed(BlessingDefinition definition)
        {
            if (exposedIds == null || definition == null) return false;
            foreach (string id in exposedIds)
                if (string.Equals(id, definition.Id, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool HasActiveIn(IReadOnlyList<BlessingDefinition> definitions)
        {
            if (definitions == null) return false;
            for (var i = 0; i < definitions.Count; i++)
                if (definitions[i] != null && definitions[i].IsActive) return true;
            return false;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null || id == null)
            {
                return false;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                // 네트워크로 오가는 값이라 문화권에 따라 판정이 갈리면 안 된다 — Ordinal 로 고정한다.
                if (string.Equals(ids[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(IReadOnlyList<BlessingDefinition> list, BlessingDefinition item)
        {
            if (list == null || item == null)
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (item.Equals(list[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= candidates.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"후보 번호는 0 ~ {candidates.Length - 1} 입니다.");
            }
        }
    }
}
