using MiscUtil.Conversion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AgarBot
{
    public class AgarPacketManager
    {
        private readonly int recv_size = 1024;
        private readonly int send_size = 1024;

        private bool send_running;
        private bool recv_running;

        private ClientWebSocket websock;

        private object in_packets_l;
        private Queue<AgarPacket> in_packets;
        private AutoResetEvent packet_received;

        private object out_packets_l;
        private Queue<AgarPacket> out_packets;
        private AutoResetEvent packet_sent;

        private Thread stream_sender;
        private Thread stream_receiver;

        private LittleEndianBitConverter bc;

        public string AgarServerString
        {
            get
            {
                return "http://m.agar.io/";
            }
        }

        public uint AgarServerVersion
        {
            get
            {
                return 154669603U;
            }
        }

        public bool Alive
        {
            get
            {
                return send_running & recv_running;
            }
        }

        public int Available
        {
            get
            {
                lock (in_packets_l)
                {
                    return in_packets.Count;
                }
            }
        }

        public string ConnectionToken
        {
            get;
            private set;
        }

        public int OutPending
        {
            get
            {
                lock (out_packets_l)
                {
                    return out_packets.Count;
                }
            }
        }

        public WaitHandle WaitPacketReceived
        {
            get
            {
                return packet_received;
            }
        }


        public AgarPacketManager()
        {
            websock = new ClientWebSocket();

            bc = new LittleEndianBitConverter();

            in_packets_l = new object();
            in_packets = new Queue<AgarPacket>();
            packet_received = new AutoResetEvent(false);

            out_packets_l = new object();
            out_packets = new Queue<AgarPacket>();
            packet_sent = new AutoResetEvent(false);

            stream_sender = new Thread(SendLoop);
            stream_receiver = new Thread(ReceiveLoop);
        }

        public async Task Start()
        {
            IPEndPoint ep;
            string epstring;
            Uri agaruri = new Uri(AgarServerString);

            // Request a unique identifier (the connection token)
            HttpWebRequest hwr = (HttpWebRequest)WebRequest.Create(agaruri);
            hwr.Method = "POST";

            using (StreamWriter sw = new StreamWriter(hwr.GetRequestStream()))
            {
                // sw.NewLine = "\n";
                // sw.Write("EU-London") // Region code
                // sw.Write("154669603") // Version number, needs to be dynamically retrieved
            }

            WebResponse res = hwr.GetResponse();
            using (StreamReader sr = new StreamReader(res.GetResponseStream()))
            {
                epstring = sr.ReadLine();
                string[] host = epstring.Split(':');
                if (host.Length != 2)
                    throw new InvalidOperationException();

                ep = new IPEndPoint(IPAddress.Parse(host[0]),
                                    int.Parse(host[1]));

                ConnectionToken = sr.ReadLine();
            }

            websock.Options.SetRequestHeader("Origin", "http://agar.io"); // Set header, server blocks if not

            // Connect
            epstring = "ws://" + epstring;
            await websock.ConnectAsync(new Uri(epstring), CancellationToken.None).ConfigureAwait(false);

            // Start threads
            send_running = true;
            recv_running = true;

            stream_sender.Start();
            stream_receiver.Start();
        }


        internal void SendPacket(AgarPacket a)
        {
            lock (out_packets_l)
            {
                out_packets.Enqueue(a);
            }
            packet_sent.Set();
        }

        internal AgarPacket DequeuePacket()
        {
            lock (in_packets_l)
            {
                if (in_packets.Count == 0)
                    throw new InvalidOperationException("No items in queue");

                return in_packets.Dequeue();
            }
        }


        private void SendLoop()
        {
            while (send_running)
            {
                packet_sent.WaitOne();
                lock (out_packets_l)
                {
                    while (out_packets.Count > 0)
                    {
                        AgarPacket p = out_packets.Dequeue();
                        byte[] buff = p.ToByteArray();

                        SendMessage(buff).Wait();
                    }
                }
            }
        }

        private async Task SendMessage(byte[] buff)
        {
            int written = 0;
            while (written < buff.Length)
            {
                int len = Math.Min(buff.Length - written, send_size);
                bool eo = (written + len == buff.Length);

                await websock.SendAsync(new ArraySegment<byte>(buff, written, len), WebSocketMessageType.Binary, eo, CancellationToken.None);

                written += len;
            }
        }

        private void ReceiveLoop()
        {
            while (recv_running)
            {
                byte[] message = ReceiveMessage().Result;
                AgarPacket packet = AgarPacket.FromByteArray(message);

                lock (in_packets_l)
                {
                    in_packets.Enqueue(packet);
                }

                // Signal that a packet arrived
                packet_received.Set();
            }
        }

        private async Task<byte[]> ReceiveMessage()
        {
            List<byte[]> data = new List<byte[]>();

            WebSocketReceiveResult res;
            do
            {
                byte[] buff = new byte[recv_size];
                ArraySegment<byte> segment = new ArraySegment<byte>(buff);

                res = await websock.ReceiveAsync(segment, CancellationToken.None).ConfigureAwait(false);

                if (res.MessageType == WebSocketMessageType.Close)
                {
                    await websock.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server closed connection", CancellationToken.None).ConfigureAwait(false);
                    recv_running = false;
                }
                else
                {
                    Array.Resize(ref buff, res.Count);
                    if (res.Count > 0)
                        data.Add(buff);
                }

            } while (!res.EndOfMessage);

            int totalLength = data.Sum(b => b.Length);
            byte[] message = new byte[totalLength];
            int offset = 0;

            foreach (byte[] bunch in data)
            {
                Array.Copy(bunch, 0, message, offset, bunch.Length);
                offset += bunch.Length;
            }

            return message;
        }
    }
}