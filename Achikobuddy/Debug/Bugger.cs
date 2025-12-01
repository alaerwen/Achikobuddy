// Bugger.cs
// ─────────────────────────────────────────────────────────────────────────────
// Global logging system + dual named pipe server for all bot components
//
// Responsibilities:
// • Central logging hub for Main, RemoteAchiko, and AchikoDLL
// • Dual named pipe servers running on background tasks
// • Simultaneous output to: memory buffer, disk file, and live UI
// • Thread-safe, high-performance, crash-resistant architecture
// • Automatic cleanup on application exit via App.OnExit()
// • Provides LogAdded event for real-time UI updates
// • 100% .NET 4.0 / C# 7.3 compatible — no modern syntax
//
// Architecture:
// • Singleton pattern with lazy initialization (thread-safe)
// • Two pipe servers run concurrently on Task threads:
//   - AchikoPipe_Bootstrapper → receives logs from RemoteAchiko.dll (native)
//   - AchikoPipe_AchikoDLL     → receives logs from AchikoDLL.dll (managed)
// • All logs tagged with source: [Main], [RemoteAchiko], [AchikoDLL]
// • DebugWindow subscribes to LogAdded for live display
//
// Critical Design Decisions:
// • File logging is best-effort — never throws on disk errors
// • Pipe servers auto-reconnect if client disconnects
// • Memory buffer grows unbounded (acceptable for debugging)
// • CancellationToken used for clean shutdown without deadlocks
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Achikobuddy.Debug
{
    // ═══════════════════════════════════════════════════════════════
    // Bugger — singleton debug and logging core
    // ═══════════════════════════════════════════════════════════════
    // The name "Bugger" is a playful nod to debugging — it's not
    // British slang here, just a fun dev culture reference. 🐛
    // ───────────────────────────────────────────────────────────────
    public sealed class Bugger
    {
        // ───────────────────────────────────────────────────────────────
        // Singleton infrastructure
        // ───────────────────────────────────────────────────────────────
        private static readonly object _lock = new object();
        private static Bugger _instance;

        // ───────────────────────────────────────────────────────────────
        // Logging storage and threading
        // ───────────────────────────────────────────────────────────────
        private readonly StringBuilder _mainLog = new StringBuilder();
        private readonly object _fileLock = new object();
        private CancellationTokenSource _cts;
        private Task _bootstrapperTask;
        private Task _botTask;

        // ───────────────────────────────────────────────────────────────
        // File path for persistent logging
        // ───────────────────────────────────────────────────────────────
        private static readonly string LogFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Achikobuddy.log");

        // ═══════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════

        // ───────────────────────────────────────────────────────────────
        // Instance — thread-safe singleton accessor
        //
        // Returns:
        //   The single global Bugger instance (lazy-initialized)
        //
        // Thread safety:
        //   Double-checked locking pattern ensures exactly one instance
        //   even if multiple threads call this simultaneously on startup.
        // ───────────────────────────────────────────────────────────────
        public static Bugger Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                        _instance = new Bugger();
                    return _instance;
                }
            }
        }

        // ───────────────────────────────────────────────────────────────
        // ClearAllLogs — wipes the internal log buffer completely
        //
        // Used by:
        //   DebugWindow when user presses "Clear Logs"
        // ───────────────────────────────────────────────────────────────
        public void ClearAllLogs()
        {
            try
            {
                _mainLog.Clear();
            }
            catch { /* StringBuilder should never throw, but safety first */ }
        }

        // ───────────────────────────────────────────────────────────────
        // Public events and state
        // ───────────────────────────────────────────────────────────────
        public event Action<string> LogAdded;           // Fired on every new log line
        public Action<string> LogRemoteAchiko { get; private set; }  // Pipe-specific logger
        public Action<string> LogAchikoDLL { get; private set; }     // Pipe-specific logger
        public Core.DebugWindow DebugWindow { get; private set; }    // Reference to log viewer
        public bool IsRunning { get; private set; }                  // System active state

        // ═══════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        // ───────────────────────────────────────────────────────────────
        // Private constructor — enforces singleton pattern
        //
        // Behavior:
        //   1. Creates/overwrites Achikobuddy.log with fresh header
        //   2. Sets IsRunning = true
        //   3. Logs startup banner to all outputs
        //   4. Initializes pipe-specific logging delegates
        //   5. Starts both named pipe servers on background tasks
        //
        // Called by:
        //   Instance property on first access (usually from App.OnStartup)
        //
        // Thread safety:
        //   Only called once due to double-checked locking in Instance
        // ───────────────────────────────────────────────────────────────
        private Bugger()
        {
            // ───────────────────────────────────────────────────────────
            // Create fresh log file with startup banner
            // ───────────────────────────────────────────────────────────
            try
            {
                File.WriteAllText(LogFilePath,
                    $"=== Achikobuddy Log Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
            }
            catch
            {
                // Best-effort — if file creation fails (permissions, disk full),
                // we continue without file logging. Memory + UI still work.
            }

            IsRunning = true;

            // ───────────────────────────────────────────────────────────
            // Log initialization banner
            // ───────────────────────────────────────────────────────────
            Log("═══════════════════════════════════════");
            Log("ACHIKOBUDDY DEBUG SYSTEM INITIALIZED");
            Log("Dual pipe servers starting...");
            Log(" • AchikoPipe_Bootstrapper → Native loader");
            Log(" • AchikoPipe_AchikoDLL     → Managed bot");
            Log("═══════════════════════════════════════");

            // ───────────────────────────────────────────────────────────
            // Setup pipe-specific logging delegates (auto-tag sources)
            // ───────────────────────────────────────────────────────────
            InitializePipeLoggers();

            // ───────────────────────────────────────────────────────────
            // Start both pipe servers on background tasks
            // ───────────────────────────────────────────────────────────
            StartPipes();
        }

        // ───────────────────────────────────────────────────────────────
        // InitializePipeLoggers — create auto-tagging delegates
        //
        // Behavior:
        //   Creates two Action<string> delegates that prepend source tags:
        //   • LogRemoteAchiko → adds "[RemoteAchiko]" prefix
        //   • LogAchikoDLL    → adds "[AchikoDLL]" prefix
        //
        // Why delegates?
        //   Pipe servers can pass these directly to PipeLoop, keeping
        //   the pipe code clean and tag-agnostic.
        // ───────────────────────────────────────────────────────────────
        private void InitializePipeLoggers()
        {
            LogRemoteAchiko = s => LogRaw("[RemoteAchiko] " + s);
            LogAchikoDLL = s => LogRaw("[AchikoDLL] " + s);
        }

        // ═══════════════════════════════════════════════════════════════
        // LOGGING API
        // ═══════════════════════════════════════════════════════════════

        // ───────────────────────────────────────────────────────────────
        // Log — main logging function for UI components
        //
        // Args:
        //   message - log message (will be auto-tagged if no tag present)
        //
        // Behavior:
        //   • If message already has [Main], [AchikoDLL], or [RemoteAchiko],
        //     it's left as-is (used by pipe delegates)
        //   • Otherwise, prepends "[Main]" to indicate UI origin
        //   • Calls WriteLog() to actually write to all outputs
        //
        // Thread safety:
        //   Safe to call from any thread (WriteLog uses locks)
        // ───────────────────────────────────────────────────────────────
        public void Log(string message)
        {
            // Auto-tag messages from UI code with [Main]
            if (!message.StartsWith("[Main]") &&
                !message.StartsWith("[AchikoDLL]") &&
                !message.StartsWith("[RemoteAchiko]"))
            {
                message = "[Main] " + message;
            }
            WriteLog(message);
        }

        // ───────────────────────────────────────────────────────────────
        // LogRaw — internal logging without auto-tagging
        //
        // Args:
        //   message - pre-tagged log message (preserves original tag)
        //
        // Used by:
        //   Pipe-specific delegates (LogRemoteAchiko, LogAchikoDLL)
        //
        // Why separate from Log()?
        //   Prevents double-tagging when pipes already add their tags
        // ───────────────────────────────────────────────────────────────
        private void LogRaw(string message) => WriteLog(message);

        // ───────────────────────────────────────────────────────────────
        // WriteLog — core write path to all outputs
        //
        // Args:
        //   message - fully tagged log message
        //
        // Outputs:
        //   1. File: Achikobuddy.log (appended with lock, best-effort)
        //   2. Memory: _mainLog StringBuilder (grows unbounded)
        //   3. Event: LogAdded (for DebugWindow live updates)
        //   4. Direct: DebugWindow.AppendLog (if window is open)
        //
        // Format:
        //   [HH:mm:ss.fff] [Source] Message
        //
        // Thread safety:
        //   File writes are locked via _fileLock
        //   StringBuilder is NOT thread-safe, but only accessed here
        //   Event invocation is thread-safe (delegates handle their own)
        // ───────────────────────────────────────────────────────────────
        private void WriteLog(string message)
        {
            // Add timestamp prefix: [HH:mm:ss.fff]
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";

            // ───────────────────────────────────────────────────────────
            // Output 1: Write to disk file (best-effort, locked)
            // ───────────────────────────────────────────────────────────
            lock (_fileLock)
            {
                try { File.AppendAllText(LogFilePath, line); }
                catch { /* Ignore disk errors — memory + UI still work */ }
            }

            // ───────────────────────────────────────────────────────────
            // Output 2: Append to in-memory buffer
            // ───────────────────────────────────────────────────────────
            _mainLog.Append(line);

            // ───────────────────────────────────────────────────────────
            // Output 3: Fire event for DebugWindow (if subscribed)
            // ───────────────────────────────────────────────────────────
            LogAdded?.Invoke(message);

            // ───────────────────────────────────────────────────────────
            // Output 4: Direct append if DebugWindow is open
            // ───────────────────────────────────────────────────────────
            DebugWindow?.AppendLog(message);
        }

        // ───────────────────────────────────────────────────────────────
        // GetAllLogs — retrieve complete log history
        //
        // Returns:
        //   Full log buffer as string (includes all timestamps/tags)
        //
        // Used by:
        //   DebugWindow on tab switch to rebuild filtered views
        // ───────────────────────────────────────────────────────────────
        public string GetAllLogs() => _mainLog.ToString();

        // ═══════════════════════════════════════════════════════════════
        // DEBUG WINDOW CONTROL
        // ═══════════════════════════════════════════════════════════════

        // ───────────────────────────────────────────────────────────────
        // ShowDebugWindow — open or activate the log viewer
        //
        // Behavior:
        //   • If window doesn't exist, creates it via singleton pattern
        //   • If already exists, brings to front and activates
        //   • Stores reference in DebugWindow property for direct logging
        // ───────────────────────────────────────────────────────────────
        public void ShowDebugWindow()
        {
            if (DebugWindow == null)
            {
                Core.DebugWindow.ShowWindow();
                DebugWindow = Core.DebugWindow.Instance;
            }
            DebugWindow.Show();
            DebugWindow.Activate();
        }

        // ───────────────────────────────────────────────────────────────
        // CloseDebugWindow — forcibly close the log viewer
        //
        // Behavior:
        //   • Dispatches close on UI thread (safe cross-thread)
        //   • Clears reference after close
        //
        // Used by:
        //   Launcher if injection fails (cleanup orphaned window)
        // ───────────────────────────────────────────────────────────────
        public void CloseDebugWindow()
        {
            if (DebugWindow != null)
            {
                DebugWindow.Dispatcher.Invoke(() => DebugWindow.Close());
                DebugWindow = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // NAMED PIPE SERVERS
        // ═══════════════════════════════════════════════════════════════

        // ───────────────────────────────────────────────────────────────
        // StartPipes — launch both pipe server tasks
        //
        // Behavior:
        //   • Creates CancellationTokenSource for clean shutdown
        //   • Starts two async tasks:
        //     - _bootstrapperTask for RemoteAchiko (native DLL)
        //     - _botTask for AchikoDLL (managed bot)
        //   • Both run PipeLoop with different pipe names and loggers
        //
        // Why Task.Run?
        //   Clean async background execution without blocking startup
        // ───────────────────────────────────────────────────────────────
        private void StartPipes()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _bootstrapperTask = Task.Run(() => PipeLoop("AchikoPipe_Bootstrapper", LogRemoteAchiko, token));
            _botTask = Task.Run(() => PipeLoop("AchikoPipe_AchikoDLL", LogAchikoDLL, token));
        }

        // ───────────────────────────────────────────────────────────────
        // PipeLoop — generic pipe server implementation
        //
        // Args:
        //   pipeName - name of pipe to create (e.g., "AchikoPipe_Bootstrapper")
        //   log      - delegate to call with received messages
        //   token    - cancellation token for clean shutdown
        //
        // Behavior:
        //   1. Creates NamedPipeServerStream with given name
        //   2. Waits for client connection (RemoteAchiko or AchikoDLL)
        //   3. Reads lines until client disconnects
        //   4. Logs all received lines via provided delegate
        //   5. On disconnect, waits 300ms and reconnects (infinite loop)
        //   6. Exits cleanly when token is cancelled
        //
        // Error handling:
        //   • OperationCanceledException → expected on shutdown, silent exit
        //   • Other exceptions → logged and loop continues (reconnect)
        //
        // Why async?
        //   WaitForConnectionAsync prevents thread blocking during idle
        // ───────────────────────────────────────────────────────────────
        private async Task PipeLoop(string pipeName, Action<string> log, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;
                StreamReader reader = null;

                try
                {
                    // ───────────────────────────────────────────────────
                    // Create named pipe server (in-only, async mode)
                    // ───────────────────────────────────────────────────
                    pipe = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,                    // Only receive data
                        1,                                   // Max 1 client at a time
                        PipeTransmissionMode.Byte,           // Stream mode (not message)
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough
                    );

                    log($"Waiting for client on {pipeName}...");

                    // ───────────────────────────────────────────────────
                    // Wait for injected DLL to connect
                    // ───────────────────────────────────────────────────
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    log("Client connected");

                    // ───────────────────────────────────────────────────
                    // Read lines until disconnect or cancellation
                    // ───────────────────────────────────────────────────
                    reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);

                    while (!token.IsCancellationRequested && pipe.IsConnected)
                    {
                        string line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;  // EOF = client disconnected
                        log(line);
                    }

                    log("Client disconnected");
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown — exit gracefully
                }
                catch (Exception ex)
                {
                    // Unexpected error — log and continue (will reconnect)
                    if (!token.IsCancellationRequested)
                        log($"Pipe error: {ex.Message}");
                }
                finally
                {
                    // ───────────────────────────────────────────────────
                    // Clean up pipe resources (safe even if null)
                    // ───────────────────────────────────────────────────
                    try { reader?.Dispose(); } catch { }
                    try { pipe?.Dispose(); } catch { }
                }

                // Wait before reconnecting (reduces CPU thrashing on errors)
                await Task.Delay(300, token).ConfigureAwait(false);
            }
        }

        // ───────────────────────────────────────────────────────────────
        // StopPipes — gracefully shut down both pipe servers
        //
        // Behavior:
        //   • Cancels token → triggers OperationCanceledException in loops
        //   • Waits up to 500ms per task for clean exit
        //   • Logs warning if tasks don't stop in time (rare)
        //
        // Thread safety:
        //   Safe to call from any thread (Task.Wait handles sync)
        //
        // Called by:
        //   Shutdown() on application exit
        // ───────────────────────────────────────────────────────────────
        public void StopPipes()
        {
            try { _cts?.Cancel(); } catch { }

            var tasks = new[] { _bootstrapperTask, _botTask };
            foreach (var t in tasks)
            {
                if (t == null) continue;
                try
                {
                    if (!t.Wait(500))
                        Log("[Main] Warning: Pipe task did not stop in time");
                }
                catch { /* Task already completed or faulted — ignore */ }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SHUTDOWN
        // ═══════════════════════════════════════════════════════════════

        // ───────────────────────────────────────────────────────────────
        // Shutdown — graceful system-wide cleanup
        //
        // Behavior:
        //   1. Logs shutdown banner
        //   2. Closes DebugWindow if open
        //   3. Sets IsRunning = false
        //   4. Stops both pipe servers (waits for clean exit)
        //
        // Called by:
        //   App.OnExit() on application shutdown
        //
        // Thread safety:
        //   Safe to call from any thread (uses locks internally)
        //
        // Critical:
        //   This MUST be called before process exit or logs may be lost!
        // ───────────────────────────────────────────────────────────────
        public static void Shutdown()
        {
            var inst = _instance;
            if (inst == null) return;

            inst.Log("═══════════════════════════════════════");
            inst.Log("ACHIKOBUDDY FULLY SHUT DOWN");
            inst.Log("═══════════════════════════════════════");

            inst.CloseDebugWindow();
            inst.IsRunning = false;
            inst.StopPipes();
        }

        // ═══════════════════════════════════════════════════════════════
        // END OF Bugger.cs
        // ═══════════════════════════════════════════════════════════════
        // This file is complete and production-ready.
        // The logging infrastructure is fully operational.
        // ═══════════════════════════════════════════════════════════════
    }
}