#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using BettingRoyale.Data.Online.Lobby;
using BettingRoyale.Network;
using NUnit.Framework;

namespace BettingRoyale.Editor
{
    /// <summary>
    /// 봇 로스터 와이어 규약 회귀 테스트 (D-ONL-AM6C7M).
    /// <b>Fusion 을 켜지 않고</b> 직렬화 계약만 검증한다 — 전송 자체는 런타임 2인 검증의 몫이다.
    /// </summary>
    public class LobbyBotWireProtocolTests
    {
        private const int Host = 1;

        private static List<LobbyBotSlot> Slots(params int[] numbers)
        {
            var slots = new List<LobbyBotSlot>(numbers.Length);
            for (var i = 0; i < numbers.Length; i++)
            {
                slots.Add(new LobbyBotSlot(numbers[i], Host, i + 1));
            }

            return slots;
        }

        [Test]
        public void State_RoundTripsHostSlotsAndGrants()
        {
            byte[] payload = LobbyBotWireProtocol.SerializeState(
                Host, Slots(1, 2, 5), new List<int> { 4, 7 });

            Assert.That(
                LobbyBotWireProtocol.TryDeserializeState(
                    payload, out int host, out List<LobbyBotSlot> slots, out List<int> grants),
                Is.True);

            Assert.That(host, Is.EqualTo(Host));
            Assert.That(slots.Count, Is.EqualTo(3));
            Assert.That(slots[2].Number, Is.EqualTo(5));
            Assert.That(slots[2].Id, Is.EqualTo(LobbyBotRoster.BotIdBase + 5));
            Assert.That(slots[2].Name, Is.EqualTo("봇 5"), "이름은 싣지 않고 번호에서 다시 만든다");
            Assert.That(slots[2].Sequence, Is.EqualTo(3));
            Assert.That(grants, Is.EquivalentTo(new[] { 4, 7 }));
        }

        [Test]
        public void State_RoundTripsAnEmptyRoster()
        {
            byte[] payload = LobbyBotWireProtocol.SerializeState(Host, new List<LobbyBotSlot>(), new List<int>());

            Assert.That(
                LobbyBotWireProtocol.TryDeserializeState(
                    payload, out int host, out List<LobbyBotSlot> slots, out List<int> grants),
                Is.True);

            Assert.That(host, Is.EqualTo(Host));
            Assert.That(slots, Is.Empty);
            Assert.That(grants, Is.Empty);
        }

        [Test]
        public void State_RejectsAWrongVersionInsteadOfReadingHalfOfIt()
        {
            byte[] payload = LobbyBotWireProtocol.SerializeState(Host, Slots(1), new List<int>());
            payload[0] = 99;

            Assert.That(
                LobbyBotWireProtocol.TryDeserializeState(payload, out _, out List<LobbyBotSlot> slots, out _),
                Is.False);
            Assert.That(slots, Is.Null, "실패하면 부분 결과를 내보내지 않는다");
        }

        [Test]
        public void State_RejectsATruncatedPayload()
        {
            byte[] payload = LobbyBotWireProtocol.SerializeState(Host, Slots(1, 2), new List<int> { 4 });
            var truncated = new byte[payload.Length - 3];
            Array.Copy(payload, truncated, truncated.Length);

            Assert.That(LobbyBotWireProtocol.TryDeserializeState(truncated, out _, out _, out _), Is.False);
        }

        [Test]
        public void State_RejectsAnEmptyPayload()
        {
            Assert.That(
                LobbyBotWireProtocol.TryDeserializeState(ReadOnlySpan<byte>.Empty, out _, out _, out _),
                Is.False);
        }

        [Test]
        public void Command_RoundTripsEveryKind()
        {
            AssertCommandRoundTrip(new LobbyBotCommand(LobbyBotCommandKind.Add));
            AssertCommandRoundTrip(new LobbyBotCommand(LobbyBotCommandKind.Remove, LobbyBotRoster.BotIdBase + 3));
            AssertCommandRoundTrip(new LobbyBotCommand(LobbyBotCommandKind.SetControl, 6, true));
            AssertCommandRoundTrip(new LobbyBotCommand(LobbyBotCommandKind.SetControl, 6, false));
        }

        [Test]
        public void Command_RejectsAnUnknownKind()
        {
            byte[] payload = LobbyBotWireProtocol.SerializeCommand(new LobbyBotCommand(LobbyBotCommandKind.Add));
            payload[1] = 200;

            Assert.That(LobbyBotWireProtocol.TryDeserializeCommand(payload, out _), Is.False);
        }

        [Test]
        public void Command_RejectsNoneWhichIsTheDefaultValue()
        {
            // default(LobbyBotCommand) 가 실수로 전송돼도 Host 가 아무 일도 하지 않게 한다.
            byte[] payload = LobbyBotWireProtocol.SerializeCommand(default);

            Assert.That(LobbyBotWireProtocol.TryDeserializeCommand(payload, out _), Is.False);
        }

        [Test]
        public void Command_CarriesNoRequesterIdentity()
        {
            // 요청자를 페이로드에 담으면 권한 없는 참가자가 방장 id 를 적어 보낼 수 있다.
            // 규약이 그 자리를 아예 갖지 않는다는 것을 크기로 고정한다 (D-ONL-AM6C7M).
            byte[] payload = LobbyBotWireProtocol.SerializeCommand(
                new LobbyBotCommand(LobbyBotCommandKind.SetControl, 6, true));

            Assert.That(payload.Length, Is.EqualTo(1 + 1 + 4 + 1),
                "버전 + 종류 + 대상 + 플래그. 보낸 사람 자리는 없다");
        }

        [Test]
        public void StateAndCommandMagicsDiffer()
        {
            // 같은 신뢰 데이터 채널을 나눠 쓰므로 겹치면 한쪽이 통째로 다른 쪽 처리로 샌다.
            Assert.That(LobbyBotWireProtocol.StateMagic, Is.Not.EqualTo(LobbyBotWireProtocol.CommandMagic));
        }

        [Test]
        public void LateJoiner_IsSentTheCurrentFullBotSnapshot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            const string relativePath = "client/BettingRoyale/Assets/BettingRoyale/Scripts/Network/NetworkManager.cs";
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, relativePath)))
                directory = directory.Parent;
            Assert.That(directory, Is.Not.Null, "저장소 루트를 찾을 수 없습니다.");
            string source = File.ReadAllText(Path.Combine(directory.FullName, relativePath));

            int joined = source.IndexOf("public void OnPlayerJoined", StringComparison.Ordinal);
            int left = source.IndexOf("public void OnPlayerLeft", joined, StringComparison.Ordinal);
            string joinHandler = source.Substring(joined, left - joined);

            Assert.That(joinHandler, Does.Contain("SendBotStateToPlayer(player);"),
                "기존 broadcast를 놓친 늦은 참가자에게 현재 전체 명단을 다시 보내야 한다.");
            Assert.That(source, Does.Contain("LobbyBotWireProtocol.SerializeState("));
            Assert.That(source, Does.Contain("runner.SendReliableDataToPlayer(player, key, payload);"));
        }

        private static void AssertCommandRoundTrip(in LobbyBotCommand source)
        {
            byte[] payload = LobbyBotWireProtocol.SerializeCommand(source);

            Assert.That(LobbyBotWireProtocol.TryDeserializeCommand(payload, out LobbyBotCommand parsed), Is.True);
            Assert.That(parsed.Kind, Is.EqualTo(source.Kind));
            Assert.That(parsed.SlotId, Is.EqualTo(source.SlotId));
            Assert.That(parsed.Flag, Is.EqualTo(source.Flag));
        }
    }
}
#endif
