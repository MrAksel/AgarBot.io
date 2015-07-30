using MiscUtil.Conversion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        private AgarPacketManager pacman;
        public AgarPacketManager PacketManager
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

        public AgarClient(AgarPacketManager pm)
        {
            world = new AgarWorld();
            bc = new LittleEndianBitConverter();
            wait_useraction = new AutoResetEvent(false);

            pacman = pm;

            userActions_l = new object();
            userActions = new Queue<AgarPacket>();

            mainloop = new Thread(main);

            stillalive = true;
            mainloop.Start();
        }

        public void StartInitialization()
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

        private void main()
        {
            while (stillalive)
            {
                WaitHandle[] events = new WaitHandle[] { wait_useraction, pacman.WaitPacketReceived };
                int which = WaitHandle.WaitAny(events); // No timeout

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
                        ProcessPacket(p);
                    }
                }
            }
        }

        private void ProcessPacket(AgarPacket p)
        {
            Console.WriteLine("Received {0} packet with {1} bytes of data.", (ServerPacketType)p.OpCode, p.Payload.Length);
        }
    }
}
