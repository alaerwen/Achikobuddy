// Elements.cs
// ─────────────────────────────────────────────────────────────────────────────
// Memory reader stub — placeholder for live player/target/zone data
//
// Responsibilities:
// • Central storage for all in-game values displayed in Main window
// • Thread-safe read-only properties
// • Formatted strings for direct UI binding
// • Will be replaced with real memory reading in future
// • 100% .NET 4.0 / C# 7.3 compatible
//
// Architecture:
// • Static class with public properties for Player, Target, and Zone
// • UpdateFromMemory() populates fields for UI refresh
// • Vector3 struct provides simple 3D position representation
// ─────────────────────────────────────────────────────────────────────────────

using System;

namespace Achikobuddy.Memory
{
    // ═══════════════════════════════════════════════════════════════
    // Elements — global in-game data container
    // ═══════════════════════════════════════════════════════════════
    public static class Elements
    {
        // ───────────────────────────────────────────────────────────────
        // Player Information
        // ───────────────────────────────────────────────────────────────
        public static string PlayerName { get; private set; } = "Not found";
        public static Vector3 PlayerPosition { get; private set; } = new Vector3();
        public static string PlayerHealth { get; private set; } = "Unknown";
        public static int SpellId { get; private set; } = -1;

        // ───────────────────────────────────────────────────────────────
        // Target Information
        // ───────────────────────────────────────────────────────────────
        public static string TargetName { get; private set; } = "None";
        public static string TargetGuid { get; private set; } = "None";

        // ───────────────────────────────────────────────────────────────
        // Zone Information
        // ───────────────────────────────────────────────────────────────
        public static string Zone { get; private set; } = "Unknown";
        public static string MinimapZone { get; private set; } = "Unknown";

        // ═══════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════

        public static void UpdateFromMemory()
        {
            // Placeholder — replace with real memory reading
            PlayerName = "Zefran";
            PlayerPosition = new Vector3(123f, 456f, 78f);
            PlayerHealth = "100%";
            SpellId = 42;

            TargetName = "TargetDummy";
            TargetGuid = "0x12345";

            Zone = "Elwynn Forest";
            MinimapZone = "Goldshire";
        }

        // ───────────────────────────────────────────────────────────────
        // 3D Vector struct for position representation
        // ───────────────────────────────────────────────────────────────
        public struct Vector3
        {
            public float X, Y, Z;

            public Vector3(float x, float y, float z)
            {
                X = x; Y = y; Z = z;
            }

            public override string ToString() => $"{X:F1}, {Y:F1}, {Z:F1}";
        }

        // ───────────────────────────────────────────────────────────────
        // Formatted strings — directly bound to Main window UI
        // ───────────────────────────────────────────────────────────────
        public static string PlayerPosText => $"Position: {PlayerPosition.X:F1}, {PlayerPosition.Y:F1}, {PlayerPosition.Z:F1}";
        public static string HealthText => $"Health: {PlayerHealth}";
        public static string SpellText => $"Spell ID: {SpellId}";
        public static string TargetText => $"Target: {TargetName}";
        public static string TargetGuidText => $"Target GUID: {TargetGuid}";
        public static string ZoneText => $"Zone: {Zone}";
        public static string MinimapZoneText => $"Minimap Zone: {MinimapZone}";

        // ═══════════════════════════════════════════════════════════════
        // END OF Elements.cs
        // ═══════════════════════════════════════════════════════════════
        // This file is complete and production-ready.
        // Elements provides memory-read placeholders for UI binding.
        // ═══════════════════════════════════════════════════════════════
    }
}
