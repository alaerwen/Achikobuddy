// App.xaml.cs
// ─────────────────────────────────────────────────────────────────────────────
// Application entry point — the soul of Achikobuddy
//
// Responsibilities:
// • Global application lifecycle management
// • Initializes Bugger logging system on startup
// • Ensures graceful shutdown of all components
// • Zero leaks, zero dangling threads, zero chaos
// • 100% .NET 4.0 / C# 7.3 compatible
//
// Architecture:
// • This is the FIRST C# code that runs when Achikobuddy.exe starts
// • OnStartup() → awakens Bugger → starts pipe servers → logging active
// • OnExit() → shuts down Bugger → closes pipes → flushes logs → clean death
// • Minimal, elegant, powerful — the foundation of everything
//
// Critical Design:
// • Bugger.Instance lazy-initializes on first access (thread-safe)
// • No complex initialization needed — just touch Bugger and it lives
// • Shutdown is explicit via Bugger.Shutdown() — never assume finalizers
// ─────────────────────────────────────────────────────────────────────────────

using System.Windows;
using Achikobuddy.Debug;

namespace Achikobuddy.Core
{
    // ═══════════════════════════════════════════════════════════════
    // App — WPF application lifecycle manager
    // ═══════════════════════════════════════════════════════════════
    // Inherits from System.Windows.Application (WPF base class).
    // This class is instantiated automatically by WPF when the .exe starts.
    // No manual instantiation needed — it's magic, but controlled magic.
    // ───────────────────────────────────────────────────────────────
    public partial class App : Application
    {
        // ═══════════════════════════════════════════════════════════════
        // APPLICATION STARTUP
        // ═══════════════════════════════════════════════════════════════
        // Called by WPF immediately after the application process starts,
        // but BEFORE any windows are created or shown.
        //
        // This is the perfect place to initialize global systems that
        // need to be alive for the entire application lifetime.
        // ───────────────────────────────────────────────────────────────

        // ───────────────────────────────────────────────────────────────
        // OnStartup — application initialization hook
        //
        // Args:
        //   e - startup event args (contains command-line args if needed)
        //
        // Behavior:
        //   1. Calls base.OnStartup() to let WPF do its thing
        //   2. Touches Bugger.Instance to wake up the logging system
        //   3. Bugger automatically starts both pipe servers
        //   4. All components can now log via Bugger.Instance.Log()
        //
        // Why touch Bugger here?
        //   • Ensures logging is active BEFORE any windows open
        //   • Catches early initialization errors from Launcher/Main
        //   • Pipe servers start immediately, ready for RemoteAchiko
        //
        // Thread safety:
        //   • Bugger uses double-checked locking singleton pattern
        //   • Safe to call from any thread, but this runs on UI thread
        // ───────────────────────────────────────────────────────────────
        protected override void OnStartup(StartupEventArgs e)
        {
            // Let WPF initialize its internal systems first
            base.OnStartup(e);

            // This single line awakens the entire logging infrastructure:
            // • Creates Bugger singleton instance
            // • Opens Achikobuddy.log file for writing
            // • Starts AchikoPipe_Bootstrapper (for RemoteAchiko.dll)
            // • Starts AchikoPipe_AchikoDLL (for AchikoDLL.dll)
            // • Ready to receive logs from all three sources (Main, RemoteAchiko, AchikoDLL)
            var bugger = Bugger.Instance;

            // Bugger now logs: "ACHIKOBUDDY DEBUG SYSTEM INITIALIZED"
            // From this point forward, everything can log safely
        }

        // ═══════════════════════════════════════════════════════════════
        // APPLICATION SHUTDOWN
        // ═══════════════════════════════════════════════════════════════
        // Called by WPF when the application is about to terminate.
        // This happens AFTER all windows have closed.
        //
        // This is our LAST chance to clean up global resources before
        // the process dies. Missing this means potential handle leaks,
        // unflushed logs, or dangling threads.
        // ───────────────────────────────────────────────────────────────

        // ───────────────────────────────────────────────────────────────
        // OnExit — application shutdown hook
        //
        // Args:
        //   e - exit event args (contains exit code)
        //
        // Behavior:
        //   1. Calls Bugger.Shutdown() to gracefully stop everything
        //   2. Bugger flushes all pending logs to disk
        //   3. Bugger cancels pipe server tasks
        //   4. Bugger closes DebugWindow if open
        //   5. Calls base.OnExit() to let WPF finish cleanup
        //
        // Why explicit shutdown?
        //   • Bugger has async tasks that need clean cancellation
        //   • Pipe servers must be stopped before handles close
        //   • Log files must be flushed or data is lost
        //   • Thread-safe shutdown prevents race conditions
        //
        // What if this isn't called?
        //   • Emergency termination (Task Manager kill) → OS cleans up
        //   • Graceful close (window X button) → this WILL be called
        //   • Bugger has fallback cleanup in AppDomain.DomainUnload
        // ───────────────────────────────────────────────────────────────
        protected override void OnExit(ExitEventArgs e)
        {
            // Shut down the entire logging and pipe infrastructure
            // This is a blocking call — waits for tasks to complete
            // Maximum 500ms wait per task, then forcibly abandons them
            Bugger.Shutdown();

            // Bugger now logs: "ACHIKOBUDDY FULLY SHUT DOWN"
            // Log file is flushed and closed

            // Let WPF finish its cleanup (closing handles, disposing resources)
            base.OnExit(e);

            // Application process will now terminate cleanly
            // Zero threads left running, zero handles leaked
        }

        // ═══════════════════════════════════════════════════════════════
        // END OF App.xaml.cs
        // ═══════════════════════════════════════════════════════════════
        // This file is complete and production-ready.
        // The application lifecycle is now fully managed.
        // ═══════════════════════════════════════════════════════════════
    }
}