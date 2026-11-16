using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using GameTimeGuard.Core.Clock;
using GameTimeGuard.Core.Engine;
using GameTimeGuard.Core.Rules;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Service;

internal sealed class WindowsClock : IClockSource
{
    public DateTimeOffset GetWallUtc() => DateTimeOffset.UtcNow;

    public long GetMonotonicMilliseconds()
    {
        QueryInterruptTime(out var ticks);
        return (long)(ticks / 10_000);
    }

    public TimeSpan GetUtcOffset() => TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow);

    [DllImport("kernel32.dll")]
    private static extern void QueryInterruptTime(out ulong interruptTime);
}

internal static class NtpClient
{
    private static readonly string[] Servers = ["ntp.aliyun.com", "ntp.tencent.com", "time.windows.com"];

    public static async Task<DateTimeOffset?> QueryAsync(CancellationToken cancellationToken)
    {
        foreach (var server in Servers)
        {
            try
            {
                var sample = await QueryServerAsync(server, cancellationToken).ConfigureAwait(false);
                if (sample is not null)
                    return sample;
            }
            catch (Exception ex) when (ex is SocketException or IOException or TimeoutException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }
        }

        return null;
    }

    private static async Task<DateTimeOffset?> QueryServerAsync(string server, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = 2000;
        udp.Client.SendTimeout = 2000;
        await udp.SendAsync(SntpMessage.CreateRequest(), server, 123).ConfigureAwait(false);
        var result = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
        return SntpMessage.TryReadTransmitTimestamp(result.Buffer, out var utc) ? utc : null;
    }
}

internal static class ProcessInspector
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static List<ProcessObservation> Snapshot(IEnumerable<PlatformRule> enabledRules, PublisherCache publishers)
    {
        var list = new List<ProcessObservation>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = QueryPath(process.Id);
                if (string.IsNullOrWhiteSpace(path) || WindowsPath.IsUnsafe(path))
                    continue;
                string? publisher = null;
                if (RuleMatcher.RequiresPublisherLookup(enabledRules, path))
                    publisher = publishers.Get(path);
                list.Add(new ProcessObservation(process.Id, process.ProcessName, path, publisher));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 进程在枚举过程中退出了。
            }
            finally
            {
                process.Dispose();
            }
        }

        return list;
    }

    public static string? QueryPath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
                return null;
            return buffer.ToString();
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class PublisherCache
{
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
            return cached;
        string? publisher = null;
        try
        {
            var certificate = X509Certificate.CreateFromSignedFile(path);
            using var parsed = new X509Certificate2(certificate);
            publisher = parsed.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            publisher = null;
        }

        if (_cache.Count > 400)
            _cache.Clear();
        _cache[path] = publisher;
        return publisher;
    }
}

internal sealed class VerifiedTerminator : IProcessTerminator
{
    private readonly Func<(IReadOnlyList<PlatformRule> Rules, IReadOnlyList<GameTimeGuard.Core.Schedule.CustomGameRule> Custom)> _rules;
    private readonly PublisherCache _publishers;
    private readonly ILogger _logger;

    public VerifiedTerminator(
        Func<(IReadOnlyList<PlatformRule> Rules, IReadOnlyList<GameTimeGuard.Core.Schedule.CustomGameRule> Custom)> rules,
        PublisherCache publishers,
        ILogger logger)
    {
        _rules = rules;
        _publishers = publishers;
        _logger = logger;
    }

    public void Terminate(int processId)
    {
        try
        {
            var path = ProcessInspector.QueryPath(processId);
            if (WindowsPath.IsUnsafe(path))
                return;
            var (rules, custom) = _rules();
            var publisher = path is null ? null : _publishers.Get(path);
            var matched = RuleMatcher.MatchAll([new ProcessObservation(processId, "", path, publisher)], rules, custom);
            if (matched.Count == 0)
                return;

            using var process = Process.GetProcessById(processId);
            _logger.LogInformation("结束游戏进程 {Name} ({Pid}) {Path}", process.ProcessName, processId, path);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or NotSupportedException)
        {
            _logger.LogDebug(ex, "结束进程 {Pid} 时它已经退出", processId);
        }
    }
}
