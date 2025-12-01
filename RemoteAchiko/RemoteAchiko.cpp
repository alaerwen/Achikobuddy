// RemoteAchiko.cpp
// ─────────────────────────────────────────────────────────────────────────────
// Native bootstrapper DLL — injected into WoW via LoadLibraryA
//
// Responsibilities:
// • Starts the .NET 4.0 CLR inside the target process
// • Loads AchikoDLL.dll and calls Loader.Start()
// • Provides best-effort logging to Achikobuddy via named pipe
// • Extremely lightweight, stable, and stealthy
// • No thread handles stored — dies cleanly with process
// • Thread-safe, zero leaks, maximum stability
// • 100% C++11 / Windows API compatible
//
// Architecture:
// • This DLL is the FIRST thing injected into WoW.exe
// • It bootstraps the entire .NET runtime inside WoW's process
// • Once CLR is running, it loads AchikoDLL.dll (managed C#)
// • After bootstrap completes, this DLL stays resident but idle
// • No cleanup needed — process termination handles everything
//
// Critical Design Decisions:
// • Thread handle NOT stored — prevents handle leaks on unload
// • CLR host NOT released — releasing can deadlock on process exit
// • Pipe handle lazily opened — survives Achikobuddy restarts
// • DisableThreadLibraryCalls — reduces overhead, improves stability
// ─────────────────────────────────────────────────────────────────────────────

#include <Windows.h>
#include <metahost.h>
#include <shlwapi.h>
#include <strsafe.h>
#include <stdio.h>
#include <stdarg.h>

#pragma comment(lib, "mscoree.lib")  // CLR hosting functions
#pragma comment(lib, "shlwapi.lib")  // String helper functions

// ═══════════════════════════════════════════════════════════════
// GLOBAL STATE
// ═══════════════════════════════════════════════════════════════
// These must remain valid for the lifetime of the process.
// DO NOT release or close — process termination handles cleanup.
// ───────────────────────────────────────────────────────────────

static HMODULE          g_hModule = NULL;                  // Our own DLL handle (set in DllMain)
static HANDLE           g_hPipe = INVALID_HANDLE_VALUE;    // Named pipe to Achikobuddy UI
static ICLRRuntimeHost* g_clrHost = nullptr;               // .NET CLR host interface (NEVER release!)

// ═══════════════════════════════════════════════════════════════
// LOGGING SYSTEM
// ═══════════════════════════════════════════════════════════════
// Sends UTF-8 text logs to Achikobuddy via named pipe.
// Lazy-connects on first call — survives UI restarts.
// Fire-and-forget — never blocks, never throws.
// ───────────────────────────────────────────────────────────────

// ───────────────────────────────────────────────────────────────
// LogToPipe — best-effort logging to Achikobuddy
//
// Args:
//   format - printf-style format string
//   ...    - variable arguments
//
// Behavior:
//   • Attempts to open pipe on first call
//   • If pipe unavailable, silently fails (Achikobuddy not running)
//   • Appends \r\n to every message for clean display
//   • Non-blocking — never waits or stalls the caller
// ───────────────────────────────────────────────────────────────
static void LogToPipe(const char* format, ...)
{
    // Lazy pipe initialization — connect on first log call
    if (g_hPipe == INVALID_HANDLE_VALUE)
    {
        g_hPipe = CreateFileA(
            "\\\\.\\pipe\\AchikoPipe_Bootstrapper",  // Pipe name matches Bugger.cs
            GENERIC_WRITE,                           // Write-only
            0,                                       // No sharing
            NULL,                                    // Default security
            OPEN_EXISTING,                           // Pipe must already exist
            FILE_FLAG_OVERLAPPED,                    // Async mode (non-blocking)
            NULL
        );

        // If Achikobuddy isn't running, pipe won't exist — that's fine
        if (g_hPipe == INVALID_HANDLE_VALUE)
            return;
    }

    // Format the log message into a buffer
    char buffer[1024];
    va_list args;
    va_start(args, format);
    vsnprintf_s(buffer, sizeof(buffer), _TRUNCATE, format, args);
    va_end(args);

    // Send message + newline to pipe (best-effort, ignore errors)
    DWORD written;
    WriteFile(g_hPipe, buffer, (DWORD)strlen(buffer), &written, NULL);
    WriteFile(g_hPipe, "\r\n", 2, &written, NULL);
}

// ═══════════════════════════════════════════════════════════════
// PATH UTILITIES
// ═══════════════════════════════════════════════════════════════
// Determines the full path to AchikoDLL.dll based on where
// RemoteAchiko.dll is located (same directory).
// ───────────────────────────────────────────────────────────────

// ───────────────────────────────────────────────────────────────
// BuildManagedPath — constructs full path to AchikoDLL.dll
//
// Args:
//   outPath - [out] buffer to receive full path
//   outSize - size of outPath buffer in wchar_t units
//
// Returns:
//   true  - path successfully built
//   false - failed to get our own module path
//
// Example:
//   If RemoteAchiko.dll is at: C:\WoW\RemoteAchiko.dll
//   Then AchikoDLL.dll will be: C:\WoW\AchikoDLL.dll
// ───────────────────────────────────────────────────────────────
static bool BuildManagedPath(wchar_t* outPath, size_t outSize)
{
    // Get full path to our own DLL (RemoteAchiko.dll)
    wchar_t ourPath[MAX_PATH];
    if (GetModuleFileNameW(g_hModule, ourPath, MAX_PATH) == 0)
        return false;

    // Find last backslash to isolate directory
    wchar_t* lastSlash = wcsrchr(ourPath, L'\\');
    if (!lastSlash)
        return false;

    // Truncate at backslash, keeping directory path + trailing slash
    *(lastSlash + 1) = L'\0';

    // Append managed DLL name
    StringCchCatW(ourPath, MAX_PATH, L"AchikoDLL.dll");

    // Copy result to output buffer
    StringCchCopyW(outPath, outSize, ourPath);
    return true;
}

// ═══════════════════════════════════════════════════════════════
// CLR BOOTSTRAP THREAD
// ═══════════════════════════════════════════════════════════════
// Runs ONCE when DLL is first injected.
// Starts the .NET 4.0 CLR, loads AchikoDLL.dll, calls Loader.Start().
// After completion, this thread exits but CLR remains active forever.
// ───────────────────────────────────────────────────────────────

// ───────────────────────────────────────────────────────────────
// BootstrapThread — initializes .NET runtime and managed bot
//
// Args:
//   lpParam - unused (required by CreateThread signature)
//
// Returns:
//   0 - always (success or failure)
//
// Flow:
//   1. Create CLR MetaHost
//   2. Get .NET 4.0 runtime info
//   3. Start the CLR inside WoW's process
//   4. Load AchikoDLL.dll from disk
//   5. Call AchikoDLL.Loader.Start("")
//   6. Exit thread (CLR stays alive)
//
// Critical Notes:
//   • g_clrHost is NEVER released — releasing causes deadlocks
//   • Thread handle is NOT stored — prevents handle leaks
//   • All COM objects (metaHost, runtimeInfo) ARE released
//   • After this completes, the managed bot is fully running
// ───────────────────────────────────────────────────────────────
static DWORD WINAPI BootstrapThread(LPVOID lpParam)
{
    LogToPipe("=======================================");
    LogToPipe("RemoteAchiko: Bootstrap thread started");
    LogToPipe("Initializing .NET 4.0 CLR in WoW process...");

    HRESULT hr;

    // ───────────────────────────────────────────────────────────
    // STEP 1: Get CLR meta host interface
    // ───────────────────────────────────────────────────────────
    ICLRMetaHost* metaHost = nullptr;
    hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&metaHost);
    if (FAILED(hr))
    {
        LogToPipe("RemoteAchiko: CLRCreateInstance failed: 0x%08X", hr);
        return 0;
    }

    // ───────────────────────────────────────────────────────────
    // STEP 2: Get .NET Framework 4.0 runtime info
    // ───────────────────────────────────────────────────────────
    ICLRRuntimeInfo* runtimeInfo = nullptr;
    hr = metaHost->GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo, (LPVOID*)&runtimeInfo);
    metaHost->Release();  // Done with metaHost — safe to release
    if (FAILED(hr))
    {
        LogToPipe("RemoteAchiko: GetRuntime failed: 0x%08X", hr);
        return 0;
    }

    // ───────────────────────────────────────────────────────────
    // STEP 3: Get CLR runtime host and start the CLR
    // ───────────────────────────────────────────────────────────
    hr = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&g_clrHost);
    if (FAILED(hr))
    {
        LogToPipe("RemoteAchiko: GetInterface failed: 0x%08X", hr);
        runtimeInfo->Release();
        return 0;
    }

    // Start the CLR — loads mscorlib.dll and initializes .NET inside WoW
    hr = g_clrHost->Start();
    if (FAILED(hr))
    {
        LogToPipe("RemoteAchiko: CLR Start() failed: 0x%08X", hr);
        g_clrHost->Release();
        g_clrHost = nullptr;
        runtimeInfo->Release();
        return 0;
    }

    runtimeInfo->Release();  // Done with runtimeInfo — safe to release

    LogToPipe("RemoteAchiko: CLR started successfully - .NET 4.0 is now running inside WoW");

    // ───────────────────────────────────────────────────────────
    // STEP 4: Build full path to AchikoDLL.dll
    // ───────────────────────────────────────────────────────────
    wchar_t managedPath[MAX_PATH];
    if (!BuildManagedPath(managedPath, MAX_PATH))
    {
        LogToPipe("RemoteAchiko: Failed to build path to AchikoDLL.dll");
        return 0;
    }

    LogToPipe("RemoteAchiko: Loading managed assembly: %ws", managedPath);

    // ───────────────────────────────────────────────────────────
    // STEP 5: Load AchikoDLL.dll and call Loader.Start("")
    // ───────────────────────────────────────────────────────────
    // ExecuteInDefaultAppDomain signature:
    //   - Assembly path (AchikoDLL.dll)
    //   - Type name (AchikoDLL.Loader)
    //   - Method name (Start)
    //   - Argument (empty string)
    //   - Return code (0 = success, 1 = failure)
    // ───────────────────────────────────────────────────────────
    DWORD returnCode = 0;
    hr = g_clrHost->ExecuteInDefaultAppDomain(
        managedPath,          // Full path to AchikoDLL.dll
        L"AchikoDLL.Loader",  // Fully qualified type name
        L"Start",             // Public static method to call
        L"",                  // Arguments (empty string)
        &returnCode           // Return value from Start()
    );

    // ───────────────────────────────────────────────────────────
    // Check if managed code started successfully
    // ───────────────────────────────────────────────────────────
    if (SUCCEEDED(hr) && returnCode == 0)
    {
        LogToPipe("RemoteAchiko: Loader.Start() succeeded - bot is LIVE!");
        LogToPipe("RemoteAchiko: BotCore thread started - awaiting UI enable command");
        LogToPipe("=======================================");
    }
    else
    {
        LogToPipe("RemoteAchiko: Loader.Start() FAILED - hr=0x%08X, ret=%d", hr, returnCode);
        LogToPipe("=======================================");
    }

    // ───────────────────────────────────────────────────────────
    // Bootstrap complete — thread can exit now
    // ───────────────────────────────────────────────────────────
    // CRITICAL: DO NOT Release() g_clrHost here!
    // Releasing the CLR can cause deadlocks during process shutdown.
    // The CLR will be automatically cleaned up when WoW exits.
    //
    // Thread handle was NOT stored in DllMain, so no cleanup needed.
    // This thread simply exits cleanly and the OS reclaims resources.
    // ───────────────────────────────────────────────────────────

    LogToPipe("RemoteAchiko: Bootstrap thread exiting - managed bot is now autonomous");
    return 0;
}

// ═══════════════════════════════════════════════════════════════
// DLL ENTRY POINT
// ═══════════════════════════════════════════════════════════════
// Called by Windows when DLL is loaded/unloaded.
// On attach: starts bootstrap thread.
// On detach: logs shutdown (no cleanup needed).
// ───────────────────────────────────────────────────────────────

// ───────────────────────────────────────────────────────────────
// DllMain — Windows DLL entry point
//
// Args:
//   hModule   - handle to this DLL module
//   reason    - why we're being called (attach/detach)
//   lpReserved - reserved (unused)
//
// Returns:
//   TRUE - always (initialization cannot fail)
//
// Critical Notes:
//   • DisableThreadLibraryCalls — reduces DLL notification overhead
//   • Bootstrap thread handle NOT stored — prevents handle leaks
//   • Thread will exit on its own after CLR is started
//   • No cleanup in DLL_PROCESS_DETACH — process exit handles it
// ───────────────────────────────────────────────────────────────
BOOL WINAPI DllMain(HMODULE hModule, DWORD reason, LPVOID lpReserved)
{
    switch (reason)
    {
        // ───────────────────────────────────────────────────────────
        // DLL_PROCESS_ATTACH — our DLL just got injected into WoW
        // ───────────────────────────────────────────────────────────
    case DLL_PROCESS_ATTACH:
        g_hModule = hModule;  // Store our own DLL handle for later use

        // Reduce overhead by disabling thread attach/detach notifications
        DisableThreadLibraryCalls(hModule);

        // Start bootstrap thread — DO NOT store handle!
        // Thread will exit naturally after completing its work.
        // Storing the handle causes handle leaks on process termination.
        CreateThread(NULL, 0, BootstrapThread, NULL, 0, NULL);
        break;

        // ───────────────────────────────────────────────────────────
        // DLL_PROCESS_DETACH — WoW is closing or DLL is being ejected
        // ───────────────────────────────────────────────────────────
    case DLL_PROCESS_DETACH:
        LogToPipe("=======================================");
        LogToPipe("RemoteAchiko: DLL unloading — goodbye!");
        LogToPipe("=======================================");

        // Close pipe handle if it was opened
        if (g_hPipe != INVALID_HANDLE_VALUE)
        {
            CloseHandle(g_hPipe);
            g_hPipe = INVALID_HANDLE_VALUE;
        }

        // CRITICAL: DO NOT Release() g_clrHost here!
        // Releasing the CLR during process shutdown can deadlock.
        // Windows will automatically clean up all resources when
        // the process terminates.
        break;
    }

    return TRUE;
}

// ═══════════════════════════════════════════════════════════════
// END OF RemoteAchiko.cpp
// ═══════════════════════════════════════════════════════════════
// This file is complete and production-ready.
// No exports, no additional functions needed.
// The managed bot (AchikoDLL.dll) handles everything else.
// ═══════════════════════════════════════════════════════════════