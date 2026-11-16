using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 시간이 끝났을 때의 자동 선택 (S-GD-NK8ZJ7).
    /// 잠근 후보가 있으면 그 중에서, 없으면 전체에서 무작위다.
    /// </summary>
    [TestFixture]
    public class BlessingAutoSelectTests
    {
        private static BlessingDefinition Common(string id) =>
            new BlessingDefinition(id, id, WeaponTag.Common, BlessingTier.Silver);

        private static BlessingDraft BeginDraft(int seed) =>
            BlessingDraft.Begin(
                BlessingConfig.Default,
                new List<BlessingDefinition>
                {
                    Common("a"), Common("b"), Common("c"), Common("d"), Common("e"), Common("f"),
                },
                WeaponTag.Sword,
                round: 1,
                new BlessingLoadout(),
                new DeterministicRandom(seed));

        [Test]
        public void 잠근_후보가_있으면_그_중에서만_고른다()
        {
            // 서로 다른 씨앗으로 반복해도 잠근 자리 밖으로 나가지 않아야 한다.
            for (int seed = 1; seed <= 40; seed++)
            {
                var draft = BeginDraft(seed);
                Assert.That(draft.TryToggleLock(1), Is.True);

                var lockedId = draft.Candidates[1].Blessing.Id;
                var picked = draft.SelectAutomatically();

                Assert.That(picked.Id, Is.EqualTo(lockedId), $"seed {seed} 에서 잠그지 않은 후보가 뽑혔습니다.");
            }
        }

        [Test]
        public void 두_개를_잠그면_그_둘_중에서만_고른다()
        {
            var seen = new HashSet<string>();

            for (int seed = 1; seed <= 40; seed++)
            {
                var draft = BeginDraft(seed);
                Assert.That(draft.TryToggleLock(0), Is.True);
                Assert.That(draft.TryToggleLock(2), Is.True);

                var allowed = new[] { draft.Candidates[0].Blessing.Id, draft.Candidates[2].Blessing.Id };
                var picked = draft.SelectAutomatically();

                Assert.That(allowed, Does.Contain(picked.Id), $"seed {seed} 에서 잠그지 않은 후보가 뽑혔습니다.");
                seen.Add(picked.Id == allowed[0] ? "first" : "second");
            }

            // 둘 다 뽑혀야 "둘 중 무작위" 다. 한쪽만 나오면 사실상 고정 선택이다.
            Assert.That(seen.Count, Is.EqualTo(2), "잠근 두 후보 중 한쪽만 뽑히고 있습니다 — 무작위가 아닙니다.");
        }

        [Test]
        public void 잠금이_없으면_전체_후보에서_고른다()
        {
            var pickedIndexes = new HashSet<int>();

            for (int seed = 1; seed <= 60; seed++)
            {
                var draft = BeginDraft(seed);
                var ids = draft.Candidates.Select(c => c.Blessing.Id).ToList();

                var picked = draft.SelectAutomatically();

                int index = ids.IndexOf(picked.Id);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"seed {seed} 에서 후보 밖의 축복이 뽑혔습니다.");
                pickedIndexes.Add(index);
            }

            // 첫 후보 고정(옛 동작)이면 여기서 1이 나온다.
            Assert.That(
                pickedIndexes.Count, Is.GreaterThan(1),
                "항상 같은 자리만 뽑히고 있습니다 — 무작위가 아니라 고정 선택입니다.");
        }

        [Test]
        public void 자동_선택도_한_번만_할_수_있다()
        {
            var draft = BeginDraft(seed: 7);
            draft.SelectAutomatically();

            Assert.That(draft.IsSelected, Is.True);
            Assert.Throws<System.InvalidOperationException>(() => draft.SelectAutomatically());
        }

        /// <summary>
        /// Host 가 뽑아 복제하는 값이라 <b>같은 씨앗이면 같은 결과</b>여야 한다.
        /// 여기가 깨지면 피어마다 다른 축복을 보게 된다.
        /// </summary>
        [Test]
        public void 같은_씨앗이면_같은_축복이_뽑힌다()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var first = BeginDraft(seed).SelectAutomatically();
                var second = BeginDraft(seed).SelectAutomatically();

                Assert.That(second.Id, Is.EqualTo(first.Id), $"seed {seed} 에서 결과가 갈렸습니다.");
            }
        }
    }
}
