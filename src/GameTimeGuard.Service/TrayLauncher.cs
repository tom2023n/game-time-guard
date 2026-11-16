using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GameTimeGuard.Service;

internal static class TrayLauncher
{
    public static void EnsureRunning(string exePath, ILogger logger)
    {
        if (!File.Exists(exePath))
            return;

        var running = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            try
            {
                running.Add(process.SessionId);
            }
            catch (InvalidOperationException)
            {
                // 进程刚退出。
            }
            finally
            {
                process.Dispose();
            }
        }

        if (!WTSEnumerateSessionsW(IntPtr.Zero, 0, 1, out var pointer, out var count))
            return;

        try
        {
            var size = Marshal.SizeOf<WtsSessionInfo>();
            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<WtsSessionInfo>(IntPtr.Add(pointer, index * size));
                if (info.State != 0 || info.SessionId == 0 || running.Contains(info.SessionId))
                    continue;
                StartInSession(info.SessionId, exePath, logger);
            }
        }
        finally
        {
            WTSFreeMemory(pointer);
        }
    }

    private static void StartInSession(int sessionId, string exePath, ILogger logger)
    {
        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            logger.LogDebug("会话 {Session} 没有可用的用户令牌，等待下次登录启动托盘。", sessionId);
            return;
        }

        var primary = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(userToken, 0x02000000, IntPtr.Zero, 2, 1, out primary))
            {
                logger.LogDebug("无法复制会话 {Session} 的令牌。", sessionId);
                return;
            }

            CreateEnvironmentBlock(out environment, primary, false);
            var startup = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = "winsta0\\default"
            };
            var command = new StringBuilder($"\"{exePath}\"");
            var started = CreateProcessAsUser(
                primary,
                null,
                command,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                0x00000400,
                environment,
                Path.GetDirectoryName(exePath),
                ref startup,
                out var processInfo);
            if (!started)
            {
                logger.LogDebug("在会话 {Session} 启动托盘失败，Win32 错误 {Error}。", sessionId, Marshal.GetLastWin32Error());
                return;
            }

            CloseHandle(processInfo.hProcess);
            CloseHandle(processInfo.hThread);
            logger.LogInformation("已在会话 {Session} 启动托盘程序。", sessionId);
        }
        finally
        {
            if (environment != IntPtr.Zero)
                DestroyEnvironmentBlock(environment);
            if (primary != IntPtr.Zero)
                CloseHandle(primary);
            CloseHandle(userToken);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSEnumerateSessionsW(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr duplicate);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string? application,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint flags,
        IntPtr environment,
        string? directory,
        ref StartupInfo startup,
        out ProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
