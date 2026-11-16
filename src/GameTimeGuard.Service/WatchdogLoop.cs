using System.Diagnostics;
using System.ServiceProcess;
using GameTimeGuard.Core.Engine;
using GameTimeGuard.Core.Security;
using GameTimeGuard.Core.State;

namespace GameTimeGuard.Service;

internal static class WatchdogLoop
{
    public static void Tick(ILogger logger)
    {
        StorageSecurity.PrepareDirectories(logger);
        var armed = ReadArm(logger);
        EnsureService(GuardPaths.ServiceName, $"\"{GuardPaths.ServiceExecutable}\"", armed, logger);
        EnsureService(GuardPaths.WatchServiceName, $"\"{GuardPaths.ServiceExecutable}\" --watchdog", armed, logger);
        if (!armed)
        {
            AccessGuard.ApplyServiceDacl(GuardPaths.ServiceName, allowAdminStop: false, logger);
            AccessGuard.ApplyServiceDacl(GuardPaths.WatchServiceName, allowAdminStop: false, logger);
            EnsureScheduledTask(logger);
        }
    }

    private static bool ReadArm(ILogger logger)
    {
        try
        {
            var stores = new IBlobStore[]
            {
                new FileBlobStore(GuardPaths.Root),
                new RegistryBlobStore(),
                new FileBlobStore(GuardPaths.ShadowRoot)
            };
            var secrets = new FileBlobStore(GuardPaths.Secrets);
            var keyRead = secrets.Read(KeyRing.Name);
            if (keyRead.Content is null)
                return false;
            var key = Convert.FromBase64String(keyRead.Content.Trim());
            var config = RedundantStore.ReadMany(stores, GuardPaths.ConfigName, key, "config");
            if (config.Payload is null || !StateCodec.TryParseConfig(config.Payload, out var parsed) || parsed is null)
                return false;
            return ProtectionControl.UninstallArmed(parsed, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is FormatException or IOException or InvalidDataException)
        {
            logger.LogDebug(ex, "看门狗没能读卸载授权，按仍需保护处理");
            return false;
        }
    }

    private static void EnsureService(string name, string binPath, bool armed, ILogger logger)
    {
        try
        {
            using var controller = new ServiceController(name);
            _ = controller.Status;
            if (!armed && controller.Status == ServiceControllerStatus.Stopped)
                controller.Start();
        }
        catch (InvalidOperationException)
        {
            if (armed)
                return;
            Run("sc.exe", $"create {name} binPath= {binPath} start= auto obj= LocalSystem", logger);
            Run("sc.exe", $"failure {name} reset= 86400 actions= restart/5000/restart/5000/restart/5000", logger);
            Run("sc.exe", $"start {name}", logger);
        }
    }

    private static void EnsureScheduledTask(ILogger logger)
    {
        var command = "\\\"" + GuardPaths.ServiceExecutable + "\\\" --ensure";
        Run("schtasks.exe", "/Create /TN " + GuardPaths.EnsureTaskName + " /SC MINUTE /MO 1 /RU SYSTEM /RL HIGHEST /F /TR \"" + command + "\"", logger);
    }

    private static void Run(string file, string arguments, ILogger logger)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit(8000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogDebug(ex, "看门狗执行 {File} 失败", file);
        }
    }
}

internal sealed class WatchdogService : BackgroundService
{
    private readonly ILogger<WatchdogService> _logger;

    public WatchdogService(ILogger<WatchdogService> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                WatchdogLoop.Tick(_logger);
                AccessGuard.ApplyProcessDacl(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "看门狗这一轮没有完成");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
