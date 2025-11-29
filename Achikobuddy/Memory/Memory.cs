using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Achikobuddy.Memory
{
    // This is the only public class for external processes to interact with.
    public static class ProcessHelper
    {
        // P/Invoke for reading memory from an external process.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);

        public static bool IsProcessAttached(int pid)
        {
            try
            {
                // Check if the process with the given PID is still running.
                Process.GetProcessById(pid);
                return true;
            }
            catch (ArgumentException)
            {
                // Process.GetProcessById throws an exception if the process is not found.
                return false;
            }
        }
    }
}

namespace Achikobuddy.Offsets
{
    // This is a placeholder for your offsets.
    // The injected DLL (BotCore) should handle all memory reads, so these are
    // only kept here for reference if you ever decide to build an external bot.
    public static class WoW
    {
        // Common WoW 1.12.1.5875 offsets
        public const uint ObjectManagerBase = 0x00B41414;
        public const int FirstObjectOffset = 0xAC;
        public const int NextObjectOffset = 0x3C;
        public const int ObjectTypeOffset = 0x14;
        public const int LocalPlayerHealthOffset = 0x834;
        public const int ObjectTypePlayer = 4;
        public const uint InGame = 0x00B4B424;
    }
}