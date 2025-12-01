// PipeClient.cs
// ─────────────────────────────────────────────────────────────────────────────
// Managed bidirectional pipe system for AchikoDLL ↔ Achikobuddy UI
//
// Responsibilities:
// • Fire-and-forget logging — never blocks the game
// • Receives START/STOP commands from UI
// • Auto-reconnect if Achikobuddy crashes or restarts
// • Survives DLL unload / AppDomain teardown
// • Emergency fallback logging if main pipe fails
// • Detects broken pipes for BotCore auto-disable
// • Thread-safe, high-performance, maximum stability
// • 100% .NET 4.0 / C# 7.3 compatible
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace AchikoDLL.IPC
{
    // ═══════════════════════════════════════════════════════════════
    // PipeClient — central communication hub for logs and commands
    // ═══════════════════════════════════════════════════════════════
    public static class PipeClient
    {
        // private fields
        private static NamedPipeClientStream _logPipe;
        private static NamedPipeServerStream _commandPipe;
        private static Thread _logThread;
        private static Thread _commandThread;
        private static volatile bool _running;
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static readonly object _connectLock = new object();

        private const string LogPipeName = "AchikoPipe_AchikoDLL";
        private const string CommandPipeName = "AchikoPipe_Commands";

        // indicates whether the log pipe is broken
        public static bool IsBroken => _logPipe == null || !_logPipe.IsConnected || !_running;

        // fired for incoming commands from UI
        public static event Action<string> OnMessage;

        // ───────────────────────────────────────────────────────────────
        // initialization — register AppDomain unload
        // ───────────────────────────────────────────────────────────────
        static PipeClient()
        {
            AppDomain.CurrentDomain.DomainUnload += (s, e) => Stop();
        }

        // ───────────────────────────────────────────────────────────────
        // start both background threads
        // ───────────────────────────────────────────────────────────────
        public static void Start()
        {
            if (_running) return;

            _running = true;

            // log thread
            _logThread = new Thread(LogThreadLoop)
            {
                IsBackground = true,
                Name = "AchikoDLL → Achikobuddy (Logs)",
                Priority = ThreadPriority.AboveNormal
            };
            _logThread.Start();

            // command listener thread
            _commandThread = new Thread(CommandThreadLoop)
            {
                IsBackground = true,
                Name = "Achikobuddy → AchikoDLL (Commands)",
                Priority = ThreadPriority.Normal
            };
            _commandThread.Start();

            Log("[PipeClient] Bidirectional pipe system STARTED");
        }

        // ───────────────────────────────────────────────────────────────
        // stop all threads and flush remaining logs
        // ───────────────────────────────────────────────────────────────
        public static void Stop()
        {
            if (!_running) return;

            Log("[PipeClient] Stop requested — shutting down...");
            _running = false;

            try { _logThread?.Interrupt(); } catch { }
            try { _commandThread?.Interrupt(); } catch { }

            _logThread?.Join(1000);
            _commandThread?.Join(1000);

            FlushQueueBlocking();
            DisposePipes();

            _logThread = null;
            _commandThread = null;
            Log("[PipeClient] Pipe system STOPPED");
        }

        // ───────────────────────────────────────────────────────────────
        // add log message to queue (fire-and-forget)
        // ───────────────────────────────────────────────────────────────
        public static void Log(string message)
        {
            if (!_running || string.IsNullOrEmpty(message)) return;
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            _queue.Enqueue(line);
        }

        // ───────────────────────────────────────────────────────────────
        // send raw message to UI without timestamp
        // ───────────────────────────────────────────────────────────────
        public static void Send(string message)
        {
            if (!_running || string.IsNullOrEmpty(message)) return;
            _queue.Enqueue(message);
        }

        // ───────────────────────────────────────────────────────────────
        // LOG THREAD — flush queue to Achikobuddy
        // ───────────────────────────────────────────────────────────────
        private static void LogThreadLoop()
        {
            Log("[PipeClient] Log thread alive");

            while (_running)
            {
                try
                {
                    EnsureLogPipeConnected();
                    FlushQueueNonBlocking();
                }
                catch (ThreadInterruptedException) { break; }
                catch (Exception ex) { TryWriteEmergency($"[PipeClient] LogThread error: {ex.Message}"); }

                try { Thread.Sleep(33); } catch (ThreadInterruptedException) { break; }
            }

            Log("[PipeClient] Log thread exiting");
        }

        // ───────────────────────────────────────────────────────────────
        // COMMAND THREAD — receive START/STOP from UI
        // ───────────────────────────────────────────────────────────────
        private static void CommandThreadLoop()
        {
            Log("[PipeClient] Command listener thread alive");

            while (_running)
            {
                try
                {
                    EnsureCommandPipeConnected();

                    if (_commandPipe != null && _commandPipe.IsConnected)
                    {
                        byte[] buffer = new byte[256];
                        int bytesRead = _commandPipe.Read(buffer, 0, buffer.Length);

                        if (bytesRead > 0)
                        {
                            string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim('\0', '\n', '\r');
                            if (!string.IsNullOrEmpty(msg))
                            {
                                Log($"[PipeClient] Received command: {msg}");
                                OnMessage?.Invoke(msg);
                            }
                        }
                    }
                }
                catch (ThreadInterruptedException) { break; }
                catch (Exception ex) { Log($"[PipeClient] CommandThread error: {ex.Message}"); DisposeCommandPipe(); }

                try { Thread.Sleep(100); } catch (ThreadInterruptedException) { break; }
            }

            Log("[PipeClient] Command thread exiting");
        }

        // ───────────────────────────────────────────────────────────────
        // ensure log pipe is connected
        // ───────────────────────────────────────────────────────────────
        private static void EnsureLogPipeConnected()
        {
            if (_logPipe != null && _logPipe.IsConnected) return;

            lock (_connectLock)
            {
                if (_logPipe != null && _logPipe.IsConnected) return;

                DisposeLogPipe();

                try
                {
                    _logPipe = new NamedPipeClientStream(".", LogPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                    _logPipe.Connect(200);
                    Log("[PipeClient] Log pipe connected to Achikobuddy!");
                }
                catch { DisposeLogPipe(); }
            }
        }

        // ───────────────────────────────────────────────────────────────
        // ensure command pipe is connected
        // ───────────────────────────────────────────────────────────────
        private static void EnsureCommandPipeConnected()
        {
            if (_commandPipe != null && _commandPipe.IsConnected) return;

            DisposeCommandPipe();

            try
            {
                _commandPipe = new NamedPipeServerStream(CommandPipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                Log("[PipeClient] Waiting for Achikobuddy to connect command pipe...");
                _commandPipe.WaitForConnection();
                Log("[PipeClient] Command pipe connected!");
            }
            catch { DisposeCommandPipe(); }
        }

        // ───────────────────────────────────────────────────────────────
        // flush queued messages without blocking main thread
        // ───────────────────────────────────────────────────────────────
        private static void FlushQueueNonBlocking()
        {
            if (_logPipe == null || !_logPipe.IsConnected) return;

            string msg;
            while (_queue.TryDequeue(out msg))
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
                    _logPipe.Write(data, 0, data.Length);
                }
                catch
                {
                    DisposeLogPipe();
                    _queue.Enqueue(msg);
                    break;
                }
            }
        }

        // ───────────────────────────────────────────────────────────────
        // flush queue completely — blocking, used during shutdown
        // ───────────────────────────────────────────────────────────────
        private static void FlushQueueBlocking()
        {
            if (_logPipe == null || !_logPipe.IsConnected) return;

            int attempts = 0;
            string msg;
            while (_queue.TryDequeue(out msg) && attempts++ < 1000)
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
                    _logPipe.Write(data, 0, data.Length);
                    _logPipe.Flush();
                }
                catch { break; }
            }
        }

        // ───────────────────────────────────────────────────────────────
        // emergency log write if main pipe is dead
        // ───────────────────────────────────────────────────────────────
        private static void TryWriteEmergency(string msg)
        {
            try
            {
                using (NamedPipeClientStream temp = new NamedPipeClientStream(".", LogPipeName, PipeDirection.Out))
                {
                    temp.Connect(50);
                    byte[] data = Encoding.UTF8.GetBytes($"[EMERGENCY] {msg}\n");
                    temp.Write(data, 0, data.Length);
                }
            }
            catch { }
        }

        // ───────────────────────────────────────────────────────────────
        // dispose helpers
        // ───────────────────────────────────────────────────────────────
        private static void DisposeLogPipe() { try { _logPipe?.Dispose(); } catch { } _logPipe = null; }
        private static void DisposeCommandPipe() { try { _commandPipe?.Dispose(); } catch { } _commandPipe = null; }
        private static void DisposePipes() { DisposeLogPipe(); DisposeCommandPipe(); }

        // ═══════════════════════════════════════════════════════════════
        // END OF PipeClient.cs
        // ═══════════════════════════════════════════════════════════════
    }
}
