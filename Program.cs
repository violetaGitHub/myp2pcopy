﻿using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Net.Cache;

namespace p2pcopy
{
    class Program
    {
        static void Main(string[] args)
        {
            CommandLineArguments cla = CommandLineArguments.Parse(args);

            if (cla == null || (!cla.Sender && !cla.Receiver))
            {
                CommandLineArguments.ShowUsage();
                return;
            }

            string remoteIp;
            int remotePort;

            Socket socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram, ProtocolType.Udp);

            try
            {

                if (cla.LocalPort != -1)
                {
                    Console.WriteLine("Using local port: {0}", cla.LocalPort);
                }
                else
                {
                    cla.LocalPort = 0;
                }

                socket.Bind(new IPEndPoint(IPAddress.Any, cla.LocalPort));

                P2pEndPoint p2pEndPoint = GetExternalEndPoint(socket);

                if (p2pEndPoint == null)
                    return;

                Console.WriteLine("Tell this to your peer: {0}", p2pEndPoint.External.ToString());

                Console.WriteLine();
                Console.WriteLine();

                Console.Write("Enter the ip:port of your peer: ");
                string peer = Console.ReadLine();

                if (string.IsNullOrEmpty(peer))
                {
                    Console.WriteLine("Invalid ip:port entered");
                    return;
                }

                // try again to connect to external to "reopen" port
                GetExternalEndPoint(socket);

                ParseRemoteAddr(peer, out remoteIp, out remotePort);

                Udt.Socket connection = PeerConnect(socket, remoteIp, remotePort);

                if (connection == null)
                {
                    Console.WriteLine("Failed to establish P2P conn to {0}", remoteIp);
                    return;
                }

                try
                {
                    if (args[0] == "sender")
                    {
                        RunSender(connection, cla.File);
                        return;
                    }

                    RunReceiver(connection);
                }
                finally
                {
                    connection.Close();
                }
            }
            finally
            {
                socket.Close();
            }
        }

        class CommandLineArguments
        {
            internal bool Sender = false;

            internal bool Receiver = false;

            internal string File;

            internal int LocalPort = -1;

            static internal CommandLineArguments Parse(string[] args)
            {
                CommandLineArguments result = new CommandLineArguments();

                int i = 0;

                while (i < args.Length)
                {
                    string arg = args[i++];

                    switch (arg)
                    {
                        case "sender":
                            result.Sender = true;
                            break;
                        case "receiver":
                            result.Receiver = true;
                            break;
                        case "--file":
                            if (args.Length == i) return null;
                            result.File = args[i++];
                            break;
                        case "--localport":
                            if (args.Length == i) return null;
                            result.LocalPort = int.Parse(args[i++]);
                            break;
                        case "help":
                            return null;
                    }
                }

                return result;
            }

            static internal void ShowUsage()
            {
                Console.WriteLine("p2pcopy [sender --file file_to_send |receiver]");
            }
        }

        static void RunSender(Udt.Socket conn, string file)
        {
            int ini = Environment.TickCount;

            using (Udt.NetworkStream netStream = new Udt.NetworkStream(conn))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (BinaryReader reader = new BinaryReader(netStream))
            using (FileStream fileReader = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
                long fileSize = new FileInfo(file).Length;

                writer.Write(Path.GetFileName(file));
                writer.Write(fileSize);

                byte[] buffer = new byte[65 * 1024];

                long pos = 0;

                int i = 0;

                DrawProgress(i++, pos, fileSize, ini, Console.WindowWidth / 2);

                while (pos < fileSize)
                {
                    int toSend = buffer.Length < (fileSize - pos)
                        ? buffer.Length
                        : (int) (fileSize - pos);

                    fileReader.Read(buffer, 0, toSend);

                    writer.Write(toSend);
                    conn.Send(buffer, 0, toSend);
                    //writer.Write(buffer, 0, toSend);

                    if (!reader.ReadBoolean())
                    {
                        Console.WriteLine("Error in transmission");
                        return;
                    }

                    pos += toSend;

                    DrawProgress(i++, pos, fileSize, ini, Console.WindowWidth / 2);

                    Console.WriteLine("BandwidthMbps {0} mbps.", conn.GetPerformanceInfo().Probe.BandwidthMbps);
                    Console.WriteLine("RoundtripTime {0}.", conn.GetPerformanceInfo().Probe.RoundtripTime);
                    Console.WriteLine("SendMbps {0}.", conn.GetPerformanceInfo().Local.SendMbps);
                    Console.WriteLine("ReceiveMbps {0}.", conn.GetPerformanceInfo().Local.ReceiveMbps);


                    var local = conn.GetPerformanceInfo().Local;
                    var total = conn.GetPerformanceInfo().Total;
                    var probe = conn.GetPerformanceInfo().Probe;
                }
            }
        }

        static void RunReceiver(Udt.Socket conn)
        {
            int ini = Environment.TickCount;

            using (Udt.NetworkStream netStream = new Udt.NetworkStream(conn))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (BinaryReader reader = new BinaryReader(netStream))
            {
                string fileName = reader.ReadString();
                long size = reader.ReadInt64();

                byte[] buffer = new byte[4 * 1024 * 1024];

                int i = 0;

                DrawProgress(i++, 0, size, ini, Console.WindowWidth / 2);

                using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
                {
                    long read = 0;

                    while (read < size)
                    {
                        int toRecv = reader.ReadInt32();

                        ReadFragment(conn, toRecv, buffer);

                        fileStream.Write(buffer, 0, toRecv);

                        read += toRecv;

                        writer.Write(true);

                        DrawProgress(i++, read, size, ini, Console.WindowWidth / 2);
                    }
                }
            }
        }

        static int ReadFragment(Udt.Socket socket, int size, byte[] buffer)
        {
            int read = 0;

            while (read < size)
            {
                read += socket.Receive(buffer, read, size -read);
            }

            return read;
        }

        static void DrawProgress(
            int i,
            long transferred,
            long total,
            int transferStarted,
            int width)
        {
            Console.Write("\r");

            char[] progress = new char[] { '-', '\\', '|', '/' };

            Console.Write(progress[i % 4]);

            int fillPos = (int)((float)transferred / (float)total * width);
            string filled = new string('#', fillPos);
            string empty = new string('-', width - fillPos);
            Console.Write("[" + filled + empty + "] ");

            Console.Write("{0, 22}. ",
                SizeConverter.ConvertToSizeString(transferred) + " / " +
                SizeConverter.ConvertToSizeString(total));

            int seconds = (Environment.TickCount - transferStarted) / 1000;

            if (seconds == 0)
            {
                return;
            }

            Console.Write("{0, 10}/s",
                SizeConverter.ConvertToSizeString(transferred / seconds));
        }

        static void ParseRemoteAddr(string addr, out string remoteIp, out int port)
        {
            string[] split = addr.Split(':');

            remoteIp = split[0];
            port = int.Parse(split[1]);
        }

        class P2pEndPoint
        {
            internal IPEndPoint External;
            internal IPEndPoint Internal;
        }

        static P2pEndPoint GetExternalEndPoint(Socket socket)
        {
            // https://gist.github.com/zziuni/3741933

            StunResult externalEndPoint = StunClient.Query("stun.l.google.com", 19302, socket);

            if (externalEndPoint.NetType == StunNetType.UdpBlocked)
            {
                Console.WriteLine("Your external IP can't be obtained. You are blocked :-(");
                return null;
            }

            Console.WriteLine("Your firewall is {0}", externalEndPoint.NetType.ToString());

            return new P2pEndPoint()
            {
                External = externalEndPoint.PublicEndPoint,
                Internal = (socket.LocalEndPoint as IPEndPoint)
            };
        }

        static int SleepTime(DateTime now)
        {
            List<int> seconds = new List<int>() {10, 20, 30, 40, 50, 60};

            int next = seconds.Find(x => x > now.Second);

            return next - now.Second;
        }

        static Udt.Socket PeerConnect(Socket socket, string remoteAddr, int remotePort)
        {
            bool bConnected = false;
            int retry = 0;

            Udt.Socket client = null;

            while (!bConnected)
            {
                try
                {
                    DateTime now = GetNistTime();

                    int sleepTimeToSync = SleepTime(now);

                    Console.WriteLine("[{0}] - Waiting {1} sec to sync with other peer",
                        now.ToLongTimeString(),
                        sleepTimeToSync);
                    System.Threading.Thread.Sleep(sleepTimeToSync * 1000);

                    GetExternalEndPoint(socket);

                    if (client != null)
                        client.Close();

                    client = new Udt.Socket(AddressFamily.InterNetwork, SocketType.Stream);

                    client.SetSocketOption(Udt.SocketOptionName.Rendezvous, true);

                    client.Bind(socket);

                    Console.Write("\r{0} - Trying to connect to {1}:{2}.  ",
                        retry++, remoteAddr, remotePort);

                    client.Connect(remoteAddr, remotePort);

                    Console.WriteLine("Connected successfully to {0}:{1}",
                        remoteAddr, remotePort);

                    bConnected = true;
                }
                catch (Exception e)
                {
                    Console.Write(e.Message.Replace(Environment.NewLine, ". "));
                }
            }

            return client;
        }


        static DateTime GetNistTime()
        {
            // http://stackoverflow.com/questions/6435099/how-to-get-datetime-from-the-internet

            DateTime dateTime = DateTime.MinValue;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://nist.time.gov/actualtime.cgi?lzbc=siqm9b");
            request.Method = "GET";
            request.Accept = "text/html, application/xhtml+xml, */*";
            request.UserAgent = "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.1; Trident/6.0)";
            request.ContentType = "application/x-www-form-urlencoded";
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore); //No caching
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                StreamReader stream = new StreamReader(response.GetResponseStream());
                string html = stream.ReadToEnd();//<timestamp time=\"1395772696469995\" delay=\"1395772696469995\"/>
                string time = Regex.Match(html, @"(?<=\btime="")[^""]*").Value;
                double milliseconds = Convert.ToInt64(time) / 1000.0;
                dateTime = new DateTime(1970, 1, 1).AddMilliseconds(milliseconds).ToLocalTime();
            }

            return dateTime;
        }
    }
}
