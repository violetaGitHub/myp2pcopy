﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace p2pcopy
{
    static class Receiver
    {
        static internal void Run(Udt.Socket conn)
        {
            int ini = Environment.TickCount;
            int ini = Environment.TickCount;
            using (Udt.NetworkStream netStream = new Udt.NetworkStream(conn))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (BinaryReader reader = new BinaryReader(netStream))
            {
                string fileName = reader.ReadString();
                long size = reader.ReadInt64();

                byte[] buffer = new byte[4 * 1024 * 1024];

                int i = 0;

                ConsoleProgress.Draw(i++, 0, size, ini, Console.WindowWidth / 2);

                using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
                {
                    long read = 0;

                    while (read < size)
                    {
                        int toRecv = reader.ReadInt32();

                        ReadFragment(reader, toRecv, buffer);

                        fileStream.Write(buffer, 0, toRecv);

                        read += toRecv;

                        writer.Write(true);

                        ConsoleProgress.Draw(i++, read, size, ini, Console.WindowWidth / 2);
                    }
                }
            }
        }

        static int ReadFragment(BinaryReader reader, int size, byte[] buffer)
        {
            int read = 0;

            while (read < size)
            {
                read += reader.Read(buffer, read, size - read);
            }

            return read;
        }
    }
}
