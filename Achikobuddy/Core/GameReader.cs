using System;
using System.Diagnostics;
using AchikoDLL.Memory;

namespace Achikobuddy.Core
{
    /// <summary>
    /// Service responsible for initializing the connection to the game process.
    /// </summary>
    public class GameReader
    {
        public bool IsGameConnected => MemoryReader.Instance.IsProcessOpen;

        /// <summary>
        /// Attempts to connect to the game process by PID and starts the GameData polling.
        /// </summary>
        /// <param name="processId">The Process ID of the WoW client.</param>
        public bool Start(int processId)
        {
            // 1. Open the process handle
            if (MemoryReader.Instance.Open(processId))
            {
                // 2. The GameData static constructor already starts the timer,
                //    but we ensure a first update is triggered soon.
                System.Diagnostics.Debug.WriteLine($"[GameReader] Successfully connected to PID: {processId}. Data polling started.");
                return true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[GameReader] Failed to connect to PID: {processId}.");
                return false;
            }
        }

        public void Stop()
        {
            MemoryReader.Instance.Dispose();
        }
    }
}