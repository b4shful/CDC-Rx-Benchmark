using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SerialReceive
{
    internal class Program
    {
        const uint GENERIC_READ = 0x80000000;
        const uint FILE_SHARE_READ = 0x00000001;
        const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(
            SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, IntPtr lpOverlapped
        );

        static void Main(string[] args)
        {
            string port = args.Length > 0 ? args[0] : "COM16";
            using var hPort = CreateFile(
                @"\\.\" + port,
                GENERIC_READ,
                FILE_SHARE_READ,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero
            );
            if (hPort.IsInvalid)
                throw new IOException($"Failed to open {port}", Marshal.GetLastWin32Error());

            var buffer = new byte[64 * 1024];
            long total = 0;
            var sw = Stopwatch.StartNew();

            while (true)
            {
                if (!ReadFile(hPort, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
                    throw new IOException("ReadFile failed", Marshal.GetLastWin32Error());

                total += read;
                double secs = sw.Elapsed.TotalSeconds;
                double kbps = (total / 1024.0) / secs;
                Console.Write($"\rRead: {total / 1024:N0} KB, {kbps:F0} KB/s");
            }
        }
    }
}
