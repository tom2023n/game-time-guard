using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GameTimeGuard.Tray;

internal static class AdminAccount
{
    public const string Warning =
        "当前登录的是管理员账户。保护仍然有效：没有家长密码就不能从这里暂停、停止或卸载。服务管理器、任务管理器和删文件也会被尽量拦住。懂行的管理员最终仍可能自己关掉这类软件，关掉之后会留下记录，并在下次启动时把中断的时间算进去。不必为了使用本软件去改账户类型。";

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                return true;

            var length = IntPtr.Size;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(identity.Token, 19, buffer, length, out _))
                    return false;
                var linked = Marshal.ReadIntPtr(buffer);
                try
                {
                    using var elevated = new WindowsIdentity(linked);
                    return new WindowsPrincipal(elevated).IsInRole(WindowsBuiltInRole.Administrator);
                }
                finally
                {
                    CloseHandle(linked);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int tokenInformationClass, IntPtr buffer, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
