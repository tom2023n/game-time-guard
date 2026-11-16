using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using GameTimeGuard.Core.Ipc;

namespace GameTimeGuard.Service;

public sealed class EventHub
{
    private readonly object _gate = new();
    private Action<IpcEvent>? _published;

    public event Action<IpcEvent>? Published
    {
        add
        {
            lock (_gate)
                _published += value;
        }
        remove
        {
            lock (_gate)
                _published -= value;
        }
    }

    public void Publish(IpcEvent evt)
    {
        Action<IpcEvent>? handlers;
        lock (_gate)
            handlers = _published;
        handlers?.Invoke(evt);
    }
}

public sealed class GuardWorker : BackgroundService
{
    private readonly GuardRuntime _runtime;
    private readonly EventHub _hub;
    private readonly ILogger<GuardWorker> _logger;

    public GuardWorker(GuardRuntime runtime, EventHub hub, ILogger<GuardWorker> logger)
    {
        _runtime = runtime;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        ConfigureRecovery();
        var nextNtp = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextNtp)
                {
                    var sample = await NtpClient.QueryAsync(stoppingToken).ConfigureAwait(false);
                    _runtime.NoteNtp(sample);
                    nextNtp = DateTimeOffset.UtcNow.AddMinutes(30);
                }

                foreach (var evt in _runtime.Poll())
                    _hub.Publish(evt);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "本轮游戏检查失败，5 秒后重试。");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ConfigureRecovery()
    {
        try
        {
            RunSc("failure GameTimeGuard reset= 86400 actions= restart/5000/restart/5000/restart/5000");
            RunSc("failureflag GameTimeGuard 1");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug(ex, "没能通过 sc.exe 设置服务恢复。安装程序会再设置一次。");
        }
    }

    private static void RunSc(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit(4000);
    }
}

public sealed class PipeWorker : BackgroundService
{
    private readonly GuardRuntime _runtime;
    private readonly EventHub _hub;
    private readonly ILogger<PipeWorker> _logger;
    private readonly ConcurrentDictionary<int, StreamWriter> _clients = new();
    private int _nextClient;

    public PipeWorker(GuardRuntime runtime, EventHub hub, ILogger<PipeWorker> logger)
    {
        _runtime = runtime;
        _hub = hub;
        _logger = logger;
        _hub.Published += Publish;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => Serve(connected), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                _logger.LogWarning(ex, "命名管道暂时不可用，稍后重试。");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            GuardPaths.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            16 * 1024,
            16 * 1024,
            security);
    }

    private async Task Serve(NamedPipeServerStream pipe)
    {
        var id = Interlocked.Increment(ref _nextClient);
        using (pipe)
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            _clients[id] = writer;
            try
            {
                while (pipe.IsConnected && await reader.ReadLineAsync().ConfigureAwait(false) is string line)
                {
                    if (!IpcProtocol.TryRead(line, out var request, out _, out _) || request is null)
                    {
                        await WriteAsync(writer, IpcProtocol.WriteResponse(new IpcResponse { Ok = false, Error = "请求格式不正确" })).ConfigureAwait(false);
                        continue;
                    }

                    var response = _runtime.Handle(request);
                    await WriteAsync(writer, IpcProtocol.WriteResponse(response)).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // 托盘退出了。
            }
            finally
            {
                _clients.TryRemove(id, out _);
            }
        }
    }

    private void Publish(IpcEvent evt)
    {
        var line = IpcProtocol.WriteEvent(evt);
        foreach (var pair in _clients)
        {
            try
            {
                WriteAsync(pair.Value, line).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                _clients.TryRemove(pair.Key, out _);
            }
        }
    }

    private static async Task WriteAsync(StreamWriter writer, string line)
    {
        // StreamWriter 不能并发写。每条连接一把锁。
        lock (writer)
            writer.WriteLine(line);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _hub.Published -= Publish;
        base.Dispose();
    }
}

public sealed class TrayWatchdog : BackgroundService
{
    private readonly ILogger<TrayWatchdog> _logger;

    public TrayWatchdog(ILogger<TrayWatchdog> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                TrayLauncher.EnsureRunning(GuardPaths.TrayExecutable, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "检查托盘程序时出错");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
