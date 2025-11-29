#include <Windows.h>
#include <metahost.h>
#include <stdio.h>
#pragma comment(lib, "mscoree.lib")

HMODULE g_hModule = NULL;
HANDLE g_hPipe = INVALID_HANDLE_VALUE;

void ConnectToPipe()
{
    if (g_hPipe != INVALID_HANDLE_VALUE)
        return; // Already connected

    for (int i = 0; i < 10; i++)
    {
        g_hPipe = CreateFileA("\\\\.\\pipe\\AchikoPipe_Native",
            GENERIC_WRITE,
            0,
            NULL,
            OPEN_EXISTING,
            0,
            NULL);

        if (g_hPipe != INVALID_HANDLE_VALUE)
            break;

        Sleep(100);
    }
}

void LogToPipe(const char* message)
{
    if (g_hPipe == INVALID_HANDLE_VALUE)
        ConnectToPipe();

    if (g_hPipe != INVALID_HANDLE_VALUE)
    {
        char fullMessage[512];
        sprintf_s(fullMessage, "[RemoteAchiko] %s\r\n", message);

        DWORD bytesWritten;
        WriteFile(g_hPipe, fullMessage, (DWORD)strlen(fullMessage),
            &bytesWritten, NULL);
        FlushFileBuffers(g_hPipe);
    }
}

typedef HRESULT(__stdcall* CLRCreateInstanceFn)(
    REFCLSID clsid,
    REFIID riid,
    LPVOID* ppInterface);

DWORD WINAPI BootstrapThread(LPVOID)
{
    __try
    {
        Sleep(200); // Give pipe server time to be ready
        ConnectToPipe(); // Connect ONCE at the start

        LogToPipe("Bootstrap thread started");
        Sleep(50);

        // Get DLL directory
        wchar_t dllDir[MAX_PATH] = { 0 };

        LogToPipe("Getting module filename...");
        if (!GetModuleFileNameW(g_hModule, dllDir, MAX_PATH))
        {
            LogToPipe("ERROR: GetModuleFileNameW failed");
            while (true) Sleep(1000);
        }

        LogToPipe("Module filename obtained");

        wchar_t* lastSlash = wcsrchr(dllDir, L'\\');
        if (!lastSlash)
        {
            LogToPipe("ERROR: Could not find backslash in path");
            while (true) Sleep(1000);
        }

        *lastSlash = 0;
        LogToPipe("Directory path extracted");

        // Build path to AchikoDLL.dll
        wchar_t managedPath[MAX_PATH] = { 0 };
        wsprintfW(managedPath, L"%s\\AchikoDLL.dll", dllDir);

        // Convert to char for logging
        char pathLog[MAX_PATH];
        WideCharToMultiByte(CP_UTF8, 0, managedPath, -1, pathLog, MAX_PATH, NULL, NULL);

        char logMsg[MAX_PATH + 50];
        sprintf_s(logMsg, "Looking for: %s", pathLog);
        LogToPipe(logMsg);

        // Check if file exists
        DWORD fileAttr = GetFileAttributesW(managedPath);
        if (fileAttr == INVALID_FILE_ATTRIBUTES)
        {
            LogToPipe("ERROR: AchikoDLL.dll not found!");
            while (true) Sleep(1000);
        }

        LogToPipe("AchikoDLL.dll found");

        // Load CLR
        ICLRMetaHost* metaHost = nullptr;
        ICLRRuntimeInfo* runtime = nullptr;
        ICLRRuntimeHost* host = nullptr;

        LogToPipe("Loading mscoree.dll...");
        HMODULE hMscoree = LoadLibraryA("mscoree.dll");
        if (!hMscoree)
        {
            LogToPipe("Failed to load mscoree.dll");
            while (true) Sleep(1000);
        }

        LogToPipe("Getting CLRCreateInstance function...");
        CLRCreateInstanceFn CLRCreateInstance =
            (CLRCreateInstanceFn)GetProcAddress(hMscoree, "CLRCreateInstance");

        if (!CLRCreateInstance)
        {
            LogToPipe("Failed to get CLRCreateInstance");
            while (true) Sleep(1000);
        }

        LogToPipe("Creating CLR MetaHost...");
        HRESULT hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_PPV_ARGS(&metaHost));
        if (FAILED(hr))
        {
            char msg[128];
            sprintf_s(msg, "CLRCreateInstance failed: 0x%08X", hr);
            LogToPipe(msg);
            while (true) Sleep(1000);
        }

        LogToPipe("Getting CLR runtime v4.0.30319...");
        hr = metaHost->GetRuntime(L"v4.0.30319", IID_PPV_ARGS(&runtime));
        if (FAILED(hr))
        {
            char msg[128];
            sprintf_s(msg, "GetRuntime failed: 0x%08X", hr);
            LogToPipe(msg);
            while (true) Sleep(1000);
        }

        LogToPipe("Checking if runtime is loadable...");
        BOOL loadable = FALSE;
        runtime->IsLoadable(&loadable);
        if (!loadable)
        {
            LogToPipe("Runtime is not loadable");
            while (true) Sleep(1000);
        }

        LogToPipe("Getting CLR runtime host...");
        hr = runtime->GetInterface(CLSID_CLRRuntimeHost, IID_PPV_ARGS(&host));
        if (FAILED(hr))
        {
            char msg[128];
            sprintf_s(msg, "GetInterface failed: 0x%08X", hr);
            LogToPipe(msg);
            while (true) Sleep(1000);
        }

        LogToPipe("Starting CLR...");
        hr = host->Start();
        if (FAILED(hr))
        {
            char msg[128];
            sprintf_s(msg, "CLR Start failed: 0x%08X", hr);
            LogToPipe(msg);
            while (true) Sleep(1000);
        }

        LogToPipe("CLR started successfully!");

        // Try to get more detailed error info by loading assembly first
        LogToPipe("Attempting to verify assembly can be loaded...");

        DWORD result = 0;
        hr = host->ExecuteInDefaultAppDomain(
            managedPath,
            L"AchikoDLL.Loader",
            L"Start",
            L"",
            &result);

        if (SUCCEEDED(hr))
        {
            LogToPipe("Managed code executed successfully!");
            LogToPipe("===========================================");
            LogToPipe("C# BOT IS NOW RUNNING INSIDE WOW");
            LogToPipe("===========================================");
        }
        else
        {
            char msg[512];
            sprintf_s(msg, "ExecuteInDefaultAppDomain failed: 0x%08X", hr);
            LogToPipe(msg);

            // 0x80131513 = COR_E_TYPELOAD
            // Even though ildasm shows the method exists, CLR can't find it at runtime
            // This can happen if there are dependencies missing
            LogToPipe("ERROR: Type or method load failed");
            LogToPipe("Possible causes:");
            LogToPipe("1. Missing System.Core.dll or other dependencies");
            LogToPipe("2. .NET 4.0 runtime not fully installed");
            LogToPipe("3. Assembly has wrong runtime version");
            LogToPipe("4. Security/trust issues with the assembly");

            // Let's try calling with a non-empty argument
            LogToPipe("Retrying with non-empty argument...");
            hr = host->ExecuteInDefaultAppDomain(
                managedPath,
                L"AchikoDLL.Loader",
                L"Start",
                L"test",
                &result);

            if (SUCCEEDED(hr))
            {
                LogToPipe("SUCCESS with non-empty argument!");
            }
            else
            {
                sprintf_s(msg, "Still failed: 0x%08X", hr);
                LogToPipe(msg);
            }
        }

        // Keep thread alive forever
        LogToPipe("Bootstrap thread staying alive...");
        while (true)
            Sleep(1000);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        LogToPipe("CRITICAL: Bootstrap thread crashed!");
        LogToPipe("Likely bitness mismatch - verify RemoteAchiko.dll is x86");
        while (true) Sleep(1000);
    }

    return 0;
}

BOOL WINAPI DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
        CreateThread(NULL, 0, BootstrapThread, NULL, 0, NULL);
    }
    return TRUE;
}