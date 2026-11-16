using GameTimeGuard.Service;
using Microsoft.Extensions.Logging.Console;

if (args.Contains("--ensure"))
{
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
    WatchdogLoop.Tick(loggerFactory.CreateLogger("GameTimeGuardEnsure"));
    return;
}

if (args.Contains("--watchdog"))
{
    var watch = Host.CreateApplicationBuilder(args);
    watch.Services.AddWindowsService(options => options.ServiceName = GuardPaths.WatchServiceName);
    watch.Services.AddHostedService<WatchdogService>();
    await watch.Build().RunAsync();
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "GameTimeGuard");
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
Directory.CreateDirectory(GuardPaths.Logs);
builder.Logging.AddProvider(new RollingFileLoggerProvider(Path.Combine(GuardPaths.Logs, "service.log")));
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(options => options.SourceName = "GameTimeGuard");
}

var runtime = new GuardRuntime();
builder.Services.AddSingleton(runtime);
builder.Services.AddSingleton<EventHub>();
builder.Services.AddHostedService<GuardWorker>();
builder.Services.AddHostedService<PipeWorker>();
builder.Services.AddHostedService<TrayWatchdog>();

var host = builder.Build();
runtime.UseLogger(host.Services.GetRequiredService<ILogger<GuardRuntime>>());
await host.RunAsync();
