using System.IO;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;
using GameTimeGuard.Core.State;
using Microsoft.Win32;

namespace GameTimeGuard.Tray;

/// <summary>
/// 卸载程序以管理员身份运行。服务连不上时，直接读只有管理员能读的签名记录来核对密码。
/// 这里只读取，不创建新密钥。
/// </summary>
internal static class LocalAuth
{
    public static bool Verify(string password)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameTimeGuard");
            var files = new FileBlobStore(root);
            var secrets = new FileBlobStore(Path.Combine(root, "secrets"));
            var registry = new RegistryBlobStore();
            var key = ReadKey(secrets) ?? ReadKey(registry);
            if (key is null)
                return false;
            var config = RedundantStore.Read(files, registry, "config.json", key, "config");
            if (config.Status != StoreStatus.Authentic || config.Payload is null)
                return false;
            if (!StateCodec.TryParseConfig(config.Payload, out var parsed) || parsed is null)
                return false;
            return ParentPassword.Verify(password, parsed.ParentPassword);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static byte[]? ReadKey(IBlobStore store)
    {
        var read = store.Read(KeyRing.Name);
        if (string.IsNullOrWhiteSpace(read.Content))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(read.Content.Trim());
            return bytes.Length >= 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class RegistryBlobStore : IBlobStore
    {
        public BlobRead Read(string name)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GameTimeGuard", false);
                return key?.GetValue(name) is string value ? BlobRead.Found(value) : BlobRead.Empty();
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
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\GameTimeGuard", true);
                key?.SetValue(name, content, RegistryValueKind.String);
                error = null;
                return key is not null;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
