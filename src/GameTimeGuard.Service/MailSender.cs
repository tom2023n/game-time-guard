using System.Net;
using System.Net.Mail;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;

namespace GameTimeGuard.Service;

internal static class MailSender
{
    public static string? Send(GuardConfig config, MailDraft draft, ISecretProtector secrets, ILogger logger)
    {
        if (!secrets.TryUnprotect(config.SmtpPasswordProtected, out var password) && !string.IsNullOrEmpty(config.SmtpPasswordProtected))
            return "邮箱密码解不开，请重新填写";
        try
        {
            using var message = new MailMessage(From(config), draft.To, draft.Subject, draft.Body);
            using var client = new SmtpClient(config.SmtpHost, config.SmtpPort)
            {
                EnableSsl = config.SmtpUseSsl,
                Timeout = 15000,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            if (!string.IsNullOrWhiteSpace(config.SmtpUser))
                client.Credentials = new NetworkCredential(config.SmtpUser, password);
            client.Send(message);
            return null;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "通知邮件没有发出去");
            return "邮件没有发出去：" + ex.Message;
        }
    }

    private static string From(GuardConfig config)
        => config.SmtpUser.Contains('@') ? config.SmtpUser : config.NotifyAddress;
}
