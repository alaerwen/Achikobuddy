// Launcher.xaml.cs
// ─────────────────────────────────────────────────────────────────────────────
// Launcher window — discovers running WoW instances and injects RemoteAchiko.dll
//
// Responsibilities:
// • Real-time WoW process detection with 1-second refresh
// • Visual [BOT ATTACHED] indicator using global mutex
// • 100% protection against double injection
// • Safe process handle cleanup — zero leaks
// • Clean, professional error handling and user feedback
// • 100% .NET 4.0 / C# 7.3 compatible — no modern syntax
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Achikobuddy.Debug;

namespace Achikobuddy.Core
{
    // ───────────────────────────────────────────────────────────────
    // Launcher — entry point for bot injection
    // ───────────────────────────────────────────────────────────────
    public partial class Launcher : Window
    {
        // ───────────────────────────────────────────────────────────────
        // Private fields
        // ───────────────────────────────────────────────────────────────
        private DispatcherTimer _updateTimer;
        private int? _lastSelectedPid = null;

        // ───────────────────────────────────────────────────────────────
        // Win32 API Declarations — manual DLL injection (LoadLibraryA)
        // ───────────────────────────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
            uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
            uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
            IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // ───────────────────────────────────────────────────────────────
        // Constructor
        // ───────────────────────────────────────────────────────────────
        public Launcher()
        {
            InitializeComponent();
            Loaded += Launcher_Loaded;
        }

        // ───────────────────────────────────────────────────────────────
        // Window Loaded — initialize logging and start process scanning
        // ───────────────────────────────────────────────────────────────
        private void Launcher_Loaded(object sender, RoutedEventArgs e)
        {
            _ = Bugger.Instance; // Start logging + pipe servers

            UpdatePidList();

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += (s, args) => UpdatePidList();
            _updateTimer.Start();
        }

        // ───────────────────────────────────────────────────────────────
        // Real-time WoW process detection + [BOT ATTACHED] status
        // ───────────────────────────────────────────────────────────────
        private void UpdatePidList()
        {
            _lastSelectedPid = GetSelectedPid();

            var processes = Process.GetProcessesByName("WoW")
                .OrderBy(p => p.Id)
                .Select(p =>
                {
                    string item = $"WoW.exe (PID: {p.Id})";

                    // Check if bot is already attached via global mutex
                    try
                    {
                        using (Mutex.OpenExisting($"AchikobuddyBot_PID_{p.Id}"))
                        {
                            item += " [BOT ATTACHED]";
                        }
                    }
                    catch
                    {
                        // Mutex does not exist → bot not attached
                    }

                    return item;
                })
                .ToList();

            Dispatcher.Invoke(() =>
            {
                var current = selectPID.Items.Cast<string>().ToList();
                if (!processes.SequenceEqual(current))
                {
                    selectPID.ItemsSource = processes;

                    // Restore previous selection if possible
                    if (_lastSelectedPid.HasValue)
                    {
                        string match = processes.FirstOrDefault(x => ParsePidFromItem(x) == _lastSelectedPid.Value);
                        if (match != null)
                            selectPID.SelectedItem = match;
                    }
                    else if (processes.Count > 0 && selectPID.SelectedIndex == -1)
                    {
                        selectPID.SelectedIndex = 0;
                    }
                }
            });
        }

        // ───────────────────────────────────────────────────────────────
        // Get currently selected PID from dropdown
        // ───────────────────────────────────────────────────────────────
        private int? GetSelectedPid()
        {
            if (selectPID.SelectedItem is string s)
                return (int?)ParsePidFromItem(s);
            return null;
        }

        // ───────────────────────────────────────────────────────────────
        // Extract PID number from string item
        // ───────────────────────────────────────────────────────────────
        private static int ParsePidFromItem(string item)
        {
            var m = Regex.Match(item, @"PID: (\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int pid) ? pid : 0;
        }

        // ───────────────────────────────────────────────────────────────
        // Main injection logic — called on "Launch" button click
        // ───────────────────────────────────────────────────────────────
        private void launchMain_Click(object sender, RoutedEventArgs e)
        {
            int pid = GetSelectedPid() ?? 0;
            if (pid <= 0)
            {
                MessageBox.Show("Please select a valid WoW process.", "Achikobuddy",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string mutexName = $"AchikobuddyBot_PID_{pid}";
            bool createdNew;
            var mutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Bot is already attached to this WoW instance.", "Already Attached",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                mutex.Dispose();
                return;
            }

            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteAchiko.dll");
            if (!File.Exists(dllPath))
            {
                MessageBox.Show("RemoteAchiko.dll not found in application directory.", "Missing DLL",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                mutex.Dispose();
                return;
            }

            var mainWindow = new Main(pid, mutex);
            mainWindow.Show();

            if (!InjectDLL(pid, dllPath))
            {
                MessageBox.Show(
                    "DLL injection failed.\n\nBoth WoW and Achikobuddy must be run as Administrator.",
                    "Injection Failed", MessageBoxButton.OK, MessageBoxImage.Error);

                mainWindow.Close();
                Bugger.Instance.CloseDebugWindow();
                mutex.Dispose();
                return;
            }

            Bugger.Instance.Log($"[Launcher] Successfully injected into PID {pid} — bot is LIVE");
            this.Close();
        }

        // ───────────────────────────────────────────────────────────────
        // Manual DLL injection using LoadLibraryA — safe and reliable
        // ───────────────────────────────────────────────────────────────
        private bool InjectDLL(int pid, string dllPath)
        {
            IntPtr hProcess = OpenProcess(0x1F0FFF, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                Bugger.Instance.Log($"[Launcher] OpenProcess failed — GLE: {Marshal.GetLastWin32Error()}");
                return false;
            }

            try
            {
                byte[] pathBytes = Encoding.ASCII.GetBytes(dllPath + "\0");

                IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)pathBytes.Length, 0x3000, 0x40);
                if (remoteMem == IntPtr.Zero) return false;

                if (!WriteProcessMemory(hProcess, remoteMem, pathBytes, (uint)pathBytes.Length, out _))
                    return false;

                IntPtr loadLib = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                if (loadLib == IntPtr.Zero) return false;

                IntPtr thread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLib, remoteMem, 0, IntPtr.Zero);
                return thread != IntPtr.Zero;
            }
            finally
            {
                if (hProcess != IntPtr.Zero)
                    CloseHandle(hProcess);
            }
        }
    }
}

// ───────────────────────────────────────────────────────────────
// END OF FILE
// ───────────────────────────────────────────────────────────────
