using AgarBot.PackageManagers;
using MiscUtil.Conversion;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace AgarBot
{
    public class AgarClient
    {
        private bool stillalive;
        private Thread mainloop;
        private AutoResetEvent wait_useraction;

        private LittleEndianBitConverter bc;

        private object userActions_l;
        private Queue<AgarPacket> userActions;

        private DefaultPacketManager pacman;
        public DefaultPacketManager PacketManager
        {
            get
            {
                return pacman;
            }
        }

        private AgarWorld world;
        public AgarWorld World
        {
            get
            {
                return world;
            }
        }

        public AgarClient(DefaultPacketManager pm)
        {
            world = new AgarWorld();
            bc = new LittleEndianBitConverter();
            wait_useraction = new AutoResetEvent(false);

            pacman = pm;

            userActions_l = new object();
            userActions = new Queue<AgarPacket>();
        }


        public void StartProcessingLoop()
        {
            stillalive = true;
            mainloop = new Thread(main);
            mainloop.Start();
        }

        public void SendInitializationPackets()
        {
            AgarPacket init1 = new AgarPacket();
            init1.OpCode = (byte)ClientPacketType.Init1;
            init1.Payload = bc.GetBytes(5U);

            pacman.SendPacket(init1);

            AgarPacket init2 = new AgarPacket();
            init2.OpCode = (byte)ClientPacketType.Init2;
            init2.Payload = bc.GetBytes(pacman.AgarServerVersion);

            pacman.SendPacket(init2);

            AgarPacket conntoken = new AgarPacket();
            conntoken.OpCode = (byte)ClientPacketType.ConnectionToken;
            conntoken.Payload = Encoding.UTF8.GetBytes(pacman.ConnectionToken); // Lets hope the encoding is right

            pacman.SendPacket(conntoken);
        }

        public void StopProcessingLoop()
        {
            stillalive = false;
            mainloop.Join();
        }


        private void main()
        {
            while (stillalive)
            {
                WaitHandle[] events = new WaitHandle[] { wait_useraction, pacman.WaitPacketReceived };
                int which = WaitHandle.WaitAny(events, 125); // Timeout so that we can stop the loop

                if (which == WaitHandle.WaitTimeout)
                {
                    // Timed out
                }
                else if (which == 0)
                {
                    lock (userActions_l)
                    {
                        while (userActions.Count > 0)
                        {
                            AgarPacket a = userActions.Dequeue();
                            pacman.SendPacket(a);
                        }
                    }
                }
                else if (which == 1)
                {
                    while (pacman.Available > 0)
                    {
                        AgarPacket p = pacman.DequeuePacket();
                        DispatchPacket(p);
                    }
                }
            }
        }
        
        private void DispatchPacket(AgarPacket p)
        {
            Console.WriteLine("Received {0} packet with {1} bytes of data.", (ServerPacketType)p.OpCode, p.Payload.Length);

            switch ((ServerPacketType)p.OpCode)
            {
                case ServerPacketType.GameAreaSize:
                    // TODO Assert PayloadLength == 32
                    double min_x = bc.ToDouble(p.Payload, 0);
                    double min_y = bc.ToDouble(p.Payload, 8);
                    double max_x = bc.ToDouble(p.Payload, 16);
                    double max_y = bc.ToDouble(p.Payload, 24);
                    world.SetBounds(min_x, min_y, max_x, max_y);
                    break;

                case ServerPacketType.WorldUpdate:
                    // Read eat events
                    uint count = bc.ToUInt16(p.Payload, 0);
                    EatEvent[] eats = new EatEvent[count];
                    for (int i = 0; i < count; i++)
                    {
                        eats[i] = new EatEvent()
                        {
                            eater_id = bc.ToUInt32(p.Payload, i * 8),
                            victim_id = bc.ToUInt32(p.Payload, i * 8 + 4)
                        };
                    }

                    // Read cell updates (names, size, position)
                    List<UpdateEvent> updates = new List<UpdateEvent>();
                    UpdateEvent current;
                    int offset = 0;

                    while (true)
                    {
                        current.player_id = bc.ToUInt32(p.Payload, offset);
                        if (current.player_id == 0)
                            break;

                        current.x = bc.ToUInt32(p.Payload, offset + 4);
                        current.y = bc.ToUInt32(p.Payload, offset + 8);

                        current.radius = bc.ToUInt16(p.Payload, offset + 10);
                        current.red = p.Payload[offset + 11];
                        current.green = p.Payload[offset + 12];
                        current.blue = p.Payload[offset + 13];
                        current.flags = p.Payload[offset + 14];

                        if ((current.flags & 0x02) != 0)
                            offset += 4; // Unsure about this...

                        current.skin_url = null; // Just to fully initialize the struct

                        int bytesread = 0;
                        if ((current.flags & 0x04) != 0)
                            current.skin_url = bc.ReadNullStr8(p.Payload, offset + 15, out bytesread);
                        offset += bytesread;

                        current.name = bc.ReadNullStr16(p.Payload, offset + 15, out bytesread);
                        offset += bytesread;

                        updates.Add(current);
                    }

                    // Read cell removals (out of sight, disconnect, etc)
                    count = bc.ToUInt32(p.Payload, offset + 15);
                    uint[] removals = new uint[count];
                    for (int i = 0; i < count; i++)
                        removals[i] = bc.ToUInt32(p.Payload, offset + 17 + i * 4);

                    world.RegisterEats(eats);
                    world.RegisterUpdates(updates);
                    world.RegisterRemovals(removals);

                    break;
            }
        }
    }
}
