using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BettingRoyale.Data.Online.Lobby;

namespace BettingRoyale.Network
{
    /// <summary>봇 조작 요청의 종류. 와이어에 실리는 값이므로 번호를 바꾸지 않는다.</summary>
    public enum LobbyBotCommandKind : byte
    {
        None = 0,

        /// <summary>봇 한 칸 추가.</summary>
        Add = 1,

        /// <summary>봇 한 칸 제거. <see cref="LobbyBotCommand.SlotId"/> 가 0 이면 가장 나중에 추가된 봇.</summary>
        Remove = 2,

        /// <summary>봇 관리 권한 위임/회수. 방장만 보낼 수 있고 Host 가 다시 확인한다.</summary>
        SetControl = 3,
    }

    /// <summary>
    /// Client → Host 봇 조작 요청 (D-ONL-AM6C7M).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>요청자 식별자를 담지 않는다.</b> 담으면 권한 없는 참가자가 방장의 id 를 적어 보낼 수 있다.
    /// 보낸 사람은 <c>OnReliableDataReceived</c> 의 <c>PlayerRef</c> 로만 판정한다 —
    /// 경제 명령(<c>EconomyCommandReceived</c>)이 쓰는 것과 같은 규칙이다.
    /// </remarks>
    public readonly struct LobbyBotCommand
    {
        public LobbyBotCommand(LobbyBotCommandKind kind, int slotId = 0, bool flag = false)
        {
            Kind = kind;
            SlotId = slotId;
            Flag = flag;
        }

        public LobbyBotCommandKind Kind { get; }

        /// <summary><see cref="LobbyBotCommandKind.Remove"/> 는 봇 식별자, <see cref="LobbyBotCommandKind.SetControl"/> 은 대상 참가자.</summary>
        public int SlotId { get; }

        /// <summary><see cref="LobbyBotCommandKind.SetControl"/> 의 부여 여부.</summary>
        public bool Flag { get; }

        public override string ToString()
        {
            return string.Concat(
                "LobbyBotCommand(Kind=", Kind.ToString(),
                ", SlotId=", SlotId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ", Flag=", Flag ? "true" : "false",
                ")");
        }
    }

    /// <summary>
    /// 대기방 봇 로스터를 신뢰 데이터 채널로 나르는 최소 규약 (D-ONL-AM6C7M · S-ONL-9GXTYQ).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RoomSettingsProtocol</c>·<c>OnlineCoreLoopProtocol</c> 과 같은 형태다 — 버전 바이트를 앞에 두고,
    /// 받는 쪽은 버전이 다르면 해석하지 않고 버린다. 구버전 피어가 절반만 읽고 엉뚱한 로스터로
    /// 진행하는 것보다 낫다.
    /// </para>
    /// <para>
    /// ⚠️ <b>봇은 <c>[Networked]</c> 로 복제하지 않는다.</b> 봇에는 <c>PlayerRef</c> 가 없어
    /// <c>PlayerInfo</c> 를 쓸 수 없고, 슬롯 수가 가변이라 고정 크기 복제와 맞지 않는다.
    /// 근거 전체는 D-ONL-AM6C7M 을 본다.
    /// </para>
    /// </remarks>
    public static class LobbyBotWireProtocol
    {
        /// <summary>Host → Client 로스터 스냅샷. 같은 채널을 나눠 쓰므로 다른 매직과 달라야 한다.</summary>
        public const int StateMagic = 0x42524F53;

        /// <summary>Client → Host 조작 요청.</summary>
        public const int CommandMagic = 0x42434D44;

        private const byte Version = 1;

        /// <summary>
        /// 슬롯·권한자 수의 와이어 상한. 정원 상한이 8명이므로(S-GD-EZ682A) 넉넉히 잡되
        /// <b>무한히 받지는 않는다</b> — 길이 바이트 하나가 망가지면 그 뒤를 전부 읽으려 든다.
        /// </summary>
        private const int MaxEntries = 32;

        private const int MaxStatePayloadBytes = 512;
        private const int MaxCommandPayloadBytes = 32;

        public static byte[] SerializeState(int hostPlayerId, IReadOnlyList<LobbyBotSlot> slots, IReadOnlyList<int> grants)
        {
            using var stream = new MemoryStream(64);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            int slotCount = slots == null ? 0 : Math.Min(slots.Count, MaxEntries);
            int grantCount = grants == null ? 0 : Math.Min(grants.Count, MaxEntries);

            writer.Write(Version);
            writer.Write(hostPlayerId);
            writer.Write((byte)slotCount);
            for (var i = 0; i < slotCount; i++)
            {
                LobbyBotSlot slot = slots[i];
                writer.Write((byte)Math.Clamp(slot.Number, 0, byte.MaxValue));
                writer.Write(slot.AddedByPlayerId);
                writer.Write(slot.Sequence);
            }

            writer.Write((byte)grantCount);
            for (var i = 0; i < grantCount; i++)
            {
                writer.Write(grants[i]);
            }

            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// 로스터 스냅샷을 읽는다. 버전·길이가 맞지 않으면 <c>false</c> 이고 출력은 건드리지 않는다.
        /// </summary>
        public static bool TryDeserializeState(
            ReadOnlySpan<byte> payload,
            out int hostPlayerId,
            out List<LobbyBotSlot> slots,
            out List<int> grants)
        {
            hostPlayerId = 0;
            slots = null;
            grants = null;

            if (payload.Length <= 0 || payload.Length > MaxStatePayloadBytes)
            {
                return false;
            }

            try
            {
                using var stream = new MemoryStream(payload.ToArray(), writable: false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

                if (reader.ReadByte() != Version)
                {
                    return false;
                }

                int host = reader.ReadInt32();
                int slotCount = reader.ReadByte();
                if (slotCount > MaxEntries)
                {
                    return false;
                }

                var readSlots = new List<LobbyBotSlot>(slotCount);
                for (var i = 0; i < slotCount; i++)
                {
                    int number = reader.ReadByte();
                    int addedBy = reader.ReadInt32();
                    int sequence = reader.ReadInt32();
                    readSlots.Add(new LobbyBotSlot(number, addedBy, sequence));
                }

                int grantCount = reader.ReadByte();
                if (grantCount > MaxEntries)
                {
                    return false;
                }

                var readGrants = new List<int>(grantCount);
                for (var i = 0; i < grantCount; i++)
                {
                    readGrants.Add(reader.ReadInt32());
                }

                hostPlayerId = host;
                slots = readSlots;
                grants = readGrants;
                return true;
            }
            catch (Exception)
            {
                // 잘린 페이로드는 EndOfStreamException 으로 온다. 부분 해석 결과를 내보내지 않는다.
                return false;
            }
        }

        public static byte[] SerializeCommand(in LobbyBotCommand command)
        {
            using var stream = new MemoryStream(16);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(Version);
            writer.Write((byte)command.Kind);
            writer.Write(command.SlotId);
            writer.Write(command.Flag);

            writer.Flush();
            return stream.ToArray();
        }

        public static bool TryDeserializeCommand(ReadOnlySpan<byte> payload, out LobbyBotCommand command)
        {
            command = default;

            if (payload.Length <= 0 || payload.Length > MaxCommandPayloadBytes)
            {
                return false;
            }

            try
            {
                using var stream = new MemoryStream(payload.ToArray(), writable: false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

                if (reader.ReadByte() != Version)
                {
                    return false;
                }

                var kind = (LobbyBotCommandKind)reader.ReadByte();
                if (kind != LobbyBotCommandKind.Add
                    && kind != LobbyBotCommandKind.Remove
                    && kind != LobbyBotCommandKind.SetControl)
                {
                    return false;
                }

                int slotId = reader.ReadInt32();
                bool flag = reader.ReadBoolean();

                command = new LobbyBotCommand(kind, slotId, flag);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
