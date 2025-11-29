using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace Achikobuddy.Core
{
    public partial class Bugger : Window
    {
        private static Bugger _instance;
        private static readonly object _lock = new object();
        private Thread _nativePipeThread;
        private Thread _managedPipeThread;
        private volatile bool _running;

        public Bugger()
        {
            InitializeComponent();

            lock (_lock)
            {
                if (_instance != null)
                {
                    _instance.Activate();
                    this.Close();
                    return;
                }

                _instance = this;
            }

            _running = true;

            // Start separate threads for native and managed pipes
            _nativePipeThread = new Thread(NativePipeServerLoop)
            {
                IsBackground = true,
                Name = "AchikoNativePipeServer"
            };
            _nativePipeThread.Start();

            _managedPipeThread = new Thread(ManagedPipeServerLoop)
            {
                IsBackground = true,
                Name = "AchikoManagedPipeServer"
            };
            _managedPipeThread.Start();

            SafeLog("═══════════════════════════════════════");
            SafeLog("ACHIKOBUDDY DEBUG CONSOLE");
            SafeLog("═══════════════════════════════════════");
            SafeLog("Dual pipe servers started - waiting for connections...");
            SafeLog("Pipe 1: AchikoPipe_Native (C++ bootstrapper)");
            SafeLog("Pipe 2: AchikoPipe_Managed (C# bot)");
        }

        private void NativePipeServerLoop()
        {
            while (_running)
            {
                NamedPipeServerStream pipeServer = null;

                try
                {
                    pipeServer = new NamedPipeServerStream("AchikoPipe_Native",
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    SafeLog("[Native Pipe] Waiting for bootstrapper connection...");
                    pipeServer.WaitForConnection();
                    SafeLog("[Native Pipe] Bootstrapper connected!");

                    using (var reader = new StreamReader(pipeServer, System.Text.Encoding.UTF8))
                    {
                        while (_running && pipeServer.IsConnected)
                        {
                            string line = reader.ReadLine();
                            if (line != null)
                                SafeLog(line);
                            else
                                break;
                        }
                    }

                    SafeLog("[Native Pipe] Bootstrapper disconnected.");
                }
                catch (Exception ex)
                {
                    if (_running)
                        SafeLog($"[Native Pipe] Error: {ex.Message}");
                }
                finally
                {
                    try { pipeServer?.Dispose(); } catch { }
                }

                if (_running)
                    Thread.Sleep(500);
            }
        }

        private void ManagedPipeServerLoop()
        {
            while (_running)
            {
                NamedPipeServerStream pipeServer = null;

                try
                {
                    pipeServer = new NamedPipeServerStream("AchikoPipe_Managed",
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    SafeLog("[Managed Pipe] Waiting for C# bot connection...");
                    pipeServer.WaitForConnection();
                    SafeLog("[Managed Pipe] C# bot connected!");

                    using (var reader = new StreamReader(pipeServer, System.Text.Encoding.UTF8))
                    {
                        while (_running && pipeServer.IsConnected)
                        {
                            string line = reader.ReadLine();
                            if (line != null)
                                SafeLog(line);
                            else
                                break;
                        }
                    }

                    SafeLog("[Managed Pipe] C# bot disconnected.");
                }
                catch (Exception ex)
                {
                    if (_running)
                        SafeLog($"[Managed Pipe] Error: {ex.Message}");
                }
                finally
                {
                    try { pipeServer?.Dispose(); } catch { }
                }

                if (_running)
                    Thread.Sleep(500);
            }
        }

        public static void Log(string message)
        {
            SafeLog(message);
        }

        private static void SafeLog(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);

            if (_instance == null) return;

            if (_instance.Dispatcher.CheckAccess())
            {
                _instance.logTextBox.AppendText(line + Environment.NewLine);
                _instance.logTextBox.ScrollToEnd();
            }
            else
            {
                _instance.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _instance.logTextBox.AppendText(line + Environment.NewLine);
                    _instance.logTextBox.ScrollToEnd();
                }));
            }
        }

        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            logTextBox.Clear();
            SafeLog("Logs cleared.");
        }

        protected override void OnClosed(EventArgs e)
        {
            _running = false;

            // Give threads a moment to exit
            Thread.Sleep(1000);

            lock (_lock)
            {
                if (_instance == this)
                    _instance = null;
            }

            base.OnClosed(e);
        }
    }
}