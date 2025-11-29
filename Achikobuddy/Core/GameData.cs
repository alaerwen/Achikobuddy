using System;
using AchikoDLL.Memory;
using AchikoDLL.Objects;

namespace Achikobuddy.Core
{
    /// <summary>
    /// Static class that holds the current state of the game and updates it periodically.
    /// </summary>
    public static class GameData
    {
        public static event Action OnDataUpdated;

        private static readonly System.Timers.Timer _timer;

        static GameData()
        {
            _timer = new System.Timers.Timer(200); // update every 200ms
            _timer.Elapsed += (s, e) => Update();
            _timer.Start();
        }

        public static bool IsConnected => MemoryReader.Instance.IsProcessOpen;

        // Player Data
        public static string PlayerName { get; private set; } = "Unknown";
        public static int Level { get; private set; }
        public static Vector3 Position { get; private set; } = new Vector3();
        public static int HealthCurrent { get; private set; }
        public static int HealthMax { get; private set; }
        public static float HealthPercent => HealthMax > 0 ? (HealthCurrent / (float)HealthMax) * 100f : 0f;
        public static bool IsInGame { get; private set; }
        public static bool IsGhost { get; private set; }

        // Target Data
        public static ulong TargetGuid { get; private set; }
        public static string TargetName { get; private set; } = "None";

        // Combat Data
        public static uint CurrentSpellId { get; private set; }
        public static bool IsCasting { get; private set; }

        // TBD fields
        public static string ZoneText => "TBD Zone";
        public static string MinimapZoneText => "TBD Minimap Zone";

        private static void Update()
        {
            if (!IsConnected)
            {
                // Don't update if we are not connected to a process
                return;
            }

            try
            {
                // 1. Update the Object Manager (this finds the LocalPlayer)
                ObjectManager.Update();

                var player = ObjectManager.Player;

                // 2. Read LocalPlayer data
                if (player != null && player.IsValid)
                {
                    PlayerName = player.Name;
                    Level = player.Level;
                    Position = player.Position;
                    HealthCurrent = player.Health;
                    HealthMax = player.MaxHealth;
                    TargetGuid = player.TargetGuid;
                    TargetName = player.TargetName;
                    CurrentSpellId = player.CastingSpellId;
                    IsCasting = player.IsCasting;
                    IsInGame = player.IsInGame;
                    IsGhost = player.IsGhost;
                }
                else
                {
                    // Reset to default states if player is null (e.g., character screen or loading)
                    PlayerName = IsConnected ? "Loading..." : "Not Connected";
                    HealthCurrent = 0;
                    HealthMax = 0;
                    TargetName = "None";
                    IsInGame = false;
                }

                // 3. Notify the UI
                OnDataUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                // Catch exceptions during memory reading (e.g., process closing, memory protected)
                System.Diagnostics.Debug.WriteLine($"[GameData] Update error: {ex.Message}");
                // In a real app, you might stop the timer and try to re-connect here
            }
        }
    }
}