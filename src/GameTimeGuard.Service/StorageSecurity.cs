using System.Security.AccessControl;
using System.Security.Principal;
using GameTimeGuard.Core.Security;
using Microsoft.Win32;

namespace GameTimeGuard.Service;

internal static class StorageSecurity
{
    public static void PrepareDirectories(ILogger logger)
    {
        Directory.CreateDirectory(GuardPaths.Root);
        Directory.CreateDirectory(GuardPaths.Secrets);
        Directory.CreateDirectory(GuardPaths.Logs);
        TryProtect(GuardPaths.Root, includeUsersRead: true, logger);
        TryProtect(GuardPaths.Secrets, includeUsersRead: false, logger);
        TryProtect(GuardPaths.Logs, includeUsersRead: true, logger);
        TryProtectRegistry(logger);
    }

    private static void TryProtect(string path, bool includeUsersRead, ILogger logger)
    {
        try
        {
            var info = new DirectoryInfo(path);
            var security = info.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                security.RemoveAccessRuleAll(rule);

            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(Allow(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl, inherit));
            security.AddAccessRule(Allow(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl, inherit));
            if (includeUsersRead)
            {
                security.AddAccessRule(Allow(
                    WellKnownSidType.BuiltinUsersSid,
                    FileSystemRights.ReadAndExecute,
                    inherit));
            }

            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            logger.LogWarning(ex, "暂时无法收紧 {Path} 的权限。服务如果不是以 SYSTEM 或管理员运行，标准用户以后可能改得了记录。", path);
        }
    }

    private static FileSystemAccessRule Allow(WellKnownSidType sid, FileSystemRights rights, InheritanceFlags inherit)
    {
        return new FileSystemAccessRule(
            new SecurityIdentifier(sid, null),
            rights,
            inherit,
            PropagationFlags.None,
            AccessControlType.Allow);
    }

    private static void TryProtectRegistry(ILogger logger)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\GameTimeGuard", true)
                ?? throw new InvalidOperationException("无法创建注册表项");
            var security = key.GetAccessControl();
            security.SetAccessRuleProtection(true, false);
            foreach (RegistryAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                security.RemoveAccessRuleAll(rule);
            security.AddAccessRule(new RegistryAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new RegistryAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            key.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "暂时无法收紧注册表权限。");
        }
    }
}

internal sealed class RegistryBlobStore : IBlobStore
{
    private const string KeyPath = @"SOFTWARE\GameTimeGuard";

    public BlobRead Read(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, false);
            if (key?.GetValue(name) is string value)
                return BlobRead.Found(value);
            return BlobRead.Empty();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return BlobRead.Unavailable();
        }
    }

    public bool TryWrite(string name, string content, out string? error)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(KeyPath, true)
                ?? throw new InvalidOperationException("无法写入注册表");
            key.SetValue(name, content, RegistryValueKind.String);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }
}
