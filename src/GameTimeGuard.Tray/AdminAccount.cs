using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GameTimeGuard.Tray;

internal static class AdminAccount
{
    public const string Warning =
        "当前登录的是管理员账户。如果这是家长自己的账户，可以忽略这条提示。请不要让孩子使用管理员账户，否则孩子可以自己关掉时间限制。给孩子单独建一个本地标准账户，不必登录微软账号。";

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
