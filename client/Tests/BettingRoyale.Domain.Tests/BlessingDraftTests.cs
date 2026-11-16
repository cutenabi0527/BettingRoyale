using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.LocalPlay;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 축복 추첨 흐름 — 후보 → 잠금 → 재추첨 → 선택 (S-GD-S46S28, S-GD-SHJGNB, S-GD-SB84DQ, S-GD-HE87JY, S-GD-VF67AG).
    /// </summary>
    [TestFixture]
    public class BlessingDraftTests
    {
        private const int Seed = 20260827;

        /// <summary>
        /// 등급마다 넉넉히 채운 풀.
        ///
        /// 등급당 공용 4 + 검 2 + 활 2 이므로 무기 하나가 쓸 수 있는 것은 <b>등급당 6개</b>다.
        /// 후보가 3개이니 재추첨해도 뽑을 것이 남는다 — 등급이 라운드로 고정되면서(S-GD-HE87JY)
        /// 한 라운드가 쓸 수 있는 풀이 1/3 로 줄었기 때문에 여유를 둔다.
        /// </summary>
        private static IReadOnlyList<BlessingDefinition> MakePool()
        {
            var pool = new List<BlessingDefinition>();

            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (var i = 0; i < 4; i++)
                {
                    pool.Add(new BlessingDefinition(
                        $"common_{tier}_{i}", $"공용{tier}{i}", WeaponTag.Common, tier));
                }

                for (var i = 0; i < 2; i++)
                {
                    pool.Add(new BlessingDefinition(
                        $"sword_{tier}_{i}", $"검{tier}{i}", WeaponTag.Sword, tier));
                    pool.Add(new BlessingDefinition(
                        $"bow_{tier}_{i}", $"활{tier}{i}", WeaponTag.Bow, tier));
                }
            }

            return pool;
        }

        private static BlessingDraft BeginDraft(
            int round = 1,
            WeaponTag weapon = WeaponTag.Sword,
            BlessingLoadout acquired = null) =>
            BlessingDraft.Begin(
                BlessingConfig.Default,
                MakePool(),
                weapon,
                round,
                acquired ?? new BlessingLoadout(),
                new DeterministicRandom(Seed));

        // ------------------------------------------------------------ 후보 (S-GD-S46S28, S-GD-SB84DQ)

        [Test]
        public void 후보는_3개다()
        {
            Assert.That(BeginDraft().Candidates.Count, Is.EqualTo(3));
        }

        [Test]
        public void 후보는_전부_현재_무기와_호환된다()
        {
            var draft = BeginDraft(weapon: WeaponTag.Bow);

            foreach (var candidate in draft.Candidates)
            {
                Assert.That(candidate.Blessing.Tag,
                    Is.EqualTo(WeaponTag.Common).Or.EqualTo(WeaponTag.Bow),
                    $"{candidate.Blessing} 은 활과 호환되지 않는다 (S-GD-SB84DQ)");
            }
        }

        [Test]
        public void 한_준비_단계의_후보는_서로_중복되지_않는다()
        {
            var draft = BeginDraft();
            var ids = new HashSet<string>();

            foreach (var candidate in draft.Candidates)
            {
                Assert.That(ids.Add(candidate.Blessing.Id), Is.True, "후보가 중복됐다");
            }
        }

        [Test]
        public void 같은_시드는_같은_후보를_뽑는다()
        {
            var first = BeginDraft().Candidates;
            var second = BeginDraft().Candidates;

            for (var i = 0; i < first.Count; i++)
            {
                Assert.That(second[i].Blessing.Id, Is.EqualTo(first[i].Blessing.Id),
                    "2PC 에서 같은 결과가 나오려면 추첨이 결정적이어야 한다");
            }
        }

        // ------------------------------------------------- 등급은 라운드로 정해진다 (S-GD-HE87JY)

        [Test]
        public void 등급은_라운드_순서대로_실버_골드_플래티넘이다()
        {
            Assert.That(BeginDraft(1).Tier, Is.EqualTo(BlessingTier.Silver));
            Assert.That(BeginDraft(2).Tier, Is.EqualTo(BlessingTier.Gold));
            Assert.That(BeginDraft(3).Tier, Is.EqualTo(BlessingTier.Platinum));
        }

        [Test]
        public void 한_라운드의_후보는_전부_같은_등급이다()
        {
            for (var round = 1; round <= 3; round++)
            {
                var draft = BeginDraft(round);

                foreach (var candidate in draft.Candidates)
                {
                    Assert.That(candidate.Blessing.Tier, Is.EqualTo(draft.Tier),
                        $"{round}라운드 후보에 다른 등급이 섞였다 (S-GD-HE87JY)");
                }
            }
        }

        [Test]
        public void 재추첨해도_등급은_바뀌지_않는다()
        {
            var draft = BeginDraft(2);            // 골드
            var wallet = new Wallet(10_000);

            Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success));

            foreach (var candidate in draft.Candidates)
            {
                Assert.That(candidate.Blessing.Tier, Is.EqualTo(BlessingTier.Gold),
                    "등급은 라운드로 정해지므로 재추첨으로 올릴 수 없다 (S-GD-HE87JY)");
            }
        }

        [Test]
        public void 설정의_라운드별_등급표를_그대로_읽는다()
        {
            Assert.That(BlessingConfig.Default.TryGetTierForRound(1, out var first), Is.True);
            Assert.That(first, Is.EqualTo(BlessingTier.Silver));

            Assert.That(BlessingConfig.Default.TryGetTierForRound(3, out var third), Is.True);
            Assert.That(third, Is.EqualTo(BlessingTier.Platinum));

            Assert.That(BlessingConfig.Default.TryGetTierForRound(4, out _), Is.False,
                "한 매치는 3라운드다 (S-GD-6H2Q5Q)");
        }

        [Test]
        public void 라운드별_비용표와_등급표의_길이가_다르면_설정을_만들_수_없다()
        {
            Assert.That(
                () => new BlessingConfig(
                    3, 2,
                    new[] { 100, 200, 300 },
                    new[] { BlessingTier.Silver, BlessingTier.Gold },   // 3라운드인데 등급 2개
                    2),
                Throws.TypeOf<ArgumentException>(),
                "둘 다 라운드마다 하나씩인 표다. 길이가 어긋나면 등급 없는 라운드가 생긴다");
        }

        [Test]
        public void 정의되지_않은_라운드는_시작조차_못한다()
        {
            Assert.That(() => BeginDraft(4), Throws.TypeOf<InvalidOperationException>(),
                "4라운드는 존재하지 않는다(S-GD-6H2Q5Q). 등급을 임의로 외삽하지 않고 드러낸다");
        }

        // ------------------------------------------------ 획득한 축복은 다시 안 나온다 (S-GD-VF67AG)

        [Test]
        public void 이미_획득한_축복은_후보에_나오지_않는다()
        {
            // 같은 라운드(=같은 등급)로 두 번 뽑아 S-GD-VF67AG 필터만 따로 검증한다.
            // 실제 게임에서는 라운드마다 등급이 달라(S-GD-HE87JY) 이 상황이 오지 않지만,
            // "등급을 올려주는 축복" 이 들어오면 그 보호가 사라지고 이 필터만 남는다.
            var acquired = new BlessingLoadout();
            foreach (var candidate in BeginDraft(1).Candidates)
            {
                acquired.Add(candidate.Blessing);
            }

            var next = BeginDraft(1, acquired: acquired);

            foreach (var candidate in next.Candidates)
            {
                Assert.That(acquired.Contains(candidate.Blessing), Is.False,
                    $"{candidate.Blessing} 은 이미 획득한 축복이다 (S-GD-VF67AG)");
            }
        }

        [Test]
        public void 획득_목록을_빠뜨리면_시작할_수_없다()
        {
            Assert.That(
                () => BlessingDraft.Begin(
                    BlessingConfig.Default, MakePool(), WeaponTag.Sword, 1, null,
                    new DeterministicRandom(Seed)),
                Throws.TypeOf<ArgumentNullException>(),
                "1라운드에도 빈 목록을 넘긴다. null 을 허용하면 중복이 조용히 생긴다");
        }

        [Test]
        public void 같은_축복을_두_번_획득할_수_없다()
        {
            var loadout = new BlessingLoadout();
            var blessing = BeginDraft().Select(0);

            loadout.Add(blessing);

            Assert.That(() => loadout.Add(blessing), Throws.TypeOf<InvalidOperationException>(),
                "후보 필터가 뚫린 것이므로 조용히 쌓지 않고 드러낸다 (S-GD-VF67AG)");
        }

        [Test]
        public void 뽑을_축복이_모자라면_조용히_넘어가지_않는다()
        {
            // 실버 공용 1개뿐인 풀 — 후보 3개를 채울 수 없다.
            var thinPool = new List<BlessingDefinition>
            {
                new BlessingDefinition("only", "하나뿐", WeaponTag.Common, BlessingTier.Silver),
            };

            Assert.That(
                () => BlessingDraft.Begin(
                    BlessingConfig.Default, thinPool, WeaponTag.Sword, 1, new BlessingLoadout(),
                    new DeterministicRandom(Seed)),
                Throws.TypeOf<InvalidOperationException>(),
                "후보가 2개만 뜨는 화면이 나오면 원인을 화면에서 알 수 없다");
        }

        // -------------------------------------------------------------- 잠금 (S-GD-S46S28)

        [Test]
        public void 잠금은_최대_2개까지다()
        {
            var draft = BeginDraft();

            Assert.That(draft.TryToggleLock(0), Is.True);
            Assert.That(draft.TryToggleLock(1), Is.True);
            Assert.That(draft.TryToggleLock(2), Is.False, "3개째 잠금은 거부된다");
            Assert.That(draft.LockedCount, Is.EqualTo(2));
        }

        [Test]
        public void 남은_잠금_수를_도메인이_알려준다()
        {
            var draft = BeginDraft();

            Assert.That(draft.RemainingLockCount, Is.EqualTo(2), "처음에는 2개를 잠글 수 있다");

            draft.TryToggleLock(0);
            Assert.That(draft.RemainingLockCount, Is.EqualTo(1));

            draft.TryToggleLock(1);
            Assert.That(draft.RemainingLockCount, Is.EqualTo(0), "화면은 이 값으로 '잠금 가능 N개' 를 그린다");

            draft.TryToggleLock(0);
            Assert.That(draft.RemainingLockCount, Is.EqualTo(1), "해제하면 다시 늘어난다");
        }
        [Test]
        public void 잠금을_풀면_다시_잠글_수_있다()
        {
            var draft = BeginDraft();
            draft.TryToggleLock(0);
            draft.TryToggleLock(1);

            Assert.That(draft.TryToggleLock(0), Is.True, "해제");
            Assert.That(draft.TryToggleLock(2), Is.True, "자리가 났으므로 잠금 가능");
        }

        // ------------------------------------------------------------ 재추첨 (S-GD-SHJGNB)

        [Test]
        public void 라운드별_최초_재추첨_비용은_100_200_300G_다()
        {
            for (var round = 1; round <= 3; round++)
            {
                Assert.That(BeginDraft(round).TryGetCurrentRerollCost(out var cost), Is.True);
                Assert.That(cost, Is.EqualTo(round * 100), $"{round}라운드 최초 재추첨");
            }
        }

        [Test]
        public void 같은_준비_단계에서_재추첨하면_비용이_2배씩_오른다()
        {
            var draft = BeginDraft(2);            // 최초 200G
            var wallet = new Wallet(10_000);

            draft.TryGetCurrentRerollCost(out var first);
            Assert.That(first, Is.EqualTo(200));

            Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success));
            draft.TryGetCurrentRerollCost(out var second);
            Assert.That(second, Is.EqualTo(400));

            Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success));
            draft.TryGetCurrentRerollCost(out var third);
            Assert.That(third, Is.EqualTo(800));

            Assert.That(wallet.Gold, Is.EqualTo(10_000 - 200 - 400), "지불한 만큼만 빠진다");
        }

        [Test]
        public void 라운드가_바뀌면_배수가_초기화된다()
        {
            var wallet = new Wallet(10_000);
            var round1 = BeginDraft(1);
            round1.TryReroll(wallet);
            round1.TryReroll(wallet);   // 1라운드에서 100 → 200 지불

            var round2 = BeginDraft(2);
            round2.TryGetCurrentRerollCost(out var cost);

            Assert.That(cost, Is.EqualTo(200), "2라운드는 최초 비용 200G 에서 다시 시작한다");
        }

        [Test]
        public void 골드가_모자라면_후보도_잔액도_변하지_않는다()
        {
            var draft = BeginDraft();
            var wallet = new Wallet(99);          // 최초 비용 100G
            var before = draft.Candidates;

            Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.NotEnoughGold));
            Assert.That(wallet.Gold, Is.EqualTo(99));
            Assert.That(draft.RerollCount, Is.EqualTo(0));

            var after = draft.Candidates;
            for (var i = 0; i < before.Count; i++)
            {
                Assert.That(after[i].Blessing.Id, Is.EqualTo(before[i].Blessing.Id));
            }
        }

        [Test]
        public void 재추첨은_잠근_후보를_그대로_두고_나머지만_바꾼다()
        {
            var draft = BeginDraft();
            var wallet = new Wallet(10_000);

            draft.TryToggleLock(0);
            var lockedId = draft.Candidates[0].Blessing.Id;

            Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success));

            Assert.That(draft.Candidates[0].Blessing.Id, Is.EqualTo(lockedId), "잠근 것은 유지된다");
            Assert.That(draft.Candidates[0].IsLocked, Is.True, "잠금 상태도 유지된다");
        }

        [Test]
        public void 재추첨해도_잠근_축복이_다른_자리에_또_뜨지_않는다()
        {
            var draft = BeginDraft();
            var wallet = new Wallet(10_000);

            draft.TryToggleLock(0);
            var lockedId = draft.Candidates[0].Blessing.Id;

            for (var i = 0; i < 3 && draft.TryReroll(wallet) == RerollResult.Success; i++)
            {
                var occurrences = 0;
                foreach (var candidate in draft.Candidates)
                {
                    if (candidate.Blessing.Id == lockedId)
                    {
                        occurrences++;
                    }
                }

                Assert.That(occurrences, Is.EqualTo(1), "잠근 축복이 중복으로 나타났다");
            }
        }

        // ---------------------------------------------------- 재추첨 가능 여부 (UI 사전 차단)

        [Test]
        public void 재추첨_가능_여부는_실제_판정과_같다()
        {
            var draft = BeginDraft();
            var wallet = new Wallet(150);   // 최초 100G → 1회 가능, 2회차 200G 는 불가

            Assert.That(draft.GetRerollAvailability(wallet), Is.EqualTo(RerollResult.Success));
            Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success));

            // 잔액 50G, 다음 비용 200G
            Assert.That(draft.GetRerollAvailability(wallet), Is.EqualTo(RerollResult.NotEnoughGold));
            Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.NotEnoughGold),
                "미리 알려준 판정과 실제 결과가 달라지면 버튼 상태와 동작이 어긋난다");
        }

        [Test]
        public void 재추첨_가능_여부를_묻는_것만으로는_아무것도_바뀌지_않는다()
        {
            var draft = BeginDraft();
            var wallet = new Wallet(10_000);
            var before = draft.Candidates;

            draft.GetRerollAvailability(wallet);
            draft.GetRerollAvailability(wallet);

            Assert.That(wallet.Gold, Is.EqualTo(10_000));
            Assert.That(draft.RerollCount, Is.EqualTo(0));
            for (var i = 0; i < before.Count; i++)
            {
                Assert.That(draft.Candidates[i].Blessing.Id, Is.EqualTo(before[i].Blessing.Id));
            }
        }

        [Test]
        public void 선택_후에는_재추첨이_불가능하다고_미리_알려준다()
        {
            var draft = BeginDraft();
            draft.Select(0);

            Assert.That(draft.GetRerollAvailability(new Wallet(10_000)),
                Is.EqualTo(RerollResult.AlreadySelected));
        }

        // ------------------------------------------------------------------ 등급 (S-GD-EQCVWS)

        [Test]
        public void 등급은_실버_골드_플래티넘_순서로_비교된다()
        {
            Assert.That(BlessingTier.Silver, Is.LessThan(BlessingTier.Gold));
            Assert.That(BlessingTier.Gold, Is.LessThan(BlessingTier.Platinum));
        }

        [Test]
        public void 등급을_빠뜨린_축복은_만들_수_없다()
        {
            Assert.That(() => new BlessingDefinition("x", "X", WeaponTag.Common, default),
                Throws.TypeOf<ArgumentOutOfRangeException>(),
                "default(BlessingTier) 는 0 이라 등급 누락이 여기서 잡혀야 한다");
        }

        // -------------------------------------------------------------- 선택 (S-GD-S46S28, S-GD-RFXF8X)

        [Test]
        public void 두_개를_잠가도_최종_선택은_한_개다()
        {
            var draft = BeginDraft();
            draft.TryToggleLock(0);
            draft.TryToggleLock(1);

            var selected = draft.Select(2);

            Assert.That(draft.IsSelected, Is.True);
            Assert.That(draft.Selected, Is.EqualTo(selected));
        }

        [Test]
        public void 선택_후에는_재추첨과_잠금이_막힌다()
        {
            var draft = BeginDraft();
            draft.Select(0);

            Assert.That(draft.TryReroll(new Wallet(10_000)), Is.EqualTo(RerollResult.AlreadySelected));
            Assert.That(draft.TryToggleLock(1), Is.False);
        }

        [Test]
        public void 축복은_매치_내에서_누적되고_새_매치에서_초기화된다()
        {
            var loadout = new BlessingLoadout();

            for (var round = 1; round <= 3; round++)
            {
                loadout.Add(BeginDraft(round, acquired: loadout).Select(0));
            }

            Assert.That(loadout.Count, Is.EqualTo(3), "라운드를 넘어 누적된다");

            loadout.ResetForNewMatch();

            Assert.That(loadout.Count, Is.EqualTo(0), "매치를 넘지 못한다 (S-GD-RFXF8X)");
        }

        [Test]
        public void 한_매치에서_얻는_축복은_등급이_하나씩_올라간다()
        {
            var loadout = new BlessingLoadout();

            for (var round = 1; round <= 3; round++)
            {
                loadout.Add(BeginDraft(round, acquired: loadout).Select(0));
            }

            Assert.That(loadout.Acquired[0].Tier, Is.EqualTo(BlessingTier.Silver));
            Assert.That(loadout.Acquired[1].Tier, Is.EqualTo(BlessingTier.Gold));
            Assert.That(loadout.Acquired[2].Tier, Is.EqualTo(BlessingTier.Platinum),
                "뒤로 갈수록 강해져야 역전의 여지가 남는다 (S-GD-HE87JY)");
        }

        // ------------------------------------------- 실제 확장 카탈로그 (S15P21A606-173, S-GD-55Z9AQ)
        //
        // 위의 검사들은 합성 풀을 쓴다. 규칙만 보려면 그게 맞지만, 그러면 **실제 CSV 풀이 얇아져도
        // 전부 통과한다.** 등급마다 20종으로 늘린 이유가 "한 라운드 + 무기 필터 + 획득 제외" 를
        // 모두 거친 뒤에도 후보 3장이 남게 하는 것이므로, 여기서는 런타임이 실제로 쓰는
        // <see cref="LocalPlayBlessingCatalog.All"/> 로 같은 경로를 밟는다.

        private static readonly WeaponTag[] EquippableTags = { WeaponTag.Sword, WeaponTag.Bow };

        private static BlessingDraft BeginRealDraft(int round, WeaponTag weapon, BlessingLoadout acquired, int seed) =>
            BlessingDraft.Begin(
                BlessingConfig.Default,
                LocalPlayBlessingCatalog.All,
                weapon,
                round,
                acquired,
                new DeterministicRandom(seed));

        [Test]
        public void 실제_카탈로그는_등급마다_20종_이상을_가진다()
        {
            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                var count = 0;
                foreach (var definition in BlessingMvpCatalog.All)
                {
                    if (definition.Definition.Tier == tier) count++;
                }

                Assert.That(count, Is.GreaterThanOrEqualTo(BlessingMvpCatalog.MinimumPerTier),
                    $"{tier} 가 {count}종이다 — 후보 3장을 채우지 못하면 같은 카드가 반복된다");
            }
        }

        [Test]
        public void 실제_카탈로그의_id_와_이름은_전부_고유하다()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var definition in BlessingMvpCatalog.All)
            {
                Assert.That(ids.Add(definition.Definition.Id), Is.True,
                    $"id '{definition.Definition.Id}' 가 중복이다");
                Assert.That(names.Add(definition.Definition.DisplayName), Is.True,
                    $"이름 '{definition.Definition.DisplayName}' 가 중복이다 — 이름을 개수 채우기로 쓰면 안 된다");
                Assert.That(definition.Definition.DisplayName, Is.Not.Empty, definition.Definition.Id);
                Assert.That(definition.Definition.Description, Is.Not.Empty, definition.Definition.Id);
            }
        }

        [Test]
        public void 실제_카탈로그의_id_접미사는_등급과_일치한다()
        {
            foreach (var definition in BlessingMvpCatalog.All)
            {
                var expected = definition.Definition.Tier switch
                {
                    BlessingTier.Silver => "-t1",
                    BlessingTier.Gold => "-t2",
                    BlessingTier.Platinum => "-t3",
                    _ => null,
                };

                Assert.That(expected, Is.Not.Null, definition.Definition.Id);
                Assert.That(definition.Definition.Id, Does.EndWith(expected),
                    $"{definition.Definition.Id} 의 접미사가 {definition.Definition.Tier} 와 다르다 (S-GD-ZQMB7Y)");
            }
        }

        [Test]
        public void 실제_카탈로그로도_모든_라운드_무기_조합에서_후보가_3장_뜬다()
        {
            foreach (var weapon in EquippableTags)
            {
                for (var round = 1; round <= 3; round++)
                {
                    var draft = BeginRealDraft(round, weapon, new BlessingLoadout(), Seed + round);
                    var ids = new HashSet<string>(StringComparer.Ordinal);

                    // 무기의 분류(검→근접, 활→원거리)를 실제 규약에서 가져온다. 여기에
                    // Melee/Ranged 를 통째로 허용하면 활에 근접 전용 축복이 떠도 통과한다.
                    Assert.That(BlessingWeaponTagCatalog.TryGetCategory(weapon, out var category), Is.True);

                    Assert.That(draft.Candidates.Count, Is.EqualTo(3), $"{weapon} {round}라운드");

                    foreach (var candidate in draft.Candidates)
                    {
                        Assert.That(ids.Add(candidate.Blessing.Id), Is.True,
                            $"{weapon} {round}라운드에 같은 후보가 두 번 떴다");
                        Assert.That(candidate.Blessing.Tier, Is.EqualTo(draft.Tier),
                            $"{weapon} {round}라운드 후보에 다른 등급이 섞였다 (S-GD-HE87JY)");
                        Assert.That(candidate.Blessing.Tag,
                            Is.EqualTo(WeaponTag.Common).Or.EqualTo(weapon).Or.EqualTo(category),
                            $"{candidate.Blessing.Id}({candidate.Blessing.Tag}) 는 {weapon} 과 "
                                + "호환되지 않는다 (S-GD-SB84DQ, S-GD-DPWH9W)");
                    }
                }
            }
        }

        [Test]
        public void 실제_카탈로그는_잠금_두_개를_유지한_재추첨을_여러_번_견딘다()
        {
            // 등급별 20종이 실제로 재추첨 여유를 주는지 본다. 풀이 얇으면 여기서 후보를
            // 채우지 못해 InvalidOperationException 이 난다.
            foreach (var weapon in EquippableTags)
            {
                var draft = BeginRealDraft(2, weapon, new BlessingLoadout(), Seed);
                var wallet = new Wallet(1_000_000);

                Assert.That(draft.TryToggleLock(0), Is.True);
                Assert.That(draft.TryToggleLock(1), Is.True);
                Assert.That(draft.TryToggleLock(2), Is.False, "최대 2개 (S-GD-S46S28)");

                var lockedFirst = draft.Candidates[0].Blessing.Id;
                var lockedSecond = draft.Candidates[1].Blessing.Id;

                for (var i = 0; i < 5; i++)
                {
                    Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success),
                        $"{weapon} {i + 1}번째 재추첨");
                    Assert.That(draft.Candidates[0].Blessing.Id, Is.EqualTo(lockedFirst));
                    Assert.That(draft.Candidates[1].Blessing.Id, Is.EqualTo(lockedSecond));
                    Assert.That(draft.Candidates[2].Blessing.Id,
                        Is.Not.EqualTo(lockedFirst).And.Not.EqualTo(lockedSecond),
                        "잠근 축복이 남은 자리에 또 떴다");
                }
            }
        }

        [Test]
        public void 실제_카탈로그에서_획득한_축복은_같은_등급_재추첨에도_다시_안_나온다()
        {
            var acquired = new BlessingLoadout();
            var wallet = new Wallet(1_000_000);

            // 실버를 계속 뽑아 획득 제외 필터만 압박한다. 등급당 20종이므로 여러 번 버틴다.
            for (var i = 0; i < 5; i++)
            {
                var draft = BeginRealDraft(1, WeaponTag.Sword, acquired, Seed + i);

                foreach (var candidate in draft.Candidates)
                {
                    Assert.That(acquired.Contains(candidate.Blessing), Is.False,
                        $"{candidate.Blessing.Id} 는 이미 획득했다 (S-GD-VF67AG)");
                }

                Assert.That(draft.TryReroll(wallet), Is.EqualTo(RerollResult.Success));

                foreach (var candidate in draft.Candidates)
                {
                    Assert.That(acquired.Contains(candidate.Blessing), Is.False,
                        $"재추첨 후 {candidate.Blessing.Id} 는 이미 획득했다 (S-GD-VF67AG)");
                }

                acquired.Add(draft.Select(0));
            }

            Assert.That(acquired.Count, Is.EqualTo(5));
        }
    }
}
