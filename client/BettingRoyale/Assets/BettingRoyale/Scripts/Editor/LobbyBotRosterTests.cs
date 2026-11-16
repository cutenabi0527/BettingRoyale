#if UNITY_EDITOR
using BettingRoyale.Data.Online.Lobby;
using NUnit.Framework;

namespace BettingRoyale.Editor
{
    /// <summary>
    /// 대기방 봇 슬롯·권한 규칙 회귀 테스트 (S-ONL-9GXTYQ).
    /// UnityEngine · Fusion 없이 검증 가능한 범위만 다룬다 — 이 파일은 순수 도메인
    /// 테스트 프로젝트(<c>client/Tests/BettingRoyale.Lobby.Tests</c>)에서도 그대로 컴파일된다.
    /// </summary>
    public class LobbyBotRosterTests
    {
        private const int Host = 1;
        private const int Guest = 2;
        private const int Stranger = 3;

        private static LobbyBotRoster Roster() => new LobbyBotRoster();

        // ---------- 권한 ----------

        [Test]
        public void Host_AlwaysHasControlWithoutAnyGrant()
        {
            Assert.That(Roster().HasControl(Host, Host), Is.True);
        }

        [Test]
        public void Participant_HasNoControlUntilGranted()
        {
            LobbyBotRoster roster = Roster();
            Assert.That(roster.HasControl(Guest, Host), Is.False);

            Assert.That(roster.Grant(Guest, Host), Is.True);
            Assert.That(roster.HasControl(Guest, Host), Is.True);
        }

        [Test]
        public void Grant_IsIdempotentAndNeverDuplicatesTheHost()
        {
            LobbyBotRoster roster = Roster();

            Assert.That(roster.Grant(Guest, Host), Is.True);
            Assert.That(roster.Grant(Guest, Host), Is.False, "이미 가진 권한을 다시 줘도 변경이 아니다");
            Assert.That(roster.Grant(Host, Host), Is.False, "방장은 목록에 들어가지 않는다");
            Assert.That(roster.GrantedPlayerIds.Count, Is.EqualTo(1));
        }

        [Test]
        public void OperatingCapacity_IsSharedByHumanAndBots()
        {
            LobbyBotRoster roster = Roster();
            int capacity = LobbyRoomRules.MaxCapacity;
            for (int i = 1; i < capacity; i++)
                Assert.That(roster.TryAdd(Host, Host, 1, capacity), Is.EqualTo(LobbyBotActionResult.Ok));
            Assert.That(roster.TryAdd(Host, Host, 1, capacity), Is.Not.EqualTo(LobbyBotActionResult.Ok));
            Assert.That(roster.Count, Is.EqualTo(capacity - 1));
            Assert.That(roster.TrimToCapacity(2, capacity), Is.EqualTo(1));
            Assert.That(roster.Count + 2, Is.EqualTo(capacity));
        }

        [Test]
        public void Revoke_RemovesControlAndReportsWhetherAnythingChanged()
        {
            LobbyBotRoster roster = Roster();
            roster.Grant(Guest, Host);

            Assert.That(roster.Revoke(Guest), Is.True);
            Assert.That(roster.HasControl(Guest, Host), Is.False);
            Assert.That(roster.Revoke(Guest), Is.False);
        }

        [Test]
        public void ForgetPlayer_DropsGrantSoAReusedPlayerIdDoesNotInheritIt()
        {
            // Fusion 은 빈 슬롯의 PlayerRef 번호를 재사용한다. 지우지 않으면 다음에 이 번호로
            // 들어온 **다른 사람**이 앞사람의 권한을 그대로 갖게 된다.
            LobbyBotRoster roster = Roster();
            roster.Grant(Guest, Host);

            Assert.That(roster.ForgetPlayer(Guest), Is.True);
            Assert.That(roster.HasControl(Guest, Host), Is.False);
        }

        [Test]
        public void ForgetPlayer_KeepsBotsAddedByThatPlayer()
        {
            LobbyBotRoster roster = Roster();
            roster.Grant(Guest, Host);
            roster.TryAdd(Guest, Host, humanCount: 2, maxPlayers: 8);

            roster.ForgetPlayer(Guest);

            Assert.That(roster.Count, Is.EqualTo(1), "추가한 사람이 나가도 봇은 방에 남는다");
        }

        // ---------- 추가 / 제거 ----------

        [Test]
        public void Add_IsRefusedForParticipantsWithoutControl()
        {
            LobbyBotRoster roster = Roster();

            Assert.That(
                roster.TryAdd(Stranger, Host, humanCount: 2, maxPlayers: 8),
                Is.EqualTo(LobbyBotActionResult.NotPermitted));
            Assert.That(roster.Count, Is.Zero);
        }

        [Test]
        public void Remove_IsRefusedForParticipantsWithoutControl()
        {
            LobbyBotRoster roster = Roster();
            roster.TryAdd(Host, Host, humanCount: 1, maxPlayers: 8);

            Assert.That(
                roster.TryRemove(Stranger, Host, botId: 0),
                Is.EqualTo(LobbyBotActionResult.NotPermitted));
            Assert.That(roster.Count, Is.EqualTo(1));
        }

        [Test]
        public void Add_StopsAtCapacityCountingHumansAndBotsTogether()
        {
            LobbyBotRoster roster = Roster();

            // 정원 4 · 사람 2 → 봇은 2칸까지.
            Assert.That(roster.TryAdd(Host, Host, 2, 4), Is.EqualTo(LobbyBotActionResult.Ok));
            Assert.That(roster.TryAdd(Host, Host, 2, 4), Is.EqualTo(LobbyBotActionResult.Ok));
            Assert.That(roster.TryAdd(Host, Host, 2, 4), Is.EqualTo(LobbyBotActionResult.RoomFull));
            Assert.That(roster.Count, Is.EqualTo(2));
        }

        [Test]
        public void Add_StopsAtBotLimitSoAtLeastOneHumanSeatRemains()
        {
            LobbyBotRoster roster = Roster();

            // 정원 4 · 사람 1(방장뿐) → 상한은 3이지 4가 아니다.
            for (var i = 0; i < 3; i++)
            {
                Assert.That(roster.TryAdd(Host, Host, 1, 4), Is.EqualTo(LobbyBotActionResult.Ok));
            }

            Assert.That(roster.TryAdd(Host, Host, 1, 4), Is.EqualTo(LobbyBotActionResult.LimitReached));
            Assert.That(roster.Count, Is.EqualTo(LobbyBotRoster.MaxBots(4)));
        }

        [Test]
        public void Remove_WithoutIdDropsTheNewestBot()
        {
            LobbyBotRoster roster = Roster();
            roster.TryAdd(Host, Host, 1, 8);
            roster.TryAdd(Host, Host, 1, 8);
            roster.TryAdd(Host, Host, 1, 8);

            Assert.That(roster.TryRemove(Host, Host, 0), Is.EqualTo(LobbyBotActionResult.Ok));
            Assert.That(roster.Count, Is.EqualTo(2));
            Assert.That(roster.Slots[0].Number, Is.EqualTo(1));
            Assert.That(roster.Slots[1].Number, Is.EqualTo(2));
        }

        [Test]
        public void Remove_ByIdTargetsThatExactBot()
        {
            LobbyBotRoster roster = Roster();
            roster.TryAdd(Host, Host, 1, 8);
            roster.TryAdd(Host, Host, 1, 8);

            int first = roster.Slots[0].Id;
            Assert.That(roster.TryRemove(Host, Host, first), Is.EqualTo(LobbyBotActionResult.Ok));
            Assert.That(roster.Count, Is.EqualTo(1));
            Assert.That(roster.Slots[0].Number, Is.EqualTo(2));
        }

        [Test]
        public void Remove_ReportsNoBotsAndNotFoundSeparately()
        {
            LobbyBotRoster roster = Roster();
            Assert.That(roster.TryRemove(Host, Host, 0), Is.EqualTo(LobbyBotActionResult.NoBots));

            roster.TryAdd(Host, Host, 1, 8);
            Assert.That(roster.TryRemove(Host, Host, 99999), Is.EqualTo(LobbyBotActionResult.NotFound));
        }

        [Test]
        public void FreedSlotNumberIsReusedSoNamesStayShort()
        {
            LobbyBotRoster roster = Roster();
            roster.TryAdd(Host, Host, 1, 8);
            roster.TryAdd(Host, Host, 1, 8);
            roster.TryRemove(Host, Host, roster.Slots[0].Id);

            roster.TryAdd(Host, Host, 1, 8);

            Assert.That(roster.Count, Is.EqualTo(2));
            Assert.That(roster.Slots[1].Number, Is.EqualTo(1), "비어 있는 가장 작은 번호를 다시 쓴다");
            Assert.That(roster.Slots[1].Name, Is.EqualTo("봇 1"));
        }

        // ---------- 식별자 ----------

        [Test]
        public void BotIdsNeverCollideWithSessionPlayerIds()
        {
            LobbyBotRoster roster = Roster();
            roster.TryAdd(Host, Host, 1, 8);

            Assert.That(LobbyBotRoster.IsBotId(roster.Slots[0].Id), Is.True);
            Assert.That(LobbyBotRoster.IsBotId(Host), Is.False);
            Assert.That(LobbyBotRoster.IsBotId(LobbyRoomRules.MaxCapacity), Is.False);
        }

        // ---------- 사람 우선 ----------

        [Test]
        public void Trim_DropsNewestBotsWhenHumansFillTheRoom()
        {
            // Photon 의 MaxPlayers 는 봇을 세지 않으므로, 정원이 봇으로 찬 방에도 사람이 들어온다.
            LobbyBotRoster roster = Roster();
            for (var i = 0; i < 3; i++)
            {
                roster.TryAdd(Host, Host, 1, 4);
            }

            Assert.That(roster.Count, Is.EqualTo(3));

            // 사람이 3명이 되면 봇은 1칸만 남아야 한다.
            Assert.That(roster.TrimToCapacity(humanCount: 3, maxPlayers: 4), Is.EqualTo(2));
            Assert.That(roster.Count, Is.EqualTo(1));
            Assert.That(roster.Slots[0].Number, Is.EqualTo(1), "가장 먼저 추가된 봇이 남는다");
        }

        [Test]
        public void Trim_DoesNothingWhenThereIsStillRoom()
        {
            LobbyBotRoster roster = Roster();
            roster.TryAdd(Host, Host, 1, 8);

            Assert.That(roster.TrimToCapacity(humanCount: 2, maxPlayers: 8), Is.Zero);
            Assert.That(roster.Count, Is.EqualTo(1));
        }

        [Test]
        public void Trim_CanEmptyTheRosterWhenHumansAloneFillTheRoom()
        {
            LobbyBotRoster roster = Roster();
            roster.TryAdd(Host, Host, 1, 4);
            roster.TryAdd(Host, Host, 1, 4);

            Assert.That(roster.TrimToCapacity(humanCount: 4, maxPlayers: 4), Is.EqualTo(2));
            Assert.That(roster.Count, Is.Zero);
        }

        // ---------- 시작 인원 ----------

        [Test]
        public void MinimumRoster_CountsBotsButStillRequiresAHuman()
        {
            Assert.That(LobbyBotRoster.MeetsMinimumRoster(1, 1), Is.True, "사람 1 + 봇 1 이면 시작 인원을 채운다");
            Assert.That(LobbyBotRoster.MeetsMinimumRoster(2, 0), Is.True);
            Assert.That(LobbyBotRoster.MeetsMinimumRoster(1, 0), Is.False);
            Assert.That(LobbyBotRoster.MeetsMinimumRoster(0, 8), Is.False, "사람이 없으면 시작하지 않는다");
        }

        // ---------- 복제 스냅샷 ----------

        [Test]
        public void ApplySnapshot_ReplacesEverythingAndKeepsRemovalOrderWorking()
        {
            LobbyBotRoster host = Roster();
            host.Grant(Guest, Host);
            host.TryAdd(Host, Host, 1, 8);
            host.TryAdd(Guest, Host, 1, 8);

            var client = Roster();
            client.ApplySnapshot(host.Slots, host.GrantedPlayerIds);

            Assert.That(client.Count, Is.EqualTo(2));
            Assert.That(client.HasControl(Guest, Host), Is.True);
            Assert.That(client.HasControl(Stranger, Host), Is.False);

            // 순번을 이어받아야 이후 추가·제거가 Host 와 같은 순서로 동작한다.
            client.TryAdd(Host, Host, 1, 8);
            Assert.That(client.TryRemove(Host, Host, 0), Is.EqualTo(LobbyBotActionResult.Ok));
            Assert.That(client.Count, Is.EqualTo(2));
            Assert.That(client.Slots[1].Number, Is.EqualTo(2), "방금 추가한 3번 봇이 먼저 지워진다");
        }

        [Test]
        public void ApplySnapshot_AcceptsEmptyInputWithoutThrowing()
        {
            LobbyBotRoster roster = Roster();
            roster.TryAdd(Host, Host, 1, 8);

            roster.ApplySnapshot(null, null);

            Assert.That(roster.Count, Is.Zero);
            Assert.That(roster.GrantedPlayerIds.Count, Is.Zero);
        }

        [Test]
        public void Clear_ResetsSlotsGrantsAndNumbering()
        {
            LobbyBotRoster roster = Roster();
            roster.Grant(Guest, Host);
            roster.TryAdd(Host, Host, 1, 8);

            roster.Clear();

            Assert.That(roster.Count, Is.Zero);
            Assert.That(roster.HasControl(Guest, Host), Is.False);

            roster.TryAdd(Host, Host, 1, 8);
            Assert.That(roster.Slots[0].Number, Is.EqualTo(1));
            Assert.That(roster.Slots[0].Sequence, Is.EqualTo(1));
        }
    }
}
#endif
