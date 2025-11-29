using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Achikobuddy.Core
{
    public partial class Main : Window
    {
        private readonly int _pid;
        private readonly Mutex _pidMutex;
        private DispatcherTimer _updateTimer;

        public Main(int pid, Mutex pidMutex)
        {
            InitializeComponent();

            _pid = pid;
            _pidMutex = pidMutex;

            Title = $"Achikobuddy — PID {_pid}";
            statusText.Text = "Status: Initializing...";
            statusText.Foreground = Brushes.Gold;

            Bugger.Log($"Attached to WoW process PID {_pid}");

            Loaded += Main_Loaded;
        }

        private void Main_Loaded(object sender, RoutedEventArgs e)
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _updateTimer.Tick += (s, args) => UpdateStatus();
            _updateTimer.Start();

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            try
            {
                bool processAlive = Process.GetProcessesByName("WoW").Any(p => p.Id == _pid);
                bool mutexAlive = false;

                try
                {
                    Mutex.OpenExisting($"AchikobuddyBot_PID_{_pid}");
                    mutexAlive = true;
                }
                catch { }

                if (processAlive && mutexAlive)
                {
                    statusText.Text = "Status: Connected | Running";
                    statusText.Foreground = Brushes.LimeGreen;
                }
                else if (processAlive)
                {
                    statusText.Text = "Status: Disconnected";
                    statusText.Foreground = Brushes.OrangeRed;
                    Bugger.Log("Bot disconnected from WoW process");
                    _updateTimer.Stop();
                }
                else
                {
                    statusText.Text = "Status: WoW Closed";
                    statusText.Foreground = Brushes.Red;
                    Bugger.Log("WoW process terminated");
                    _updateTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                statusText.Text = "Status: Error";
                Bugger.Log($"Status update error: {ex.Message}");
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Bugger.Log("Start button clicked");
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Bugger.Log("Stop button clicked");
        }

        private void ClickToMoveButton_Click(object sender, RoutedEventArgs e)
        {
            Bugger.Log("Click-to-Move toggled");
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateTimer?.Stop();

            try { _pidMutex?.ReleaseMutex(); } catch { }
            try { _pidMutex?.Dispose(); } catch { }

            Bugger.Log($"Detached from PID {_pid}");
            base.OnClosed(e);
        }
    }
}