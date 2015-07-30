using MiscUtil.Conversion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AgarBot.PackageManagers
{
    public class DefaultPacketManager
    {
        private object this_lock;

        private bool hasStarted;

        private readonly int recv_size = 1024;
        private readonly int send_size = 1024;

        private ClientWebSocket websock;

        private object in_packets_l;
        private Queue<AgarPacket> in_packets;
        private AutoResetEvent packet_received;

        private object out_packets_l;
        private Queue<AgarPacket> out_packets;
        private AutoResetEvent packet_sent;

        private Task stream_sender;
        private Task stream_receiver;

        private CancellationTokenSource send_cancel;
        private CancellationTokenSource recv_cancel;

        private LittleEndianBitConverter bc;

        public delegate void PacketReceivedEventHandler(DefaultPacketManager manager, AgarPacket packet);
        public event PacketReceivedEventHandler PacketReceived;

        public delegate void ConnectionClosedEventHandler(DefaultPacketManager manager, string reason);
        public event ConnectionClosedEventHandler ConnectionClosed;

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
                return hasStarted && !send_cancel.IsCancellationRequested && !recv_cancel.IsCancellationRequested;
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


        public DefaultPacketManager()
        {
            this_lock = new object();

            websock = new ClientWebSocket();

            bc = new LittleEndianBitConverter();

            in_packets_l = new object();
            in_packets = new Queue<AgarPacket>();
            packet_received = new AutoResetEvent(false);

            out_packets_l = new object();
            out_packets = new Queue<AgarPacket>();
            packet_sent = new AutoResetEvent(false);

            hasStarted = false;
        }


        public async Task Start()
        {
            lock (this_lock)
                if (hasStarted)
                    throw new InvalidOperationException("The packet manager cannot be started a second time.");
                else
                    hasStarted = true;

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
                // sw.Write("154669603") // Version number, should be dynamically retrieved
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
            send_cancel = new CancellationTokenSource();
            recv_cancel = new CancellationTokenSource();

            stream_sender = Task.Run(SendLoop, send_cancel.Token);
            stream_receiver = Task.Run(ReceiveLoop, recv_cancel.Token);
        }

        public async Task Stop()
        {
            // No problem stopping the task several times, no ill side-effects,
            // so we don't have to check for previous calls to Stop()

            // Wrap exceptions in an AggregateException
            List<Exception> exs = new List<Exception>();
            try
            {
                send_cancel.Cancel();
            }
            catch (AggregateException a1)
            {
                exs.AddRange(a1.InnerExceptions);
            }
            try
            {
                recv_cancel.Cancel();
            }
            catch (AggregateException a2)
            {
                exs.AddRange(a2.InnerExceptions);
            }

            try
            {
                await Task.WhenAll(stream_sender, stream_receiver).ConfigureAwait(false);
            }
            catch (OperationCanceledException) // Don't bother the user with this *expected* exception
            {
            }
            catch (Exception ex)
            {
                exs.Add(ex);
            }

            // Throw exceptions if there were any
            if (exs.Count > 0)
            {
                AggregateException ae = new AggregateException(exs);
                throw ae;
            }
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


        private async Task SendLoop()
        {
            CancellationToken cancel = send_cancel.Token;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                Monitor.Enter(out_packets_l);
                if (out_packets.Count == 0)
                {
                    Monitor.Exit(out_packets_l);

                    cancel.ThrowIfCancellationRequested();

                    packet_sent.WaitOne(125); // Wait for packets to arrive, with a timeout so that we can exit the loop
                }
                else
                {
                    List<AgarPacket> packets = new List<AgarPacket>(out_packets); // Make a copy of the packets
                    out_packets.Clear();

                    Monitor.Exit(out_packets_l);

                    foreach (AgarPacket p in packets)
                    {
                        cancel.ThrowIfCancellationRequested();

                        byte[] buff = p.ToByteArray();
                        await SendMessage(buff).ConfigureAwait(false);        // Watch out - can't await inside lock 
                                                                              // (that's why we make a copy of the list)
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

                // We shouldn't cancel this, always send the whole packet
                await websock.SendAsync(new ArraySegment<byte>(buff, written, len), WebSocketMessageType.Binary, eo, CancellationToken.None).ConfigureAwait(false);

                written += len;
            }
        }

        private async Task ReceiveLoop()
        {
            CancellationToken cancel = recv_cancel.Token;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                try
                {
                    byte[] message = await ReceiveMessage(cancel).ConfigureAwait(false);
                    AgarPacket packet = AgarPacket.FromByteArray(message);

                    lock (in_packets_l)
                    {
                        in_packets.Enqueue(packet);
                    }

                    OnPacketReceived(packet);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }

        private async Task<byte[]> ReceiveMessage(CancellationToken cancel)
        {
            List<byte[]> data = new List<byte[]>();

            WebSocketReceiveResult res = null;
            do
            {
                cancel.ThrowIfCancellationRequested();

                byte[] buff = new byte[recv_size];
                ArraySegment<byte> segment = new ArraySegment<byte>(buff);

                try
                {
                    res = await websock.ReceiveAsync(segment, cancel).ConfigureAwait(false);

                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        // No cancelling on this one
                        await websock.CloseAsync((WebSocketCloseStatus)res.CloseStatus, "Server closed connection. Message: " + res.CloseStatusDescription, CancellationToken.None).ConfigureAwait(false);
                        OnSocketClosed();
                        throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely); // TODO This might not be the correct exception to raise...
                    }
                    else
                    {
                        Array.Resize(ref buff, res.Count);
                        if (res.Count > 0)
                            data.Add(buff);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Expecting this one if the operation is cancelled
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


        private void OnSocketClosed()
        {
            // Might have a deadlock if we use Stop()
            recv_cancel.Cancel(); // If any errors are thrown, they will be stored in the task, 
            send_cancel.Cancel(); // and thrown when someone calls Stop()

            if (ConnectionClosed != null)
                ConnectionClosed(this, websock.CloseStatus.ToString() + ": " + websock.CloseStatusDescription);
        }

        private void OnPacketReceived(AgarPacket packet)
        {
            packet_received.Set(); // Signal any waiting threads

            if (PacketReceived != null)
                PacketReceived(this, packet);
        }
    }
}