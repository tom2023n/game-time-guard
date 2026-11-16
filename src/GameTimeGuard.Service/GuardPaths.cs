namespace GameTimeGuard.Service;

public static class GuardPaths
{
    public const string PipeName = "GameTimeGuard";
    public const string ServiceName = "GameTimeGuard";

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
}
