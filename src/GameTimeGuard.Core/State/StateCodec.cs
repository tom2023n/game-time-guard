using System.Globalization;
using GameTimeGuard.Core.Clock;
using GameTimeGuard.Core.Quota;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.State;

public sealed class UsageDocument
{
    public int Version { get; set; } = 1;
    public long Sequence { get; set; }
    public string LogicalDate { get; set; } = "";
    public int UsedMilliseconds { get; set; }
    public int BonusSeconds { get; set; }
    public bool Warned10 { get; set; }
    public bool Warned5 { get; set; }
    public bool TimeUpNotified { get; set; }
    public bool TamperLatched { get; set; }
    public string? TamperReason { get; set; }
    public bool GraceActive { get; set; }
    public bool GraceAnnounced { get; set; }
    public string? GraceStartedUtc { get; set; }
    public string? GraceReason { get; set; }
    public bool HasClockSample { get; set; }
    public string? TrustedUtc { get; set; }
    public long MonotonicMilliseconds { get; set; }
    public string? WallUtc { get; set; }
    public int StoredOffsetMinutes { get; set; }
}

public static class StateCodec
{
    public static UsageDocument ToDocument(UsageSnapshot usage, ClockSample? clock)
    {
        var document = new UsageDocument
        {
            Version = 1,
            Sequence = usage.Sequence,
            LogicalDate = usage.LogicalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            UsedMilliseconds = usage.UsedMilliseconds,
            BonusSeconds = usage.BonusSeconds,
            Warned10 = usage.Warned10,
            Warned5 = usage.Warned5,
            TimeUpNotified = usage.TimeUpNotified,
            TamperLatched = usage.TamperLatched,
            TamperReason = usage.TamperReason,
            GraceActive = usage.GraceActive,
            GraceAnnounced = usage.GraceAnnounced,
            GraceStartedUtc = usage.GraceStartedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            GraceReason = usage.GraceReason
        };

        if (clock is { HasSample: true })
        {
            document.HasClockSample = true;
            document.TrustedUtc = clock.TrustedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            document.MonotonicMilliseconds = clock.MonotonicMilliseconds;
            document.WallUtc = clock.WallUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            document.StoredOffsetMinutes = (int)Math.Round(clock.StoredOffset.TotalMinutes);
        }

        return document;
    }

    public static string SerializeUsage(UsageSnapshot usage, ClockSample? clock) => GuardJson.Serialize(ToDocument(usage, clock));

    public static string SerializeConfig(GuardConfig config) => GuardJson.Serialize(config);

    public static bool TryParseUsage(string json, out UsageSnapshot usage, out ClockSample? clock)
    {
        usage = UsageSnapshot.Fresh(default);
        clock = null;
        UsageDocument? document;
        try
        {
            document = GuardJson.Deserialize<UsageDocument>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        if (document is null || !DateOnly.TryParseExact(document.LogicalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        DateTimeOffset? graceStarted = null;
        if (!string.IsNullOrWhiteSpace(document.GraceStartedUtc))
        {
            if (!DateTimeOffset.TryParse(document.GraceStartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedGrace))
                return false;
            graceStarted = parsedGrace.ToUniversalTime();
        }

        usage = new UsageSnapshot
        {
            LogicalDate = date,
            UsedMilliseconds = Math.Max(0, document.UsedMilliseconds),
            BonusSeconds = Math.Max(0, document.BonusSeconds),
            Warned10 = document.Warned10,
            Warned5 = document.Warned5,
            TimeUpNotified = document.TimeUpNotified,
            TamperLatched = document.TamperLatched,
            TamperReason = document.TamperReason,
            GraceActive = document.GraceActive,
            GraceAnnounced = document.GraceAnnounced,
            GraceStartedUtc = graceStarted,
            GraceReason = document.GraceReason,
            Sequence = document.Sequence
        };

        if (document.HasClockSample)
        {
            if (!DateTimeOffset.TryParse(document.TrustedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var trusted))
                return false;
            if (!DateTimeOffset.TryParse(document.WallUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var wall))
                return false;
            if (!Enum.TryParse<TamperKind>(usage.TamperReason, out var kind))
                kind = usage.TamperLatched ? TamperKind.RecordTampered : TamperKind.None;
            clock = new ClockSample
            {
                HasSample = true,
                TrustedUtc = trusted.ToUniversalTime(),
                MonotonicMilliseconds = document.MonotonicMilliseconds,
                WallUtc = wall.ToUniversalTime(),
                StoredOffset = TimeSpan.FromMinutes(document.StoredOffsetMinutes),
                LogicalDate = date,
                TamperLatched = usage.TamperLatched,
                TamperKind = usage.TamperLatched ? kind : TamperKind.None
            };
        }

        return true;
    }

    public static bool TryParseConfig(string json, out GuardConfig? config)
    {
        try
        {
            config = GuardJson.Deserialize<GuardConfig>(json);
            return config is not null;
        }
        catch (System.Text.Json.JsonException)
        {
            config = null;
            return false;
        }
    }
}
