using MiscUtil.Conversion;
using System;

namespace AgarBot
{
    internal struct AgarPacket
    {
        byte opcode;
        byte[] data;

        public byte OpCode
        {
            get { return opcode; }
            set { opcode = value; }
        }

        public byte[] Payload
        {
            get { return data; }
            set { data = value; }
        }

        internal AgarPacket(byte opcode, byte[] payload)
        {
            this.opcode = opcode;
            this.data = payload;
        }

        internal byte[] ToByteArray()
        {
            byte[] d = new byte[1 + data.Length];
            d[0] = opcode;
            Array.Copy(data, 0, d, 1, data.Length);
            return d;
        }

        internal static AgarPacket FromByteArray(byte[] message)
        {
            if (message == null || message.Length == 0)
                throw new ArgumentException();

            if (message.Length == 1)
                return new AgarPacket(message[0], new byte[] { });

            byte[] payload = new byte[message.Length - 1];
            Array.Copy(message, 1, payload, 0, payload.Length);

            return new AgarPacket(message[0], payload);
        }
    }

    internal enum ClientPacketType : byte
    {
        SendNicknameAndSpawn = 0,
        StartSpectating = 1,
        SetDirection = 16,
        Split = 17,
        Q = 18,
        AFK = 19,
        Explode = 20,
        EjectMass = 21,
        ConnectionToken = 80,
        LoginIdentifier = 81,
        Init1 = 254,
        Init2 = 255,
    }

    internal enum ServerPacketType :byte
    {
        WorldUpdate = 16,
        ViewUpdate = 17,
        Reset = 20,
        DrawDebugLine = 21,
        OwnsBlob= 32,
        FFALeaderboard = 49,
        TeamLeaderboard = 50,
        GameAreaSize=64,
        HelloHelloHello = (byte)'H',
        MessageLength = 240,
    }
}