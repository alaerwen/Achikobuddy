using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Achikobuddy.Core
{
    public partial class Launcher : Window
    {
        private DispatcherTimer _updateTimer;
        private int? _lastSelectedPid;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        public Launcher()
        {
            InitializeComponent();
            Loaded += Launcher_Loaded;
        }

        private void Launcher_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePidList();
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _updateTimer.Tick += (s, args) => UpdatePidList();
            _updateTimer.Start();
        }

        private void UpdatePidList()
        {
            _lastSelectedPid = GetSelectedPid();
            var wowProcesses = Process.GetProcessesByName("WoW").OrderBy(p => p.Id).ToArray();

            var newItems = new List<string>();
            foreach (var proc in wowProcesses)
            {
                string item = $"WoW.exe (PID: {proc.Id})";
                string mutexName = $"AchikobuddyBot_PID_{proc.Id}";

                try
                {
                    Mutex.OpenExisting(mutexName);
                    item += " [BOT ATTACHED]";
                }
                catch { }

                newItems.Add(item);
            }

            Dispatcher.Invoke(() =>
            {
                var currentItems = selectPID.Items.Cast<string>().ToList();
                if (!newItems.SequenceEqual(currentItems))
                {
                    selectPID.ItemsSource = newItems;

                    if (_lastSelectedPid.HasValue)
                    {
                        var matchingItem = newItems.FirstOrDefault(i => ParsePidFromItem(i) == _lastSelectedPid.Value);
                        if (matchingItem != null)
                            selectPID.SelectedItem = matchingItem;
                    }
                    else if (newItems.Any())
                    {
                        selectPID.SelectedIndex = 0;
                    }
                }
            });
        }

        private int? GetSelectedPid()
        {
            if (selectPID.SelectedItem is string selectedItem)
            {
                int pid = ParsePidFromItem(selectedItem);
                return pid > 0 ? (int?)pid : null;
            }
            return null;
        }

        private static int ParsePidFromItem(string item)
        {
            var match = Regex.Match(item, @"PID: (\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private void launchMain_Click(object sender, RoutedEventArgs e)
        {
            var pid = GetSelectedPid();
            if (!pid.HasValue || pid.Value == 0)
            {
                MessageBox.Show("Please select a WoW process.", "Achikobuddy",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string mutexName = $"AchikobuddyBot_PID_{pid.Value}";
            var mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Bot is already attached to this WoW process.", "Achikobuddy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                mutex.Dispose();
                return;
            }

            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteAchiko.dll");
            if (!File.Exists(dllPath))
            {
                MessageBox.Show("RemoteAchiko.dll not found.", "Achikobuddy",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                mutex.ReleaseMutex();
                mutex.Dispose();
                return;
            }

            // CRITICAL ORDER:
            // 1. Create Bugger window (starts pipe server)
            var bugger = new Bugger();
            bugger.Show();

            // 2. Give pipe server time to initialize
            Thread.Sleep(2000);

            // 3. Create Main window
            var mainWindow = new Main(pid.Value, mutex);
            mainWindow.Show();

            // 4. Inject DLL (it will now connect to the ready pipe)
            if (!InjectDLL(pid.Value, dllPath))
            {
                MessageBox.Show("DLL injection failed. Ensure both WoW and this app run as Administrator.",
                    "Achikobuddy", MessageBoxButton.OK, MessageBoxImage.Error);
                mainWindow.Close();
                bugger.Close();
                mutex.ReleaseMutex();
                mutex.Dispose();
                return;
            }

            // 5. Close launcher
            this.Close();
        }

        private bool InjectDLL(int pid, string dllPath)
        {
            IntPtr hProcess = OpenProcess(0x1F0FFF, false, pid);
            if (hProcess == IntPtr.Zero)
                return false;

            byte[] dllPathBytes = Encoding.ASCII.GetBytes(dllPath + "\0");
            IntPtr allocAddr = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)dllPathBytes.Length,
                0x1000 | 0x2000, 0x40);

            if (allocAddr == IntPtr.Zero)
                return false;

            if (!WriteProcessMemory(hProcess, allocAddr, dllPathBytes, (uint)dllPathBytes.Length, out _))
                return false;

            IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
            if (loadLibraryAddr == IntPtr.Zero)
                return false;

            IntPtr threadHandle = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr,
                allocAddr, 0, IntPtr.Zero);

            return threadHandle != IntPtr.Zero;
        }
    }
}