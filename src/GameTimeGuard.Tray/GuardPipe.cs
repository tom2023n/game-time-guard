using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using GameTimeGuard.Core.Ipc;

namespace GameTimeGuard.Tray;

internal sealed class GuardPipe : IDisposable
{
    public const string PipeName = "GameTimeGuard";
    public event Action<IpcEvent>? EventReceived;

    private readonly object _write = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcResponse>> _waiters = new();
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readCancel;
    private int _ids;

    public bool IsConnected => _pipe is { IsConnected: true };

    public async Task<IpcResponse?> RequestAsync(IpcRequest request, CancellationToken cancellationToken = default)
    {
        if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false))
            return null;

        request.Type = "request";
        request.Id = Interlocked.Increment(ref _ids).ToString(CultureInfo.InvariantCulture);
        var waiter = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[request.Id] = waiter;
        try
        {
            var line = IpcProtocol.WriteRequest(request);
            lock (_write)
                _writer!.WriteLine(line);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            return await waiter.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
            _waiters.TryRemove(request.Id, out _);
            Close();
            return null;
        }
    }

    public void Dispose() => Close();

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
            return true;
        Close();
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(2500, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException or InvalidOperationException)
        {
            pipe.Dispose();
            return false;
        }

        _pipe = pipe;
        _writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true) { AutoFlush = true };
        _readCancel = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoop(pipe, _readCancel.Token));
        return true;
    }

    private async Task ReadLoop(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;
                if (!IpcProtocol.TryRead(line, out _, out var response, out var evt))
                    continue;
                if (response is not null && _waiters.TryRemove(response.Id, out var waiter))
                    waiter.TrySetResult(response);
                else if (evt is not null)
                    EventReceived?.Invoke(evt);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            foreach (var pair in _waiters.ToArray())
            {
                if (_waiters.TryRemove(pair.Key, out var waiter))
                    waiter.TrySetResult(new IpcResponse { Id = pair.Key, Ok = false, Error = "与守护服务的连接中断了" });
            }
        }
    }

    private void Close()
    {
        try { _readCancel?.Cancel(); } catch (ObjectDisposedException) { }
        _readCancel?.Dispose();
        _readCancel = null;
        try { _writer?.Dispose(); } catch (ObjectDisposedException) { }
        try { _pipe?.Dispose(); } catch (ObjectDisposedException) { }
        _writer = null;
        _pipe = null;
    }
}
