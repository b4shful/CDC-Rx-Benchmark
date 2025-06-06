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
            FILE_FLAG_OVERLAPPED = 0x40000000,
            FILE_FLAG_NO_BUFFERING = 0x20000000,
            FILE_FLAG_RANDOM_ACCESS = 0x10000000,
            FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000,
            FILE_FLAG_WRITE_THROUGH = 0x80000000
        }

        // P/Invoke CreateFile
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

        // P/Invoke ReadFile
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped
        );

        // P/Invoke EscapeCommFunction (for toggling DTR/RTS)
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

        // P/Invoke GetCommState / SetCommState
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetCommState(SafeFileHandle hFile, ref Dcb lpDCB);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetCommState(SafeFileHandle hFile, [In] ref Dcb lpDCB);

        static void Main(string[] args)
        {
            string portName = args.Length > 0 ? args[0] : "COM9";

            // 1. Open the COM port with both read and write access
            using SafeFileHandle hPort = CreateFile(
                @"\\.\" + portName,
                FileAccess.GENERIC_READ | FileAccess.GENERIC_WRITE,
                FileShare.READ | FileShare.WRITE,
                IntPtr.Zero,
                CreationDisposition.OPEN_EXISTING,
                FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero
            );

            if (hPort.IsInvalid)
                throw new IOException($"Failed to open {portName}", Marshal.GetLastWin32Error());

            // 2. Drive DTR high if desired (optional)
            if (!EscapeCommFunction(hPort, ExtendedFunctions.SETDTR))
                throw new IOException("EscapeCommFunction(SETDTR) failed", Marshal.GetLastWin32Error());

            // 3. Retrieve current DCB, modify for 3 000 000 baud, 8N1, no flow control
            Dcb dcb = new Dcb
            {
                DCBLength = (uint)Marshal.SizeOf(typeof(Dcb))
            };

            if (!GetCommState(hPort, ref dcb))
                throw new IOException("GetCommState failed", Marshal.GetLastWin32Error());

            dcb.BaudRate = 3000000;         // 3 000 000 baud
            dcb.ByteSize = 8;               // 8 bits/data
            dcb.Parity = Parity.None;     // no parity
            dcb.StopBits = StopBits.One;    // 1 stop bit

            // Disable any software/hardware flow control:
            dcb.OutxCtsFlow = false;
            dcb.OutxDsrFlow = false;
            dcb.InX = false;
            dcb.OutX = false;
            dcb.CheckParity = false;
            dcb.ReplaceErrorChar = false;
            dcb.Null = false;
            dcb.DsrSensitivity = false;
            dcb.TxContinueOnXoff = false;
            dcb.ErrorChar = 0;
            dcb.EofChar = 0;
            dcb.EvtChar = 0;
            dcb.XonChar = 0;
            dcb.XoffChar = 0;

            // Enable DTR/RTS permanently:
            dcb.DtrControl = DtrControl.Enable;
            dcb.RtsControl = RtsControl.Enable;

            if (!SetCommState(hPort, ref dcb))
                throw new IOException("SetCommState failed", Marshal.GetLastWin32Error());

            // 4. Now enter the read/benchmark loop
            var buffer = new byte[64 * 1024];
            long total = 0;                  // for throughput
            ulong byteCount = 0;             // total bytes received
            ulong mismatchCount = 0;         // mismatches in ramp pattern
            byte expectedNext = 0;           // next expected value
            bool firstByte = true;           // initialize on first received

            var sw = Stopwatch.StartNew();

            while (true)
            {
                if (!ReadFile(hPort, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
                    throw new IOException("ReadFile failed", Marshal.GetLastWin32Error());

                if (read > 0)
                {
                    for (int i = 0; i < (int)read; i++)
                    {
                        byte b = buffer[i];

                        if (firstByte)
                        {
                            // On the first byte, just set expectedNext for the next loop
                            firstByte = false;
                        }
                        else
                        {
                            if (b != expectedNext)
                                mismatchCount++;
                        }

                        expectedNext = (byte)((b + 1) & 0xFF);
                        byteCount++;
                    }

                    total += read;
                    double secs = sw.Elapsed.TotalSeconds;
                    double kibps = (total / 1024.0) / secs;
                    double mibps = kibps / 1024.0;
                    double failurePct = byteCount > 0 ? (mismatchCount * 100.0) / byteCount : 0.0;

                    Console.Write(
                        $"\rRead: {total / 1024:N0} KiB, " +
                        $"{kibps:F0} KiB/s, {mibps:F3} MiB/s, " +
                        $"Bytes: {byteCount:N0}, Mismatches: {mismatchCount:N0}, " +
                        $"Fail%: {failurePct:F3}%"
                    );
                }
            }
        }
    }
}
