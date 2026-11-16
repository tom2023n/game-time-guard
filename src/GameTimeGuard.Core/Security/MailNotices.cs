using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);
    bool TryUnprotect(string protectedText, out string plaintext);
}

public sealed class MailDraft
{
    public string To { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Body { get; init; } = "";
}

public static class MailNotices
{
    public static bool IsConfigured(GuardConfig config)
        => config.EmailEnabled
           && !string.IsNullOrWhiteSpace(config.SmtpHost)
           && !string.IsNullOrWhiteSpace(config.NotifyAddress);

    public static string? Validate(GuardConfig config)
    {
        if (!config.EmailEnabled)
            return null;
        if (string.IsNullOrWhiteSpace(config.SmtpHost))
            return "要发信就得填写 SMTP 服务器";
        if (config.SmtpPort is < 1 or > 65535)
            return "SMTP 端口不正确";
        if (string.IsNullOrWhiteSpace(config.NotifyAddress) || !config.NotifyAddress.Contains('@'))
            return "收件邮箱看起来不正确";
        return null;
    }

    public static MailDraft? ForTamper(GuardConfig config, string message)
        => config.NotifyTamper ? Draft(config, "游戏时间守护：发现异常", message) : null;

    public static MailDraft? ForTimeUp(GuardConfig config, string message)
        => config.NotifyTimeUp ? Draft(config, "游戏时间守护：时间到了", message) : null;

    public static MailDraft? ForDailySummary(GuardConfig config, string message)
        => config.NotifyDailySummary ? Draft(config, "游戏时间守护：今日小结", message) : null;

    public static MailDraft? Test(GuardConfig config)
        => Draft(config, "游戏时间守护：测试邮件", "这是一封测试邮件。如果收到了，通知设置可以用。");

    private static MailDraft? Draft(GuardConfig config, string subject, string body)
    {
        if (!IsConfigured(config))
            return null;
        return new MailDraft { To = config.NotifyAddress.Trim(), Subject = subject, Body = body };
    }
}
