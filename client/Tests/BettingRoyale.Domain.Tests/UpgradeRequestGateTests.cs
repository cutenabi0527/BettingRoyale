using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;
using WeaponType = BettingRoyale.Data.Combat.WeaponType;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// <see cref="UpgradeRequestGate"/> 의 회귀 테스트 — 두 부위 강화가 <b>온라인에 붙기 전에</b>
    /// 지켜야 하는 것들 (S-GD-A2YDDJ · S-GD-Z9CJ4H · S-GD-66GTWA · S-CLI-9QYFCT).
    ///
    /// <para>
    /// <b>여기서 확인하는 것은 "게이트 하나만 부르면 끝난다" 는 계약이다.</b> 어댑터는 결과를 그대로
    /// 발행만 하므로(D-069 · D-079), 순번 소비·구역 판정·지갑 차감이 <b>이 게이트 안에서</b>
    /// 한 번씩만 일어나야 한다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 기대 금액은 아래 <see cref="WeaponCosts"/>·<see cref="ArmorCosts"/> 에 적은 숫자뿐이다.</b>
    /// 도메인의 강화 표에서 읽어와 비교하면 표가 틀려도 테스트가 같이 틀려 통과한다 —
    /// 확정 수치(S-GD-A2YDDJ)를 테스트에 직접 적어 <b>표가 바뀌면 여기서 깨지게</b> 둔다.
    /// </para>
    ///
    /// <para>
    /// 원장(<see cref="EconomyRequestLedger"/>)은 상점·룰렛·축복과 <b>같은 것</b>이다.
    /// 그래서 "다른 명령이 먹은 순번" 과 "강화가 먹은 순번" 이 서로를 막는지도 함께 본다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UpgradeRequestGateTests
    {
        private const int Me = 1;
        private const int Other = 2;
        private const int NotInMatch = 999;

        /// <summary>모르는 부위. 정상 클라이언트는 만들 수 없는 값이다 (회선에서 올 수 있다).</summary>
        private const int UnknownSlotPayload = 7;

        /// <summary>부위별 최대 단계 (S-GD-A2YDDJ: 0..5). <b>표에서 읽지 않고 여기 적는다.</b></summary>
        private const int MaxSlotLevel = 5;

        /// <summary>확정 무기 강화 비용 — 1강부터 5강까지 (S-GD-A2YDDJ).</summary>
        private static readonly int[] WeaponCosts = { 100, 150, 200, 300, 500 };

        /// <summary>확정 방어구 강화 비용 — 1강부터 5강까지 (S-GD-A2YDDJ).</summary>
        private static readonly int[] ArmorCosts = { 50, 75, 100, 175, 300 };

        /// <summary>모루 중심 (S-GD-66GTWA 임시 배치). 반경 안이다.</summary>
        private static readonly WorldPosition Anvil = new WorldPosition(-11f, 0f, 0f);

        /// <summary>어느 구역에도 없는 좌표.</summary>
        private static readonly WorldPosition FarAway = new WorldPosition(100f, 0f, 100f);

        /// <summary>룰렛 중심. <b>준비 시설 안이지만 모루가 아니다.</b></summary>
        private static readonly WorldPosition RouletteCenter = new WorldPosition(0f, 0f, 0f);

        private MatchEconomyState economy;
        private EconomyRequestLedger ledger;
        private FakePresence presence;
        private UpgradeRequestGate gate;
        private int nextSequence;

        /// <summary>
        /// 위치를 <b>Host 가 직접 재는 자리</b>를 대신한다 (S-CLI-9QYFCT).
        /// 등록하지 않은 플레이어는 "위치를 모른다" 다 — 전투원이 아직 없거나 이미 사라진 경우다.
        /// </summary>
        private sealed class FakePresence : IPreparationPresenceProvider
        {
            private readonly Dictionary<int, WorldPosition> positions = new Dictionary<int, WorldPosition>();

            public void Place(int playerId, WorldPosition position) => positions[playerId] = position;

            public void Forget(int playerId) => positions.Remove(playerId);

            public bool TryGetPosition(int playerId, out WorldPosition position) =>
                positions.TryGetValue(playerId, out position);
        }

        private static BlessingDefinition Common(string id, BlessingTier tier) =>
            new BlessingDefinition(id, id, WeaponTag.Common, tier);

        /// <summary>등급마다 넉넉히 채운다 — 등급은 라운드가 정한다 (S-GD-HE87JY).</summary>
        private static IReadOnlyList<BlessingDefinition> Pool()
        {
            var pool = new List<BlessingDefinition>();

            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (int i = 0; i < 12; i++)
                {
                    pool.Add(Common($"{tier}-{i}", tier));
                }
            }

            return pool;
        }

        /// <summary>
        /// <b>축복을 확정한 상태에서 시작한다.</b> 그것이 강화 화면이 열려 있는 정상 상황이기
        /// 때문이다 — 선택 전에는 강화가 통째로 막힌다(S-GD-DP2DVC).
        /// 선택 전 상황은 <see cref="BuildWorld"/> 를 직접 불러 만든다.
        /// </summary>
        [SetUp]
        public void SetUp() => BuildWorld(confirmSelection: true);

        /// <param name="confirmSelection">
        /// <c>false</c> 면 이번 준비 단계의 축복을 <b>고르지 않은 채</b> 둔다 —
        /// 제품에서 준비가 막 열린 순간의 상태다.
        /// </param>
        /// <param name="roll">
        /// 강화 성공 판정 (S15P21A606-304). 기본은 <b>언제나 성공</b>이다 — 이 파일은 판정 경로(구역·원장·골드)를
        /// 보고, 확률 실패는 그 경우만 따로 <see cref="UpgradeSuccessRoll.AlwaysFails"/> 로 만든다.
        /// </param>
        private void BuildWorld(bool confirmSelection, IUpgradeSuccessRoll roll = null)
        {
            IUpgradeSuccessRoll successRoll = roll ?? UpgradeSuccessRoll.AlwaysSucceeds;
            economy = new MatchEconomyState(
                EconomyConfig.Default, BlessingConfig.Default, Pool(),
                upgradeSuccessRollFactory: _ => successRoll);
            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(Me, WeaponTag.Sword),
                    new MatchParticipant(Other, WeaponTag.Bow),
                },
                seed: 12345);

            economy.BeginPreparation();

            ledger = new EconomyRequestLedger();
            ledger.OpenPreparation();

            presence = new FakePresence();
            presence.Place(Me, Anvil);
            presence.Place(Other, Anvil);

            // 구역 판정은 상점·룰렛과 같은 게이트를 쓴다 (S-CLI-9QYFCT). 여기서 따로 재지 않는다.
            var access = new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence);
            gate = new UpgradeRequestGate(economy, ledger, access);

            nextSequence = 1;

            if (confirmSelection)
            {
                ConfirmSelection(Me);
                ConfirmSelection(Other);
            }
        }

        /// <summary>
        /// 이번 준비 단계의 축복을 확정한다.
        ///
        /// <b>원장을 쓰지 않는 경로로 고른다</b> — 게이트를 거치면 순번이 하나 사라져
        /// 이 파일의 순번 테스트가 무엇을 재는지 흐려진다.
        /// </summary>
        private void ConfirmSelection(int playerId)
        {
            Assert.That(economy.TrySelect(playerId, 0), Is.Not.Null, "테스트 준비: 축복 확정 실패");
            Assert.That(economy.IsSelectionDone(playerId), Is.True, "테스트 준비: 축복 확정 실패");
        }

        [Test]
        public void 선택_시간_만료는_준비를_닫지_않고_잠긴_후보를_한번만_획득한다()
        {
            BuildWorld(confirmSelection: false);   // 아직 아무도 고르지 않은 준비 단계에서 시작한다

            Assert.That(economy.TryToggleLock(Me, 1), Is.True);
            string lockedId = economy.GetCandidates(Me)[1].Blessing.Id;
            int gold = economy.GetGold(Me);
            var selected = economy.TrySelectAutomatically(Me);
            Assert.That(selected.Id, Is.EqualTo(lockedId));
            Assert.That(economy.TrySelectAutomatically(Me), Is.Null);
            Assert.That(economy.Phase, Is.EqualTo(MatchEconomyPhase.Preparation));
            Assert.That(economy.IsSelectionDone(Me), Is.True);
            Assert.That(economy.GetAcquired(Me).Count, Is.EqualTo(1));
            Assert.That(economy.GetGold(Me), Is.EqualTo(gold));
            Assert.That(economy.EndPreparation(), Does.Not.Contain(Me));
            Assert.That(economy.GetAcquired(Me).Count, Is.EqualTo(1));
        }

        private EconomyRequestTicket NextTicket() =>
            new EconomyRequestTicket(ledger.Epoch, nextSequence++);

        /// <summary>
        /// 잔액을 정확한 값으로 맞춘다. <b>라운드 지급액에 기대를 걸지 않기 위한 것</b>이다 —
        /// 지급액이 바뀌어도 이 파일의 금액 테스트는 같은 것을 잰다.
        /// </summary>
        private void SetGold(int playerId, int target)
        {
            int now = economy.GetGold(playerId);

            if (now < target)
            {
                economy.Earn(playerId, target - now);
            }
            else if (now > target)
            {
                Assert.That(economy.TrySpend(playerId, now - target), Is.True,
                    "테스트 준비: 잔액을 목표값으로 맞추지 못했다");
            }

            Assert.That(economy.GetGold(playerId), Is.EqualTo(target), "테스트 준비: 잔액 설정 실패");
        }

        private UpgradeRequestOutcome RequestUpgrade(int playerId, UpgradeSlot slot) =>
            gate.HandleUpgrade(playerId, slot, NextTicket(), isMine: playerId == Me);

        // ── 성공 경로 ─────────────────────────────────────────────────────

        [Test]
        public void 무기와_방어구_요청은_같은_지갑에서_나간다()
        {
            SetGold(Me, 1000);

            var weapon = RequestUpgrade(Me, UpgradeSlot.Weapon);
            int afterWeapon = economy.GetGold(Me);

            var armor = RequestUpgrade(Me, UpgradeSlot.Armor);
            int afterArmor = economy.GetGold(Me);

            Assert.That(weapon.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(weapon.HasRejection, Is.False, "구역 안·잔액 충분이면 성공이다");
            Assert.That(weapon.Gold.Delta, Is.EqualTo(-WeaponCosts[0]));
            Assert.That(weapon.Gold.Gold, Is.EqualTo(afterWeapon), "이벤트에 실린 잔액은 현재 확정값이다");
            Assert.That(afterWeapon, Is.EqualTo(1000 - WeaponCosts[0]));

            Assert.That(armor.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(armor.HasRejection, Is.False);
            Assert.That(armor.Gold.Delta, Is.EqualTo(-ArmorCosts[0]));
            Assert.That(afterArmor, Is.EqualTo(1000 - WeaponCosts[0] - ArmorCosts[0]),
                "강화 전용 지갑이 생기면 여기서 깨진다 — 상점·룰렛과 잔액이 갈린다");

            Assert.That(armor.State.WeaponLevel, Is.EqualTo(1));
            Assert.That(armor.State.ArmorLevel, Is.EqualTo(1));
            Assert.That(armor.State.SetLevel, Is.EqualTo(1), "세트는 두 부위 중 낮은 쪽이다");
            Assert.That(armor.State.Gold, Is.EqualTo(afterArmor), "상태에 실린 잔액은 현재 확정값이다");
            Assert.That(armor.State.WeaponMaxLevel, Is.EqualTo(MaxSlotLevel));
            Assert.That(armor.State.ArmorMaxLevel, Is.EqualTo(MaxSlotLevel));
        }

        [Test]
        public void 세트_단계는_낮은_쪽_부위가_정한다()
        {
            SetGold(Me, 2000);

            RequestUpgrade(Me, UpgradeSlot.Weapon);
            var afterTwoWeapon = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(afterTwoWeapon.State.WeaponLevel, Is.EqualTo(2));
            Assert.That(afterTwoWeapon.State.ArmorLevel, Is.EqualTo(0));
            Assert.That(afterTwoWeapon.State.SetLevel, Is.EqualTo(0),
                "무기만 올려서 세트가 오르면 낮은 쪽 규칙이 깨진 것이다");

            var afterArmor = RequestUpgrade(Me, UpgradeSlot.Armor);

            Assert.That(afterArmor.State.SetLevel, Is.EqualTo(1));
        }

        // ── 재전송 ────────────────────────────────────────────────────────

        [Test]
        public void 같은_무기_요청을_두_번_보내도_한_번만_나가고_한_번만_오른다()
        {
            SetGold(Me, 1000);
            var ticket = NextTicket();

            var first = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, ticket, isMine: true);
            int afterFirst = economy.GetGold(Me);

            var replay = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, ticket, isMine: true);

            Assert.That(first.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(replay.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(economy.GetGold(Me), Is.EqualTo(afterFirst), "재전송에 골드가 또 나가면 안 된다");
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(1),
                "재전송에 단계가 두 번 오르면 한 번의 클릭으로 두 단계를 산 것이다");

            Assert.That(replay.HasRejection, Is.False, "이미 성공한 클릭에 거부를 띄우면 거짓말이 된다");
            Assert.That(replay.HasEvents, Is.True, "침묵은 성공이 아니다 (규약 §6)");
            Assert.That(replay.Gold.Delta, Is.EqualTo(0), "적용되지 않았으므로 연출용 델타는 0 이다");
            Assert.That(replay.Gold.Gold, Is.EqualTo(afterFirst));
            Assert.That(replay.State.WeaponLevel, Is.EqualTo(1));
        }

        [Test]
        public void 같은_방어구_요청을_두_번_보내도_한_번만_나가고_한_번만_오른다()
        {
            SetGold(Me, 1000);
            var ticket = NextTicket();

            gate.HandleUpgrade(Me, UpgradeRequestPayload.Pack(UpgradeSlot.Armor), ticket, isMine: true);
            int afterFirst = economy.GetGold(Me);

            var replay =
                gate.HandleUpgrade(Me, UpgradeRequestPayload.Pack(UpgradeSlot.Armor), ticket, isMine: true);

            Assert.That(replay.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(afterFirst, Is.EqualTo(1000 - ArmorCosts[0]));
            Assert.That(economy.GetGold(Me), Is.EqualTo(afterFirst));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Armor), Is.EqualTo(1));
        }

        [Test]
        public void 골드가_모자라_거절된_요청은_돈이_들어와도_다시_판정되지_않는다()
        {
            SetGold(Me, 0);
            var ticket = NextTicket();

            var rejected = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, ticket, isMine: true);

            Assert.That(rejected.HasRejection, Is.True);
            Assert.That(rejected.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NotEnoughGold));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));

            // 상자 보상 등으로 돈이 들어왔다. 사용자는 이미 "골드 부족" 을 보고 손을 뗐다.
            economy.Earn(Me, 1000);

            var replay = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, ticket, isMine: true);

            Assert.That(replay.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate),
                "거부도 '처리했다' 로 기록돼야 한다 (EconomyRequestVerdict.Duplicate 주석)");
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000), "이미 포기한 클릭이 나중에 성공하면 안 된다");
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        // ── 원장 공유 ─────────────────────────────────────────────────────

        [Test]
        public void 다른_명령이_먼저_순번을_먹으면_강화는_적용되지_않는다()
        {
            SetGold(Me, 1000);
            var ticket = NextTicket();

            // 상점·룰렛 게이트가 같은 원장에서 이 순번을 이미 소비했다.
            Assert.That(ledger.Accept(Me, ticket), Is.EqualTo(EconomyRequestVerdict.Fresh));

            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, ticket, isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(outcome.HasRejection, Is.False);
            Assert.That(outcome.HasEvents, Is.True);
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        [Test]
        public void 강화가_먹은_순번은_다른_명령이_다시_쓸_수_없다()
        {
            SetGold(Me, 1000);
            var ticket = NextTicket();

            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, ticket, isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(ledger.Accept(Me, ticket), Is.EqualTo(EconomyRequestVerdict.Duplicate),
                "강화가 원장을 소비하지 않으면 같은 요청으로 재추첨까지 한 번 더 할 수 있다");
        }

        [Test]
        public void 남의_순번은_내_강화를_막지_않는다()
        {
            SetGold(Me, 1000);
            SetGold(Other, 1000);
            var ticket = NextTicket();

            var mine = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, ticket, isMine: true);
            var theirs = gate.HandleUpgrade(Other, UpgradeSlot.Weapon, ticket, isMine: false);

            Assert.That(mine.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(theirs.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh),
                "막히면 남이 내 강화를 취소시킬 수 있다");
            Assert.That(economy.GetUpgradeLevel(Other, UpgradeSlot.Weapon), Is.EqualTo(1));
        }

        // ── 표 형식·신원 ──────────────────────────────────────────────────

        [Test]
        public void 지난_준비_단계의_요청은_이번_골드를_쓰지_않는다()
        {
            SetGold(Me, 1000);
            var staleTicket = NextTicket();

            economy.EndPreparation();
            economy.TryApplyRoundResults(
                1,
                new[] { new RoundOutcome(Me, 0, 0, true), new RoundOutcome(Other, 0, 0, false) });
            economy.BeginPreparation();
            ledger.OpenPreparation();

            int before = economy.GetGold(Me);
            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, staleTicket, isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.StaleEpoch));
            Assert.That(outcome.HasRejection, Is.False);
            Assert.That(economy.GetGold(Me), Is.EqualTo(before));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        [Test]
        public void 순번이_0인_요청은_형식_오류로_걸린다()
        {
            SetGold(Me, 1000);

            var outcome = gate.HandleUpgrade(
                Me, UpgradeSlot.Weapon, new EconomyRequestTicket(ledger.Epoch, 0), isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.Malformed));
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        [Test]
        public void 참가자가_아니면_아무것도_발행하지_않고_원장도_건드리지_않는다()
        {
            var outcome = gate.HandleUpgrade(NotInMatch, UpgradeSlot.Weapon, NextTicket(), isMine: false);

            Assert.That(outcome.HasEvents, Is.False, "모르는 id 로 예외가 나거나 이벤트가 나가면 안 된다");
            Assert.That(outcome.HasRejection, Is.False);
            Assert.That(ledger.TrackedPlayerCount, Is.EqualTo(0),
                "참가자도 아닌 id 로 Host 원장에 줄이 생기면 메모리를 늘릴 수 있다");
        }

        [Test]
        public void 모르는_부위는_지갑도_단계도_건드리지_않고_거절된다()
        {
            SetGold(Me, 1000);

            var outcome = gate.HandleUpgrade(Me, UnknownSlotPayload, NextTicket(), isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh),
                "모르는 부위도 '처리했다' 로 기록돼 같은 순번으로 다시 시도할 수 없어야 한다");
            Assert.That(outcome.HasRejection, Is.True);
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.UnknownSlot));
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000), "무기 값을 내고 방어구가 오르는 경로가 되면 안 된다");
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Armor), Is.EqualTo(0));
        }

        [Test]
        public void 모르는_부위_요청도_같은_순번으로_다시_판정받을_수_없다()
        {
            SetGold(Me, 1000);
            var ticket = NextTicket();

            gate.HandleUpgrade(Me, UnknownSlotPayload, ticket, isMine: true);
            var replay = gate.HandleUpgrade(Me, UpgradeRequestPayload.Pack(UpgradeSlot.Weapon), ticket, isMine: true);

            Assert.That(replay.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        // ── 구역 판정 (S-GD-66GTWA · S-CLI-9QYFCT) ────────────────────────

        [Test]
        public void 모루_구역_밖에서는_강화되지_않는다()
        {
            SetGold(Me, 1000);
            presence.Place(Me, FarAway);

            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, NextTicket(), isMine: true);

            Assert.That(outcome.HasRejection, Is.True);
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NotInRange));
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        [Test]
        public void 위치를_모르면_강화되지_않는다()
        {
            SetGold(Me, 1000);
            presence.Forget(Me);

            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, NextTicket(), isMine: true);

            Assert.That(outcome.HasRejection, Is.True);
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NotInRange),
                "위치를 모르면 '멀다' 로 판정한다 — 모르는 것을 통과시키면 맵 반대편에서 강화된다");
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000));
        }

        [Test]
        public void 다른_시설_구역에_서_있어도_강화되지_않는다()
        {
            SetGold(Me, 1000);
            presence.Place(Me, RouletteCenter);

            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, NextTicket(), isMine: true);

            Assert.That(outcome.HasRejection, Is.True);
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NotInRange));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        [Test]
        public void 구역_밖에서는_모르는_부위보다_구역이_먼저_걸린다()
        {
            SetGold(Me, 1000);
            presence.Place(Me, FarAway);

            var outcome = gate.HandleUpgrade(Me, UnknownSlotPayload, NextTicket(), isMine: true);

            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NotInRange),
                "모르는 부위로 보내 구역 판정을 건너뛰는 경로가 생기면 안 된다");
        }

        [Test]
        public void 구역_밖의_상태는_두_부위가_모두_막혀_온다()
        {
            SetGold(Me, 1000);
            presence.Place(Me, FarAway);

            var state = gate.BuildState(Me, isMine: true);

            Assert.That(state.CanUpgradeWeapon, Is.False, "켜져 있는데 눌리지 않는 버튼이 생긴다");
            Assert.That(state.CanUpgradeArmor, Is.False);
            Assert.That(state.WeaponBlockedBy, Is.EqualTo(UpgradeRejectReason.NotInRange));
            Assert.That(state.ArmorBlockedBy, Is.EqualTo(UpgradeRejectReason.NotInRange));
        }

        [Test]
        public void 구역_안이면_상태의_두_부위가_열려_온다()
        {
            SetGold(Me, 1000);

            var state = gate.BuildState(Me, isMine: true);

            Assert.That(state.WeaponBlockedBy, Is.EqualTo(UpgradeRejectReason.None));
            Assert.That(state.ArmorBlockedBy, Is.EqualTo(UpgradeRejectReason.None));
            Assert.That(state.WeaponNextCostGold, Is.EqualTo(WeaponCosts[0]));
            Assert.That(state.ArmorNextCostGold, Is.EqualTo(ArmorCosts[0]));
            // 0강 → 1강 시도는 100% 다 (엑셀 upgrade_levels, S15P21A606-304).
            Assert.That(state.WeaponNextSuccessRatePercent, Is.EqualTo(100));
            Assert.That(state.ArmorNextSuccessRatePercent, Is.EqualTo(100));
        }

        // ── 단계·데이터 ───────────────────────────────────────────────────

        [Test]
        public void 준비_단계가_아니면_강화되지_않는다()
        {
            SetGold(Me, 1000);
            economy.EndPreparation();

            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, NextTicket(), isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.PreparationClosed));
            Assert.That(outcome.HasRejection, Is.True);
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NotPreparation));
            Assert.That(outcome.HasEvents, Is.True, "화면은 현재 상태를 받아야 한다");
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        [Test]
        public void 최대_단계에_도달하면_더_올릴_수_없다()
        {
            SetGold(Me, 5000);

            for (int level = 0; level < MaxSlotLevel; level++)
            {
                var step = RequestUpgrade(Me, UpgradeSlot.Weapon);

                Assert.That(step.HasRejection, Is.False, $"{level + 1}강이 거절됐다");
                Assert.That(step.Gold.Delta, Is.EqualTo(-WeaponCosts[level]));
            }

            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(MaxSlotLevel));

            var overMax = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(overMax.HasRejection, Is.True);
            Assert.That(overMax.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.MaxLevelReached));
            Assert.That(overMax.Gold.Delta, Is.EqualTo(0));
            Assert.That(overMax.State.WeaponHasNextLevel, Is.False);
            Assert.That(overMax.State.WeaponBlockedBy, Is.EqualTo(UpgradeRejectReason.MaxLevelReached));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(MaxSlotLevel),
                "최대 단계를 넘어 오르면 안 된다");
        }

        [Test]
        public void 무기_표가_없으면_데이터_없음으로_거절된다()
        {
            SetGold(Me, 1000);

            // 전투원이 없어 무기를 모르는 상태 — 표를 고를 근거가 없다
            // (UpgradeRejectReason.NoTrack). 조작이 아니라 데이터가 없는 것이다.
            Assert.That(economy.TrySetEquippedWeaponType(Me, WeaponType.None), Is.True);

            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, NextTicket(), isMine: true);

            Assert.That(outcome.HasRejection, Is.True);
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NoTrack));
            Assert.That(outcome.State.WeaponBlockedBy, Is.EqualTo(UpgradeRejectReason.NoTrack),
                "상태와 요청 판정이 어긋나면 켜져 있는 버튼이 눌리지 않는다");
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000), "표가 없는데 돈이 나가면 안 된다");
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        // ── 확정 금액 (S-GD-A2YDDJ) ───────────────────────────────────────

        [Test]
        public void 무기4강_방어구4강은_합쳐_1150G다()
        {
            const int TotalForFourAndFour = 1150;

            SetGold(Me, TotalForFourAndFour);

            for (int level = 0; level < 4; level++)
            {
                var weapon = RequestUpgrade(Me, UpgradeSlot.Weapon);
                Assert.That(weapon.Gold.Delta, Is.EqualTo(-WeaponCosts[level]),
                    $"무기 {level + 1}강 비용");

                var armor = RequestUpgrade(Me, UpgradeSlot.Armor);
                Assert.That(armor.Gold.Delta, Is.EqualTo(-ArmorCosts[level]),
                    $"방어구 {level + 1}강 비용");
            }

            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(4));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Armor), Is.EqualTo(4));
            Assert.That(economy.GetUpgradeSetLevel(Me), Is.EqualTo(4));
            Assert.That(economy.GetGold(Me), Is.EqualTo(0),
                "4+4 는 정확히 1150G 다 — 남거나 모자라면 표가 바뀐 것이다");
        }

        [Test]
        public void 준비_골드_1000G로_3강3강_후_한쪽4강까지_살_수_있다()
        {
            SetGold(Me, 1000);
            for (int level = 0; level < 3; level++)
            {
                Assert.That(RequestUpgrade(Me, UpgradeSlot.Weapon).HasRejection, Is.False);
                Assert.That(RequestUpgrade(Me, UpgradeSlot.Armor).HasRejection, Is.False);
            }
            Assert.That(economy.GetGold(Me), Is.EqualTo(325), "1000 - 675");
            Assert.That(RequestUpgrade(Me, UpgradeSlot.Weapon).HasRejection, Is.False);
            Assert.That(economy.GetGold(Me), Is.EqualTo(25), "검4강 300G");
            var armor = RequestUpgrade(Me, UpgradeSlot.Armor);
            Assert.That(armor.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NotEnoughGold));
            Assert.That(economy.GetGold(Me), Is.EqualTo(25), "거절은 돈을 차감하지 않는다");
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(4));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Armor), Is.EqualTo(3));
            Assert.That(economy.GetUpgradeSetLevel(Me), Is.EqualTo(3));
        }

        // ── 라운드·매치 경계 (S-GD-A2YDDJ: 라운드마다 0강, 환급 없음) ─────

        [Test]
        public void 라운드가_바뀌면_단계는_0으로_돌아가고_골드는_환급되지_않는다()
        {
            SetGold(Me, 1000);
            SetGold(Other, 1000);

            RequestUpgrade(Me, UpgradeSlot.Weapon);

            // 같은 금액을 강화가 아닌 지출로 쓴다 — 라운드 경계 뒤 두 사람의 잔액이
            // 갈리면 그 차이는 "강화만 환급됐다" 는 뜻이다 (이월 한도와 무관하게 잡힌다).
            Assert.That(economy.TrySpend(Other, WeaponCosts[0]), Is.True);
            Assert.That(economy.GetGold(Me), Is.EqualTo(economy.GetGold(Other)), "테스트 준비: 출발 잔액 동일");

            economy.EndPreparation();
            economy.TryApplyRoundResults(
                1,
                new[] { new RoundOutcome(Me, 0, 0, true), new RoundOutcome(Other, 0, 0, false) });
            economy.BeginPreparation();
            ledger.OpenPreparation();

            // 새 준비 단계가 열렸으므로 축복도 새로 고른다 — 고르기 전에는 강화가 막힌다.
            ConfirmSelection(Me);

            Assert.That(economy.Round, Is.EqualTo(2));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0),
                "강화는 라운드마다 0강으로 돌아간다 (S-GD-A2YDDJ)");
            Assert.That(economy.GetUpgradeSetLevel(Me), Is.EqualTo(0));
            Assert.That(economy.GetGold(Me), Is.EqualTo(economy.GetGold(Other)),
                "초기화되면서 강화 비용이 돌아오면 안 된다 — 환급은 없다");

            // 새 라운드에서는 다시 1강부터 산다.
            SetGold(Me, 1000);
            var again = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(again.Gold.Delta, Is.EqualTo(-WeaponCosts[0]),
                "0강으로 돌아갔으므로 다음 비용도 1강 값이다");
            Assert.That(again.State.Round, Is.EqualTo(2));
        }

        [Test]
        public void 새_매치는_단계도_원장도_다시_시작한다()
        {
            SetGold(Me, 1000);
            RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(1));

            var previousMatchTicket = new EconomyRequestTicket(ledger.Epoch, 500);

            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(Me, WeaponTag.Sword),
                    new MatchParticipant(Other, WeaponTag.Bow),
                },
                seed: 999);
            economy.BeginPreparation();
            ledger.OpenPreparation();

            Assert.That(economy.Round, Is.EqualTo(1));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Armor), Is.EqualTo(0));

            int before = economy.GetGold(Me);
            var outcome = gate.HandleUpgrade(Me, UpgradeSlot.Weapon, previousMatchTicket, isMine: true);

            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.StaleEpoch),
                "지난 매치의 요청이 새 매치의 골드를 쓰면 안 된다");
            Assert.That(economy.GetGold(Me), Is.EqualTo(before));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        // ── 축복 확정이 먼저다 (S-GD-DP2DVC) ──────────────────────────────
        //
        // 이 판정은 한때 Network 어댑터에만 있었다. 게이트가 직접 보지 않으면 **같은 게이트를 쓰는
        // 다른 경로에는 규칙이 없다** — 아래 테스트는 게이트 하나만 불러서 확인한다.

        [Test]
        public void 축복을_고르기_전에는_강화가_거절된다()
        {
            BuildWorld(confirmSelection: false);
            SetGold(Me, 1000);

            var outcome = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(outcome.HasRejection, Is.True, "침묵은 성공이 아니다 (규약 §6)");
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.SelectionRequired));
            Assert.That(outcome.Verdict, Is.EqualTo(EconomyRequestVerdict.Fresh),
                "거절도 '처리했다' 로 기록돼야 같은 순번으로 다시 시도할 수 없다");
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000), "선택 전에 돈이 나가면 안 된다");
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(0));
        }

        [Test]
        public void 축복을_고르기_전에는_상태의_두_부위가_선택_요구로_막혀_온다()
        {
            BuildWorld(confirmSelection: false);
            SetGold(Me, 1000);

            var state = gate.BuildState(Me, isMine: true);

            Assert.That(state.CanUpgradeWeapon, Is.False, "켜져 있는데 눌리지 않는 버튼이 생긴다");
            Assert.That(state.CanUpgradeArmor, Is.False);
            Assert.That(state.WeaponBlockedBy, Is.EqualTo(UpgradeRejectReason.SelectionRequired));
            Assert.That(state.ArmorBlockedBy, Is.EqualTo(UpgradeRejectReason.SelectionRequired));
        }

        [Test]
        public void 축복을_확정하면_그_자리에서_강화가_열린다()
        {
            BuildWorld(confirmSelection: false);
            SetGold(Me, 1000);

            Assert.That(RequestUpgrade(Me, UpgradeSlot.Weapon).Rejection.Reason,
                Is.EqualTo(UpgradeRejectReason.SelectionRequired));

            ConfirmSelection(Me);

            var opened = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(opened.HasRejection, Is.False, "확정한 뒤에도 막히면 강화를 영영 못 쓴다");
            Assert.That(opened.Gold.Delta, Is.EqualTo(-WeaponCosts[0]));
            Assert.That(opened.State.WeaponBlockedBy, Is.EqualTo(UpgradeRejectReason.None));
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(1));
        }

        [Test]
        public void 남이_고르지_않았다고_내_강화가_막히지는_않는다()
        {
            BuildWorld(confirmSelection: false);
            ConfirmSelection(Me);            // 나만 골랐다
            SetGold(Me, 1000);

            var mine = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(mine.HasRejection, Is.False,
                "판정은 사람마다 따로다 — 남이 고민하는 동안 내 준비가 멈추면 안 된다");
            Assert.That(economy.GetUpgradeLevel(Me, UpgradeSlot.Weapon), Is.EqualTo(1));
        }

        [Test]
        public void 선택_전에는_구역_판정보다_선택_요구가_먼저_걸린다()
        {
            BuildWorld(confirmSelection: false);
            SetGold(Me, 1000);
            presence.Place(Me, FarAway);

            var outcome = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.SelectionRequired),
                "선택 중에는 이동이 막혀 어차피 구역 밖이다 — '모루에서 멀어졌습니다' 는 할 수 없는 일을 시킨다");
            Assert.That(outcome.State.WeaponBlockedBy, Is.EqualTo(UpgradeRejectReason.SelectionRequired),
                "상태와 요청 판정이 어긋나면 화면이 서로 다른 안내를 띄운다");
        }

        [Test]
        public void 선택_전에는_모르는_부위보다_선택_요구가_먼저_걸린다()
        {
            BuildWorld(confirmSelection: false);
            SetGold(Me, 1000);

            var outcome = gate.HandleUpgrade(Me, UnknownSlotPayload, NextTicket(), isMine: true);

            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.SelectionRequired),
                "모르는 부위로 보내 선택 판정을 건너뛰는 경로가 생기면 안 된다");
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000));
        }

        [Test]
        public void 준비_단계가_아니면_선택_요구가_아니라_준비_아님이다()
        {
            BuildWorld(confirmSelection: false);
            SetGold(Me, 1000);
            economy.EndPreparation();

            var outcome = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NotPreparation),
                "전투 중에는 고를 축복 자체가 없다 — 선택을 요구하면 할 수 없는 일을 시키는 안내가 된다");
            Assert.That(gate.BuildState(Me, isMine: true).WeaponBlockedBy,
                Is.EqualTo(UpgradeRejectReason.NotPreparation));
        }

        /// <summary>
        /// <b>준비 단계가 아니면 축복 확정을 아예 묻지 않는다.</b>
        ///
        /// <para>
        /// 위 테스트만으로는 <c>RequiresSelection</c> 의 단계 가드를 지워도 통과한다 —
        /// 전투 중에는 두 부위가 이미 닫혀 있어 <c>WithBlockedIfOpen</c> 이 아무것도 안 하고,
        /// 요청 경로는 그보다 앞에서 <c>NotPreparation</c> 으로 끝나기 때문이다.
        /// 그래서 <b>물었는지 자체</b>를 본다.
        /// </para>
        /// </summary>
        [Test]
        public void 준비_단계가_아니면_축복_확정을_묻지도_않는다()
        {
            BuildWorld(confirmSelection: false);
            SetGold(Me, 1000);

            var probe = new UpgradeRequestGate(
                economy,
                ledger,
                new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence),
                new PreparationOnlySelectionPolicy(economy));

            economy.EndPreparation();

            Assert.That(
                probe.HandleUpgrade(Me, UpgradeSlot.Weapon, NextTicket(), isMine: true).Rejection.Reason,
                Is.EqualTo(UpgradeRejectReason.NotPreparation));
            Assert.That(probe.BuildState(Me, isMine: true).WeaponBlockedBy,
                Is.EqualTo(UpgradeRejectReason.NotPreparation));
        }

        /// <summary>
        /// 준비 단계가 아닐 때 불리면 <b>그 자리에서 실패시킨다.</b> 전투 중에 축복 확정을 묻는
        /// 것은 언제나 "안 골랐다" 가 되는 질문이라, 그 답으로 화면을 막으면 플레이어가
        /// <b>할 수 없는 일</b>을 하라고 안내받는다.
        /// </summary>
        private sealed class PreparationOnlySelectionPolicy : IPreparationSelectionPolicy
        {
            private readonly MatchEconomyState economy;

            public PreparationOnlySelectionPolicy(MatchEconomyState economy) => this.economy = economy;

            public bool IsSelectionConfirmed(int playerId)
            {
                Assert.That(economy.Phase, Is.EqualTo(MatchEconomyPhase.Preparation),
                    "준비 단계가 아닌데 축복 확정을 물었다 — RequiresSelection 의 단계 가드가 사라졌다");
                return economy.IsSelectionDone(playerId);
            }
        }

        // ── 둔기: 표가 없는 무기 (S-GD-A2YDDJ) ────────────────────────────

        [Test]
        public void 둔기는_강화표가_없어_데이터_없음으로_거절되고_방어구는_그대로_열린다()
        {
            Assert.That(PlaceholderUpgradeCatalog.Create().HasWeapon(WeaponType.Mace), Is.False,
                "테스트 전제: 둔기 표는 아직 없다. 표가 들어왔다면 이 테스트를 갱신한다");

            SetGold(Me, 1000);
            Assert.That(economy.TrySetEquippedWeaponType(Me, WeaponType.Mace), Is.True);

            var outcome = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(outcome.HasRejection, Is.True, "조용히 아무 일도 안 일어나면 안 된다");
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(UpgradeRejectReason.NoTrack),
                "표가 없는 것은 조작 오류가 아니라 데이터가 없는 것이다");
            Assert.That(outcome.State.EquippedWeapon, Is.EqualTo(WeaponType.Mace),
                "화면이 무기 이름과 함께 사유를 그릴 수 있어야 한다");
            Assert.That(outcome.State.WeaponBlockedBy, Is.EqualTo(UpgradeRejectReason.NoTrack));
            Assert.That(outcome.State.WeaponMaxLevel, Is.EqualTo(0), "표가 없으면 최대 단계도 0 이다");
            Assert.That(outcome.State.WeaponHasNextLevel, Is.False);
            Assert.That(economy.GetGold(Me), Is.EqualTo(1000), "표가 없는데 돈이 나가면 안 된다");

            // 무기 표가 없어도 방어구는 종류 구분이 없다 — 한쪽 데이터 결손이 다른 쪽을 막지 않는다.
            var armor = RequestUpgrade(Me, UpgradeSlot.Armor);

            Assert.That(armor.HasRejection, Is.False);
            Assert.That(armor.State.ArmorBlockedBy, Is.EqualTo(UpgradeRejectReason.None));
            Assert.That(armor.State.ArmorLevel, Is.EqualTo(1));
            Assert.That(armor.State.SetLevel, Is.EqualTo(0), "무기가 0강이라 세트는 안 붙는다");
        }

        // ── 세트 효과가 실제 스탯으로 붙는가 (S-GD-A2YDDJ) ────────────────
        //
        // UpgradeState 단위 테스트(UpgradeTests)는 조합 36가지를 이미 본다. 여기서 보는 것은
        // **게이트로 산 단계가 경제를 거쳐 같은 시트에 붙는가** 다 — 그 사이가 끊기면 화면의
        // 단계는 오르는데 전투는 그대로다.

        /// <summary>축복과 같은 스탯 정의를 쓴다 — 강화는 별도 체계가 아니다 (S-GD-A2YDDJ).</summary>
        private static StatSheet NewSheet() => new StatSheet(new StatRegistry(new List<StatDefinition>
        {
            new StatDefinition("attack-power", "공격력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition("armor-pen", "방어력 관통", 0f, StatKind.Flat, min: 0f),
            new StatDefinition("attack-speed", "공격 속도", 1f, StatKind.Multiplier),
            new StatDefinition("max-hp", "최대 체력", 100f, StatKind.Flat, min: 1f),
            new StatDefinition("defense", "방어력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition("move-speed", "이동 속도", 5f, StatKind.Multiplier),
            new StatDefinition("damage-bonus", "피해 증가", 0f, StatKind.Flat),
            new StatDefinition("damage-taken-mult", "받는 피해", 1f, StatKind.Multiplier),

            // 무기 전용 세트(검 사거리·활 화살 반경, 엑셀 upgrade_sets)가 가리킨다.
            new StatDefinition("attack-range", "공격 사거리", 1f, StatKind.Multiplier),
            new StatDefinition("projectile-radius", "화살 충돌 반경", 1f, StatKind.Multiplier),
        }));

        [Test]
        public void 게이트로_5강5강을_사면_1_3_5강_세트가_전부_붙는다()
        {
            SetGold(Me, 10_000);

            for (int i = 0; i < MaxSlotLevel; i++)
            {
                Assert.That(RequestUpgrade(Me, UpgradeSlot.Weapon).HasRejection, Is.False);
                Assert.That(RequestUpgrade(Me, UpgradeSlot.Armor).HasRejection, Is.False);
            }

            Assert.That(economy.GetUpgradeSetLevel(Me), Is.EqualTo(MaxSlotLevel));

            var sheet = NewSheet();
            economy.ApplyUpgradeToSheet(Me, sheet);

            // 기대값은 엑셀 upgrade_sets 의 누적 규칙에서 직접 계산했다 — 카탈로그를 다시 읽지 않는다 (S15P21A606-304).
            Assert.That(sheet.Get("move-speed"), Is.EqualTo(5f * 1.10f).Within(0.0001f),
                "1강 +3% · 3강 +3% · 5강 +4% 가 전부 붙어 +10% 다");
            Assert.That(sheet.Get("damage-bonus"), Is.EqualTo(0.10f).Within(0.0001f), "3% + 3% + 4%");
            Assert.That(sheet.Get("damage-taken-mult"), Is.EqualTo(0.90f).Within(0.0001f), "3% + 3% + 4% 감소");
            Assert.That(sheet.Get("defense"), Is.EqualTo(6f + 2f).Within(0.0001f),
                "방어구 5강 +6 과 5강 세트 +2 가 함께 붙는다 (S-GD-2TZT7S)");
        }

        [Test]
        public void 게이트로_한쪽만_올리면_세트는_낮은_쪽_단계까지만_붙는다()
        {
            SetGold(Me, 10_000);

            for (int i = 0; i < MaxSlotLevel; i++)
            {
                Assert.That(RequestUpgrade(Me, UpgradeSlot.Weapon).HasRejection, Is.False);
            }

            var onlyWeapon = RequestUpgrade(Me, UpgradeSlot.Armor);   // 방어구는 1강까지만

            Assert.That(onlyWeapon.State.SetLevel, Is.EqualTo(1), "세트는 낮은 쪽(방어구 1강)이 정한다");

            var sheet = NewSheet();
            economy.ApplyUpgradeToSheet(Me, sheet);

            Assert.That(sheet.Get("move-speed"), Is.EqualTo(5f * 1.03f).Within(0.0001f),
                "1강 세트만 붙는다 — 몰빵에 3·5강 세트가 붙으면 균등 배분의 이유가 사라진다");
            Assert.That(sheet.Get("damage-bonus"), Is.EqualTo(0.03f).Within(0.0001f), "1강 세트의 3% 만 — 3강 세트는 아직이다");
            Assert.That(sheet.Get("damage-taken-mult"), Is.EqualTo(0.97f).Within(0.0001f));
        }

        // ── 확률 실패 (S15P21A606-304) ───────────────────────────────────

        /// <summary>
        /// 확률 실패는 <b>거절이 아니다</b> — 거절 이벤트 없이 골드만 빠진 상태가 나간다. 화면은 앞뒤 상태로
        /// 실패를 가른다(S-UX-HQ92B5). 거절로 보내면 화면이 결과 비교를 취소해 실패를 말하지 못한다.
        /// </summary>
        [Test]
        public void 확률에서_떨어지면_거절_없이_골드만_빠지고_단계는_그대로다()
        {
            BuildWorld(confirmSelection: true, roll: UpgradeSuccessRoll.AlwaysFails);
            SetGold(Me, 1_000);

            // 1·2강은 100% 라 판정기가 실패를 내도 성공한다.
            Assert.That(RequestUpgrade(Me, UpgradeSlot.Weapon).State.WeaponLevel, Is.EqualTo(1));
            Assert.That(RequestUpgrade(Me, UpgradeSlot.Weapon).State.WeaponLevel, Is.EqualTo(2));
            Assert.That(economy.GetGold(Me), Is.EqualTo(1_000 - WeaponCosts[0] - WeaponCosts[1]));
            Assert.That(gate.BuildState(Me, isMine: true).WeaponNextSuccessRatePercent, Is.EqualTo(90));

            var failed = RequestUpgrade(Me, UpgradeSlot.Weapon);

            Assert.That(failed.HasEvents, Is.True);
            Assert.That(failed.HasRejection, Is.False, "확률 실패는 거절이 아니다");
            Assert.That(failed.ChanceFailed, Is.True);
            Assert.That(failed.State.WeaponLevel, Is.EqualTo(2), "단계는 내려가지도 오르지도 않는다");
            Assert.That(failed.Gold.Delta, Is.EqualTo(-WeaponCosts[2]), "3강 비용은 돌려주지 않는다");
            Assert.That(economy.GetGold(Me), Is.EqualTo(1_000 - WeaponCosts[0] - WeaponCosts[1] - WeaponCosts[2]));
        }
    }
}
