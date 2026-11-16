namespace GameTimeGuard.Core.Clock;

/// <summary>
/// 用单调时钟（Windows 上应使用包含睡眠时间的 QueryInterruptTime）推进可信时间。
/// 同一启动周期内，墙钟向前跳（含被人拨快）不会换日，也不会增加配额；
/// 墙钟向后跳超过容差会锁定。睡眠本身不会被当成篡改，前提是单调时钟包含睡眠时间。
/// </summary>
public static class TrustedClock
{
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(3);

    public static ClockSample Observe(
        ClockSample? previous,
        DateTimeOffset wallUtc,
        long monotonicMilliseconds,
        TimeSpan utcOffset,
        DateTimeOffset? ntpUtc = null,
        TimeSpan? tolerance = null)
    {
        wallUtc = wallUtc.ToUniversalTime();
        if (ntpUtc is { } supplied)
            ntpUtc = supplied.ToUniversalTime();

        var allowedSkew = tolerance ?? DefaultTolerance;
        if (allowedSkew < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "容差不能为负");

        if (previous is null || !previous.HasSample)
            return FirstSample(wallUtc, monotonicMilliseconds, utcOffset, ntpUtc, allowedSkew);

        var tamperLatched = previous.TamperLatched;
        var tamperKind = previous.TamperKind;
        var offset = previous.StoredOffset;

        if (Absolute(utcOffset - previous.StoredOffset) > TimeSpan.FromMinutes(2))
        {
            tamperLatched = true;
            tamperKind = TamperKind.TimezoneShift;
        }

        DateTimeOffset trusted;
        var monoDelta = monotonicMilliseconds - previous.MonotonicMilliseconds;
        if (monoDelta >= 0)
        {
            trusted = previous.TrustedUtc.AddMilliseconds(monoDelta);
            var skew = wallUtc - previous.WallUtc.AddMilliseconds(monoDelta);
            if (skew < -allowedSkew)
            {
                tamperLatched = true;
                tamperKind = TamperKind.ClockRollback;
            }
        }
        else
        {
            var candidate = ntpUtc ?? wallUtc;
            if (candidate + allowedSkew < previous.TrustedUtc)
            {
                tamperLatched = true;
                tamperKind = TamperKind.ClockRollback;
                trusted = previous.TrustedUtc;
            }
            else if (candidate < previous.TrustedUtc)
            {
                trusted = previous.TrustedUtc;
            }
            else
            {
                trusted = candidate;
            }
        }

        if (ntpUtc is { } networkTime)
            (trusted, tamperLatched, tamperKind) = ApplyNtp(trusted, wallUtc, networkTime, allowedSkew, tamperLatched, tamperKind);

        var logical = LocalDate(trusted, offset);
        if (logical < previous.LogicalDate)
        {
            logical = previous.LogicalDate;
            tamperLatched = true;
            if (tamperKind == TamperKind.None)
                tamperKind = TamperKind.DateRegression;
        }

        return new ClockSample
        {
            HasSample = true,
            TrustedUtc = trusted,
            MonotonicMilliseconds = monotonicMilliseconds,
            WallUtc = wallUtc,
            StoredOffset = offset,
            LogicalDate = logical,
            TamperLatched = tamperLatched,
            TamperKind = tamperLatched ? tamperKind : TamperKind.None
        };
    }

    public static ClockSample Acknowledge(ClockSample clock, TimeSpan currentOffset)
    {
        var logical = LocalDate(clock.TrustedUtc, currentOffset);
        if (logical < clock.LogicalDate)
            logical = clock.LogicalDate;

        return clock with
        {
            TamperLatched = false,
            TamperKind = TamperKind.None,
            StoredOffset = currentOffset,
            LogicalDate = logical
        };
    }

    public static DateOnly LocalDate(DateTimeOffset trustedUtc, TimeSpan offset)
        => DateOnly.FromDateTime(trustedUtc.ToOffset(offset).DateTime);

    private static ClockSample FirstSample(
        DateTimeOffset wallUtc,
        long monotonicMilliseconds,
        TimeSpan utcOffset,
        DateTimeOffset? ntpUtc,
        TimeSpan allowedSkew)
    {
        var trusted = wallUtc;
        var tamper = false;
        var kind = TamperKind.None;
        if (ntpUtc is { } networkTime && Absolute(networkTime - wallUtc) > allowedSkew)
        {
            tamper = true;
            kind = TamperKind.NtpMismatch;
            trusted = networkTime;
        }

        return new ClockSample
        {
            HasSample = true,
            TrustedUtc = trusted,
            MonotonicMilliseconds = monotonicMilliseconds,
            WallUtc = wallUtc,
            StoredOffset = utcOffset,
            LogicalDate = LocalDate(trusted, utcOffset),
            TamperLatched = tamper,
            TamperKind = kind
        };
    }

    private static (DateTimeOffset Trusted, bool Latched, TamperKind Kind) ApplyNtp(
        DateTimeOffset trusted,
        DateTimeOffset wallUtc,
        DateTimeOffset networkTime,
        TimeSpan allowedSkew,
        bool tamperLatched,
        TamperKind tamperKind)
    {
        if (Absolute(networkTime - wallUtc) > allowedSkew)
        {
            tamperLatched = true;
            tamperKind = TamperKind.NtpMismatch;
        }

        if (networkTime > trusted + allowedSkew)
        {
            trusted = networkTime;
            tamperLatched = true;
            tamperKind = TamperKind.NtpMismatch;
        }
        else if (networkTime + allowedSkew < trusted)
        {
            tamperLatched = true;
            tamperKind = TamperKind.NtpMismatch;
        }

        return (trusted, tamperLatched, tamperKind);
    }

    private static TimeSpan Absolute(TimeSpan value) => value < TimeSpan.Zero ? -value : value;
}
