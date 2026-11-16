namespace GameTimeGuard.Service;

internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public RollingFileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(_path, categoryName, _gate);

    public void Dispose()
    {
    }

    private sealed class RollingFileLogger : ILogger
    {
        private readonly string _path;
        private readonly string _category;
        private readonly object _gate;

        public RollingFileLogger(string path, string category, object gate)
        {
            _path = path;
            _category = category;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {logLevel} {_category}: {formatter(state, exception)}";
            if (exception is not null)
                line += " " + exception.Message;
            lock (_gate)
            {
                try
                {
                    var info = new FileInfo(_path);
                    if (info.Exists && info.Length > 1024 * 1024)
                    {
                        var previous = _path + ".1";
                        if (File.Exists(previous))
                            File.Delete(previous);
                        File.Move(_path, previous);
                    }

                    File.AppendAllText(_path, line + Environment.NewLine);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
