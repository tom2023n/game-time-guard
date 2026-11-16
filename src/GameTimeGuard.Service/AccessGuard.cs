using System.Runtime.InteropServices;

namespace GameTimeGuard.Service;

/// <summary>
/// 收紧服务和进程的访问权限。不使用会蓝屏的关键进程标记。
/// 管理员仍可用 SeDebugPrivilege 或取得所有权绕过，绕过会被看门狗在下一分钟补回。
/// </summary>
internal static class AccessGuard
{
    private const int DaclSecurityInformation = 0x4;
    private const uint ScManagerConnect = 0x0001;
    private const uint WriteDac = 0x00040000;

    public static void ApplyServiceDacl(string serviceName, bool allowAdminStop, ILogger logger)
    {
        var sddl = allowAdminStop
            ? "D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)"
            : "D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWLOCRRC;;;BA)(A;;LCRC;;;AU)";
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var sd, out _))
                return;
            try
            {
                var manager = OpenScManager(null, null, ScManagerConnect);
                if (manager == IntPtr.Zero)
                    return;
                var handle = OpenService(manager, serviceName, WriteDac);
                try
                {
                    if (handle != IntPtr.Zero)
                        SetServiceObjectSecurity(handle, DaclSecurityInformation, sd);
                }
                finally
                {
                    if (handle != IntPtr.Zero)
                        CloseServiceHandle(handle);
                    CloseServiceHandle(manager);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(sd);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogDebug(ex, "暂时没能收紧服务 {Service} 的权限", serviceName);
        }
    }

    public static void ApplyProcessDacl(ILogger logger)
    {
        const string sddl = "D:(D;;0x0801;;;BA)(D;;0x0801;;;BU)(D;;0x0801;;;WD)(A;;GA;;;SY)";
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var sd, out _))
                return;
            try
            {
                if (!SetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, sd))
                    logger.LogDebug("没能收紧进程权限，Win32 错误 {Error}", Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.FreeHGlobal(sd);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            logger.LogDebug(ex, "当前系统没有设置进程权限的接口");
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, int revision, out IntPtr security, out uint size);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenScManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr manager, string service, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceObjectSecurity(IntPtr service, int information, IntPtr descriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetKernelObjectSecurity(IntPtr handle, int information, IntPtr descriptor);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
