using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BettingRoyale.Core;
using BettingRoyale.Data.Contracts;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// <c>docs/client/ui-domain-contract.md</c> 의 규약을 코드로 고정한다 (Jira S15P21A606-31).
    ///
    /// <b>왜 리플렉션인가</b>: 이 규약들은 "새 타입을 추가할 때 지켜야 하는 것" 이라
    /// 개별 타입을 하나씩 검사하면 <b>다음에 추가되는 것을 놓친다.</b> 목업에서 난 사고가 전부
    /// "하나만 규칙을 안 지켰는데 아무도 몰랐다" 였다. 그래서 폴더 전체를 훑는다.
    /// </summary>
    [TestFixture]
    public class ContractRuleTests
    {
        /// <summary><c>BettingRoyale.Data.Contracts</c> 네임스페이스의 public 타입 전부.</summary>
        private static IEnumerable<Type> ContractTypes =>
            typeof(GoldChanged).Assembly
                .GetTypes()
                .Where(t => t.Namespace == "BettingRoyale.Data.Contracts" && t.IsPublic);

        private static IEnumerable<Type> CommandTypes =>
            ContractTypes.Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal));

        private static string Describe(IEnumerable<Type> types) =>
            string.Join(", ", types.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));

        // ── §4 화면 → 도메인 통로는 EventBus 하나뿐이다 ───────────────────────

        [Test]
        public void 명령은_전부_IGameEvent_를_구현한다()
        {
            // EventBus.Publish<T> 에 where T : IGameEvent 제약이 있다.
            // 하나라도 빠지면 화면이 그 명령을 보낼 방법이 아예 없어진다 — 그런데
            // 컴파일 에러는 화면 쪽에서만 나서, 계약을 쓴 사람은 원인을 알기 어렵다.
            var missing = CommandTypes
                .Where(t => !typeof(IGameEvent).IsAssignableFrom(t))
                .ToList();

            Assert.That(
                missing,
                Is.Empty,
                $"IGameEvent 를 구현하지 않은 명령: {Describe(missing)}. " +
                "규약 §4 — 화면은 EventBus 로만 명령을 보낸다.");
        }

        [Test]
        public void 명령이_하나도_없지는_않다()
        {
            // 위 테스트는 명령이 0개여도 통과한다. 폴더 구조가 바뀌어 리플렉션이
            // 아무것도 못 찾는 상태를 "통과" 로 읽지 않도록 하한을 둔다.
            //
            // ⚠️ 하한은 실제 개수를 따라간다. 2026-09-09 현재 7 이다 —
            //    축복 3 + 상점 1 + 룰렛 2 + 강화 2. 명령을 지웠는데 하한을 안 내리면 여기서 걸리고,
            //    명령을 늘렸는데 안 올리면 그만큼 조용히 사라져도 통과한다.
            //    강화 2 = 부위 명령(UpgradeSlotCommand) + 무기 별칭으로 남긴 옛 명령(WeaponUpgradeCommand).
            Assert.That(CommandTypes.Count(), Is.GreaterThanOrEqualTo(8));
        }

        // ── §5 명령에는 "누가 보냈는가" 를 담지 않는다 ─────────────────────────

        [Test]
        public void 명령에는_보낸_사람_필드가_없다()
        {
            // 목업에서 보내는 쪽이 자기 id 를 적어 넣는 구조 때문에 남의 id 를 적으면
            // 남의 골드를 쓸 수 있었다. 칸이 있는 한 가능하므로 칸 자체를 없앤다.
            var offenders = CommandTypes
                .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Select(f => (Type: t, Field: f)))
                .Where(x => x.Field.Name is "PlayerId" or "SenderId" or "OpenerId" or "ClaimerId")
                .Select(x => $"{x.Type.Name}.{x.Field.Name}")
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                $"명령에 보낸 사람 필드가 있다: {string.Join(", ", offenders)}. " +
                "규약 §5 — 누가 보냈는지는 어댑터가 연결 정보에서 판단한다.");
        }

        // ── §7 "누구 것인가" 는 IsMine 으로 온다 ──────────────────────────────

        [Test]
        public void PlayerId_를_가진_이벤트는_IsMine_도_가진다()
        {
            // 일부만 IsMine 을 가지면 UI 가 "어느 이벤트에 있더라" 를 외워야 하고,
            // 빠뜨린 화면은 남의 값을 그린다. 컴파일러도 lint 도 못 잡는다.
            var offenders = ContractTypes
                .Where(t => typeof(IGameEvent).IsAssignableFrom(t))
                .Where(t => t.GetField("PlayerId") != null && t.GetField("IsMine") == null)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                $"PlayerId 는 있는데 IsMine 이 없는 이벤트: {Describe(offenders)}. " +
                "규약 §7 — 플레이어별 이벤트는 전원분이 오고 화면이 IsMine 으로 거른다.");
        }

        [Test]
        public void IsMine_은_bool_이다()
        {
            var wrong = ContractTypes
                .Select(t => t.GetField("IsMine"))
                .Where(f => f != null && f.FieldType != typeof(bool))
                .Select(f => $"{f.DeclaringType?.Name}.IsMine : {f.FieldType.Name}")
                .ToList();

            Assert.That(wrong, Is.Empty, string.Join(", ", wrong));
        }

        // ── §6 침묵은 성공이 아니다 — 명령마다 거부 경로가 있다 ────────────────

        [Test]
        public void 거부될_수_있는_명령에는_거부_이벤트가_짝으로_있다()
        {
            // 짝을 표로 못박는다. 명령을 새로 추가하면서 거부 이벤트를 빠뜨리면
            // 화면은 "거부" 와 "아직 응답 없음" 을 구분할 수 없다 — 목업의
            // "눌러도 아무 일 없는 버튼" 이 정확히 그 상태였다.
            var pairs = new (Type Command, Type Rejection)[]
            {
                (typeof(BlessingLockToggleCommand), typeof(BlessingLockRejected)),
                (typeof(BlessingRerollCommand), typeof(BlessingRerollRejected)),
                (typeof(BlessingSelectCommand), typeof(BlessingSelectRejected)),
                (typeof(BlessingContractCommand), typeof(BlessingContractRejected)),
                (typeof(ShopPurchaseCommand), typeof(ShopPurchaseRejected)),
                (typeof(RouletteBetCommand), typeof(RouletteBetRejected)),
                (typeof(RouletteWithdrawCommand), typeof(RouletteWithdrawRejected)),
                (typeof(WeaponUpgradeCommand), typeof(WeaponUpgradeRejected)),
                (typeof(TrainingTravelCommand), typeof(TrainingTravelRejected)),
                (typeof(TrainingActionCommand), typeof(TrainingActionRejected)),

                // 부위 강화 (S-GD-A2YDDJ). 성공 이벤트는 없다 — 바뀐 상태(UpgradeStateChanged)와
                // GoldChanged 가 성공이고, 실패만 따로 온다. 상점 구매와 같은 모양이다.
                (typeof(UpgradeSlotCommand), typeof(UpgradeSlotRejected)),

                (typeof(LobbyWeaponChoiceCommand), typeof(LobbyWeaponChoiceRejected)),

                // ⚠️ 상자 명령 3종은 2026-09-07 에 삭제됐다 (D-079, S15P21A606-93).
                //    상자는 화면 버튼이 아니라 월드 상호작용이라 입력이 전투에서 온다.
                //    거부 이벤트 타입(ChestActionRejected)은 남아 있지만 발행자가 0이다 — O-031.
            };

            foreach (var (command, rejection) in pairs)
            {
                Assert.That(
                    typeof(IGameEvent).IsAssignableFrom(rejection),
                    Is.True,
                    $"{command.Name} 의 거부 이벤트 {rejection.Name} 이 IGameEvent 가 아니다.");
            }

            // 이제 모든 명령에 거부 이벤트가 있다. 예외였던 ChestOpenCancelCommand 가
            // 명령 자체와 함께 사라졌기 때문이다 (D-079). 새 명령을 추가하면서 거부 이벤트를
            // 빠뜨리면 여기서 걸린다.
            var withoutRejection = CommandTypes
                .Where(c => !pairs.Any(p => p.Command == c))
                .Select(c => c.Name)
                .ToList();

            Assert.That(
                withoutRejection,
                Is.Empty,
                $"거부 이벤트가 없는 명령이 있다: {string.Join(", ", withoutRejection)}");
        }

        // ── §10 이름 하나에 정의 하나 ──────────────────────────────────────────

        [Test]
        public void 계약_타입_이름은_계약_폴더_안에서_유일하다()
        {
            // 전체 어셈블리를 가로지르는 검사는 EditMode 쪽
            // (Scripts/Editor/ContractNameCollisionTests.cs) 이 한다 — 여기서는
            // Scripts/UI/ 가 컴파일되지 않아 볼 수 없기 때문이다.
            var duplicates = ContractTypes
                .GroupBy(t => t.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.That(duplicates, Is.Empty, $"이름이 겹치는 계약 타입: {string.Join(", ", duplicates)}");
        }

        // ── §11 배열은 null 대신 길이 0 ────────────────────────────────────────

        [Test]
        public void 배열_필드를_가진_이벤트는_그_사실이_문서화돼_있다()
        {
            // 실행 시점의 null 여부는 여기서 검사할 수 없다(발행자가 없다).
            // 대신 배열 필드가 어디에 있는지를 드러내서, 어댑터를 쓰는 사람이
            // "여기는 빈 배열을 넣어야 한다" 를 놓치지 않게 한다.
            var arrayFields = ContractTypes
                .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.FieldType.IsArray)
                    .Select(f => $"{t.Name}.{f.Name}"))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.That(
                arrayFields,
                Is.EquivalentTo(new[]
                {
                    "BlessingOfferChanged.Acquired",
                    "BlessingOfferChanged.Candidates",
                    "ChestsChanged.Chests",
                    "RouletteStateChanged.MyBets",
                    "RouletteWheelAnnounced.Layout",
                    "RouletteWheelAnnounced.Numbers",
                    "ShopOffersChanged.Offers",
                }),
                "배열 필드 목록이 바뀌었다. 규약 §11 — null 대신 길이 0 을 보낸다. " +
                $"현재: {string.Join(", ", arrayFields)}");
        }
    }
}
