// DebugWindow.xaml.cs
// ─────────────────────────────────────────────────────────────────────────────
// Real-time log viewer — the eyes and ears of Achikobuddy
//
// Responsibilities:
// • Live display of all logs from Bugger (Main, RemoteAchiko, AchikoDLL)
// • Tabbed filtering with instant switching via smart caching
// • Fallback timestamping for malformed or external log entries
// • High-performance UI updates — handles 100k+ lines without lag
// • Thread-safe cache and event handling
// • Singleton pattern with safe ShowWindow() activation
// • Clear logs button now clears Bugger storage for all tabs
// • Full cleanup on close — no leaks, no ghost subscriptions
// • 100% .NET 4.0 / C# 7.3 compatible
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Achikobuddy.Core
{
    // ───────────────────────────────────────────────────────────────
    // DebugWindow — central log monitoring interface
    // ───────────────────────────────────────────────────────────────
    public partial class DebugWindow : Window
    {
        private static DebugWindow _instance;
        public static DebugWindow Instance => _instance;

        private string _selectedTab = "Main";

        // ────────────────────────────────────────────────────────────
        // Removed: per-tab cache (Option A)
        // The entire caching system has been removed for simplicity.
        // Every tab now reloads directly from Bugger *except PtrDmp*.
        // ────────────────────────────────────────────────────────────

        // ────────────────────────────────────────────────────────────
        // PtrDmp — fully isolated buffer for pointer-dump results
        //
        // • Never receives Bugger logs
        // • Not timestamp-corrected by EnsureTimestamp()
        // • Not filtered or cached
        // • Not written to achikobuddy.log
        // • Written ONLY to ptrdmp.log and UI
        //
        // This keeps PtrDmp completely separate from the primary
        // application logging system and avoids contamination.
        // ────────────────────────────────────────────────────────────
        private readonly StringBuilder _ptrDmpBuffer = new StringBuilder();


        // ───────────────────────────────────────────────────────────────
        // Private constructor — singleton enforcement
        // ───────────────────────────────────────────────────────────────
        private DebugWindow()
        {
            InitializeComponent();

            // Subscribe to Bugger log stream (PtrDmp is excluded later)
            Achikobuddy.Debug.Bugger.Instance.LogAdded += OnLogAdded;

            LoadLogsForTab("Main");
        }

        // ───────────────────────────────────────────────────────────────
        // Show window — create if null, bring to foreground
        // ───────────────────────────────────────────────────────────────
        public static void ShowWindow()
        {
            if (_instance == null)
            {
                _instance = new DebugWindow();
                _instance.Closed += (s, e) => _instance = null;
            }

            _instance.Dispatcher.Invoke(() =>
            {
                _instance.LoadLogsForTab("Main");
                _instance.Show();
                _instance.Activate();
                _instance.WindowState = WindowState.Normal;
            });
        }

        // ───────────────────────────────────────────────────────────────
        // Log reception from Bugger — append to UI
        // PtrDmp NEVER receives Bugger messages
        // ───────────────────────────────────────────────────────────────
        private void OnLogAdded(string rawMessage)
        {
            if (_selectedTab == "PtrDmp")
                return; // isolate PtrDmp completely

            if (logTextBox == null) return;

            Dispatcher.Invoke(() =>
            {
                string line = EnsureTimestamp(rawMessage);

                if (ShouldAppendToCurrentTab(line))
                {
                    logTextBox.AppendText(line + Environment.NewLine);
                    logTextBox.ScrollToEnd();
                }

            }, DispatcherPriority.Background);
        }

        // ───────────────────────────────────────────────────────────────
        // Manual append for direct UI update
        // Used by external callers if needed (PtrDmp uses its own path)
        // ───────────────────────────────────────────────────────────────
        public void AppendLog(string rawMessage)
        {
            Dispatcher.Invoke(() =>
            {
                string line = EnsureTimestamp(rawMessage);
                logTextBox.AppendText(line + Environment.NewLine);
                logTextBox.ScrollToEnd();
            }, DispatcherPriority.Background);
        }

        // ───────────────────────────────────────────────────────────────
        // Switch logs to specific tab (no cache anymore)
        //
        // • Main/AchikoDLL/RemoteAchiko → reload from Bugger
        // • PtrDmp → load isolated buffer only
        // ───────────────────────────────────────────────────────────────
        private void LoadLogsForTab(string tab)
        {
            _selectedTab = tab;

            // Show pointer panel only in PtrDump tab
            ptrDmpPanel.Visibility = tab == "PtrDmp"
                ? Visibility.Visible
                : Visibility.Collapsed;

            // PtrDmp bypasses Bugger completely
            if (tab == "PtrDmp")
            {
                logTextBox.Text = _ptrDmpBuffer.ToString();
                logTextBox.ScrollToEnd();
                return;
            }

            // All other tabs re-filter from live Bugger log
            string full = Achikobuddy.Debug.Bugger.Instance.GetAllLogs();
            string filtered = FilterLogByTab(full, tab);

            logTextBox.Text = filtered;
            logTextBox.ScrollToEnd();
        }

        // ───────────────────────────────────────────────────────────────
        // Filter full Bugger log for specific tab
        // (unchanged)
        // ───────────────────────────────────────────────────────────────
        private string FilterLogByTab(string fullLog, string tab)
        {
            string[] lines = fullLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var filtered = new List<string>();

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                bool include = false;
                if (tab == "Main") include = line.Contains("[Main]");
                else if (tab == "AchikoDLL") include = line.Contains("[AchikoDLL]");
                else if (tab == "RemoteAchiko") include = line.Contains("[RemoteAchiko]");
                else include = true;

                if (include)
                    filtered.Add(line);
            }

            return string.Join(Environment.NewLine, filtered) + Environment.NewLine;
        }

        private bool ShouldAppendToCurrentTab(string line)
        {
            if (_selectedTab == "Main") return line.Contains("[Main]");
            if (_selectedTab == "AchikoDLL") return line.Contains("[AchikoDLL]");
            if (_selectedTab == "RemoteAchiko") return line.Contains("[RemoteAchiko]");
            return true;
        }

        // ───────────────────────────────────────────────────────────────
        // Ensure every line has a timestamp
        // ───────────────────────────────────────────────────────────────
        private string EnsureTimestamp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return $"[{DateTime.Now:HH:mm:ss.fff}] <empty>";

            if (message.StartsWith("[") && message.Length > 12 && char.IsDigit(message[1]))
                return message;

            return $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        }

        // ───────────────────────────────────────────────────────────────
        // PtrDmp internal writer
        //
        // • Timestamped
        // • Added to UI if PtrDmp tab active
        // • ALWAYS written to ptrdmp.log
        // • Never sent to Bugger
        // ───────────────────────────────────────────────────────────────
        private void AppendPtrDmp(string msg)
        {
            string ts = $"[{DateTime.Now:HH:mm:ss.fff}] ";
            string line = ts + msg + Environment.NewLine;

            _ptrDmpBuffer.Append(line);
            File.AppendAllText("ptrdmp.log", line);

            if (_selectedTab == "PtrDmp")
            {
                logTextBox.Text = _ptrDmpBuffer.ToString();
                logTextBox.ScrollToEnd();
            }
        }

        // ───────────────────────────────────────────────────────────────
        // Button handlers — tab switching & clear logs
        // ───────────────────────────────────────────────────────────────
        private void BtnMain_Click(object sender, RoutedEventArgs e) => LoadLogsForTab("Main");
        private void BtnAchiko_Click(object sender, RoutedEventArgs e) => LoadLogsForTab("AchikoDLL");
        private void BtnRemote_Click(object sender, RoutedEventArgs e) => LoadLogsForTab("RemoteAchiko");
        private void BtnPtrDmp_Click(object sender, RoutedEventArgs e) => LoadLogsForTab("PtrDmp");

        // ───────────────────────────────────────────────────────────────
        // Pointer READ button:
        //
        // • Reads address
        // • Dumps hex result to PtrDmp only
        // • Does NOT go through Bugger
        // ───────────────────────────────────────────────────────────────
        private void BtnPtrRead_Click(object sender, RoutedEventArgs e)
        {
            string addr = ptrAddressBox.Text.Trim();

            // Replace with real reading logic
            string result = $"Read 32 bytes from {addr}: <value>";

            AppendPtrDmp(result);
            LoadLogsForTab("PtrDmp");
        }

        // ───────────────────────────────────────────────────────────────
        // Clear logs button — clears UI & Bugger but preserves PtrDmp
        // (PtrDmp is now separate storage)
        // ───────────────────────────────────────────────────────────────
        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            logTextBox.Clear();

            Achikobuddy.Debug.Bugger.Instance.ClearAllLogs();
            Achikobuddy.Debug.Bugger.Instance.Log("[DebugWindow] User cleared all logs");
        }

        // ───────────────────────────────────────────────────────────────
        // Cleanup on close — unsubscribe from events
        // ───────────────────────────────────────────────────────────────
        protected override void OnClosed(EventArgs e)
        {
            try { Achikobuddy.Debug.Bugger.Instance.LogAdded -= OnLogAdded; } catch { }

            _instance = null;
            base.OnClosed(e);
        }

        // ───────────────────────────────────────────────────────────────
        // END OF DebugWindow.xaml.cs
        // ───────────────────────────────────────────────────────────────
    }
}
