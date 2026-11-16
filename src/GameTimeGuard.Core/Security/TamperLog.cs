using System.Globalization;

namespace GameTimeGuard.Core.Security;

public static class TamperKinds
{
    public const string ServiceGap = "service-gap";
    public const string UninstallAttempt = "uninstall-attempt";
    public const string RecordTamper = "record-tamper";
    public const string ClockChange = "clock-change";
    public const string ProcessKill = "process-kill";
    public const string ProtectionChanged = "protection-changed";
}

public sealed class TamperEvent
{
    public long Id { get; set; }
    public string Utc { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Alert { get; set; }
    public bool Acknowledged { get; set; }
}

public sealed class TamperLogDocument
{
    public int Version { get; set; } = 1;
    public long Sequence { get; set; }
    public List<TamperEvent> Events { get; set; } = [];
}

/// <summary>
/// 只追加的事件记录。整份文档带序号并被签名，删掉中间某一条而留下更小的序号会被另一份副本盖回去。
/// </summary>
public static class TamperLog
{
    public const int MaxEvents = 200;

    public static TamperLogDocument Append(TamperLogDocument? current, string kind, string message, DateTimeOffset utc, bool alert)
    {
        var next = Copy(current);
        var id = next.Events.Count == 0 ? 1 : next.Events.Max(item => item.Id) + 1;
        next.Events.Add(new TamperEvent
        {
            Id = id,
            Utc = utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Kind = kind,
            Message = message,
            Alert = alert,
            Acknowledged = false
        });
        next.Sequence++;
        Trim(next);
        return next;
    }

    public static TamperLogDocument AcknowledgeAll(TamperLogDocument? current)
    {
        var next = Copy(current);
        foreach (var item in next.Events)
            item.Acknowledged = true;
        next.Sequence++;
        Trim(next);
        return next;
    }

    public static IReadOnlyList<TamperEvent> Pending(TamperLogDocument? current)
        => current?.Events.Where(item => item.Alert && !item.Acknowledged).ToArray() ?? [];

    private static TamperLogDocument Copy(TamperLogDocument? current)
    {
        var source = current ?? new TamperLogDocument();
        return new TamperLogDocument
        {
            Version = 1,
            Sequence = source.Sequence,
            Events = source.Events.Select(item => new TamperEvent
            {
                Id = item.Id,
                Utc = item.Utc,
                Kind = item.Kind,
                Message = item.Message,
                Alert = item.Alert,
                Acknowledged = item.Acknowledged
            }).ToList()
        };
    }

    private static void Trim(TamperLogDocument document)
    {
        if (document.Events.Count <= MaxEvents)
            return;
        var drop = document.Events.Count - MaxEvents;
        document.Events.RemoveAll(item =>
        {
            if (drop <= 0 || !item.Acknowledged)
                return false;
            drop--;
            return true;
        });
    }
}
