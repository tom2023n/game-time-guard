using System.Diagnostics;
using System.Windows;

namespace GameTimeGuard.Tray;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        InitializeComponent();
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Dialogs.Alert("出现问题", args.Exception.Message);
            args.Handled = true;
        };

        if (Has(e.Args, "--authorize-uninstall"))
        {
            Shutdown(UninstallGate.Run() ? 0 : 1);
            return;
        }

        var mutexName = $@"Local\GameTimeGuard.Tray.{Process.GetCurrentProcess().SessionId}";
        _singleInstance = new Mutex(true, mutexName, out var created);
        _ownsMutex = created;
        if (!created)
        {
            Shutdown(0);
            return;
        }

        var shell = new TrayShell();
        shell.Start(Has(e.Args, "--wizard"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
            _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static bool Has(string[] args, string flag) =>
        args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));
}
