namespace GameTimeGuard.Service;

public static class GuardPaths
{
    public const string PipeName = "GameTimeGuard";
    public const string ServiceName = "GameTimeGuard";
    public const string WatchServiceName = "GameTimeGuardWatch";
    public const string EnsureTaskName = "GameTimeGuardEnsure";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GameTimeGuard");

    public static string Secrets => Path.Combine(Root, "secrets");
    public static string Logs => Path.Combine(Root, "logs");
    public static string ConfigName => "config.json";
    public static string UsageName => "usage.json";
    public static string StatusFile => Path.Combine(Root, "status.json");
    public static string RuleOverride => Path.Combine(Root, "platforms.json");

    public static string InstalledRules => Path.Combine(AppContext.BaseDirectory, "rules", "platforms.json");

    public static string TrayExecutable => Path.Combine(AppContext.BaseDirectory, "GameTimeGuard.Tray.exe");

    public static string ServiceExecutable => Path.Combine(AppContext.BaseDirectory, "GameTimeGuard.Service.exe");

    public static string ShadowRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32", "config", "systemprofile", "AppData", "Local", "GameTimeGuard");

    public static string TrayFlag => Path.Combine(Root, "tray.flag");
}
