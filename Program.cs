using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SerialReceive
{
    internal class Program
    {
        // Access rights (dwDesiredAccess)
        [Flags]
        public enum FileAccess : uint
        {
            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000,
            GENERIC_EXECUTE = 0x20000000,
            GENERIC_ALL = 0x10000000
        }

        // Share modes (dwShareMode)
        [Flags]
        public enum FileShare : uint
        {
            NONE = 0x00000000,
            READ = 0x00000001,
            WRITE = 0x00000002,
            DELETE = 0x00000004
        }

        // Creation dispositions (dwCreationDisposition)
        public enum CreationDisposition : uint
        {
            CREATE_NEW = 1,
            CREATE_ALWAYS = 2,
            OPEN_EXISTING = 3,
            OPEN_ALWAYS = 4,
            TRUNCATE_EXISTING = 5
        }

        // Flags & attributes (dwFlagsAndAttributes)
        [Flags]
        public enum FileFlagsAndAttributes : uint
        {
            FILE_ATTRIBUTE_READONLY = 0x00000001,
            FILE_ATTRIBUTE_HIDDEN = 0x00000002,
            FILE_ATTRIBUTE_NORMAL = 0x00000080,
            // … the rest probably aren't needed
            FILE_FLAG_OVERLAPPED = 0x40000000,
            FILE_FLAG_NO_BUFFERING = 0x20000000,
            FILE_FLAG_RANDOM_ACCESS = 0x10000000,
            FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000,
            FILE_FLAG_WRITE_THROUGH = 0x80000000
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern SafeFileHandle CreateFile(
            string lpFileName,
            FileAccess dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            CreationDisposition dwCreationDisposition,
            FileFlagsAndAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped
        );

        [Flags]
        public enum ExtendedFunctions : uint
        {
            SETXOFF = 1,
            SETXON = 2,
            SETRTS = 3,
            SETDTR = 5,
            CLRRTS = 4,
            CLRDTR = 6,
            SETBREAK = 8,
            CLRBREAK = 9,
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool EscapeCommFunction(SafeFileHandle hFile, ExtendedFunctions dwFunc);

        static void Main(string[] args)
        {
            string portName = args.Length > 0 ? args[0] : "COM12";
            using SafeFileHandle hPort = CreateFile(
                @"\\.\" + portName,
                FileAccess.GENERIC_READ,
                FileShare.READ,
                IntPtr.Zero,
                CreationDisposition.OPEN_EXISTING,
                0,
                IntPtr.Zero
            );
            if (hPort.IsInvalid)
            {
                throw new IOException($"Failed to open {portName}", Marshal.GetLastWin32Error());
            }

            if (!EscapeCommFunction(hPort, ExtendedFunctions.SETDTR))
            {
                throw new IOException("EscapeCommFunction(SETDTR) failed", Marshal.GetLastWin32Error());
            }

            var buffer = new byte[64 * 1024];
            long total = 0;
            var sw = Stopwatch.StartNew();
            //List<double> sampled_speeds = [];
            //int count = 0;
            while (true)
            {
                if (!ReadFile(hPort, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
                {
                    throw new IOException("ReadFile failed", Marshal.GetLastWin32Error());
                }

                total += read;
                double secs = sw.Elapsed.TotalSeconds;
                double kibps = (total / 1024.0) / secs;
                double mibps = kibps / 1024.0;
                Console.Write($"\rRead: {total / 1024:N0} KiB, {kibps:F0} KiB/s, {mibps:F3} MiB/s");
                //if (count % 10 == 0)
                //{
                //    sampled_speeds.Add(mbps);
                //}
                //count++;
            }
        }
    }
}
