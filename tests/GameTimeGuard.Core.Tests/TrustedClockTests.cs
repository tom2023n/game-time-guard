using GameTimeGuard.Core.Clock;

namespace GameTimeGuard.Core.Tests;

public class TrustedClockTests
{
    private static readonly TimeSpan China = TimeSpan.FromHours(8);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(3);

    [Fact]
    public void FirstSample_anchors_to_wall_clock_and_local_date()
    {
        var wall = Utc(2026, 11, 16, 12, 0);
        var sample = TrustedClock.Observe(null, wall, 1_000, China);

        Assert.Equal(wall, sample.TrustedUtc);
        Assert.Equal(new DateOnly(2026, 11, 16), sample.LogicalDate);
        Assert.False(sample.TamperLatched);
        Assert.Equal(China, sample.StoredOffset);
    }

    [Fact]
    public void SameBoot_advances_trusted_time_by_monotonic_delta()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 12, 0), 5_000, China);
        var next = TrustedClock.Observe(start, Utc(2026, 11, 16, 12, 1), 65_000, China, tolerance: Tolerance);

        Assert.Equal(start.TrustedUtc.AddMinutes(1), next.TrustedUtc);
        Assert.False(next.TamperLatched);
    }

    [Fact]
    public void ForwardWallJump_does_not_skip_the_day_or_latch()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 14, 0), 10_000, China);
        var jumped = start.WallUtc.AddHours(30);
        var next = TrustedClock.Observe(start, jumped, 70_000, China, tolerance: Tolerance);

        Assert.Equal(start.TrustedUtc.AddMinutes(1), next.TrustedUtc);
        Assert.Equal(new DateOnly(2026, 11, 16), next.LogicalDate);
        Assert.False(next.TamperLatched);
    }

    [Fact]
    public void BackwardWallJump_latches_and_does_not_rewind_trusted_time()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 14, 0), 10_000, China);
        var next = TrustedClock.Observe(start, Utc(2026, 11, 15, 8, 0), 70_000, China, tolerance: Tolerance);

        Assert.True(next.TamperLatched);
        Assert.Equal(TamperKind.ClockRollback, next.TamperKind);
        Assert.Equal(start.TrustedUtc.AddMinutes(1), next.TrustedUtc);
        Assert.Equal(new DateOnly(2026, 11, 16), next.LogicalDate);
    }

    [Fact]
    public void SmallBackwardSkew_within_tolerance_is_not_tamper()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 14, 0), 10_000, China);
        var wall = start.WallUtc.AddMinutes(1).AddSeconds(-90);
        var next = TrustedClock.Observe(start, wall, 70_000, China, tolerance: Tolerance);

        Assert.False(next.TamperLatched);
        Assert.Equal(start.TrustedUtc.AddMinutes(1), next.TrustedUtc);
    }

    [Fact]
    public void Walking_the_clock_backward_in_small_steps_cannot_reduce_trusted_time()
    {
        var sample = TrustedClock.Observe(null, Utc(2026, 11, 16, 14, 0), 0, China);
        var trusted = sample.TrustedUtc;
        var wall = sample.WallUtc;
        long mono = 0;
        for (var i = 0; i < 30; i++)
        {
            mono += 60_000;
            wall = wall.AddSeconds(-30);
            sample = TrustedClock.Observe(sample, wall, mono, China, tolerance: Tolerance);
            trusted = trusted.AddMinutes(1);
            Assert.Equal(trusted, sample.TrustedUtc);
        }

        Assert.False(sample.TamperLatched);
        Assert.Equal(new DateOnly(2026, 11, 16), sample.LogicalDate);
    }

    [Fact]
    public void Monotonic_crossing_local_midnight_rolls_the_logical_date()
    {
        var elevenFifty = Utc(2026, 11, 16, 15, 50);
        var start = TrustedClock.Observe(null, elevenFifty, 1_000, China);
        Assert.Equal(new DateOnly(2026, 11, 16), start.LogicalDate);

        var next = TrustedClock.Observe(start, elevenFifty.AddMinutes(20), 1_000 + 20 * 60 * 1000, China, tolerance: Tolerance);

        Assert.Equal(new DateOnly(2026, 11, 17), next.LogicalDate);
        Assert.False(next.TamperLatched);
    }

    [Fact]
    public void Reboot_with_wall_clock_behind_trusted_time_keeps_the_day()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 15, 0), 9_000_000, China);
        var next = TrustedClock.Observe(start, Utc(2026, 11, 16, 2, 0), 5_000, China, tolerance: Tolerance);

        Assert.True(next.TamperLatched);
        Assert.Equal(TamperKind.ClockRollback, next.TamperKind);
        Assert.Equal(start.TrustedUtc, next.TrustedUtc);
        Assert.Equal(start.LogicalDate, next.LogicalDate);
    }

    [Fact]
    public void Reboot_with_wall_clock_on_the_next_day_advances()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 15, 0), 9_000_000, China);
        var next = TrustedClock.Observe(start, Utc(2026, 11, 17, 0, 30), 4_000, China, tolerance: Tolerance);

        Assert.False(next.TamperLatched);
        Assert.Equal(new DateOnly(2026, 11, 17), next.LogicalDate);
        Assert.Equal(Utc(2026, 11, 17, 0, 30), next.TrustedUtc);
    }

    [Fact]
    public void Timezone_shift_does_not_move_the_logical_date_backward_or_forward()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 15, 30), 1_000, China);
        var next = TrustedClock.Observe(start, Utc(2026, 11, 16, 15, 31), 61_000, TimeSpan.FromHours(14), tolerance: Tolerance);

        Assert.True(next.TamperLatched);
        Assert.Equal(TamperKind.TimezoneShift, next.TamperKind);
        Assert.Equal(China, next.StoredOffset);
        Assert.Equal(new DateOnly(2026, 11, 16), next.LogicalDate);
    }

    [Fact]
    public void Ntp_disagreement_latches_and_can_catch_trusted_time_up()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 12, 0), 1_000, China);
        var ntp = Utc(2026, 11, 17, 1, 0);
        var next = TrustedClock.Observe(start, Utc(2026, 11, 16, 12, 1), 61_000, China, ntp, Tolerance);

        Assert.True(next.TamperLatched);
        Assert.Equal(TamperKind.NtpMismatch, next.TamperKind);
        Assert.Equal(ntp, next.TrustedUtc);
        Assert.Equal(new DateOnly(2026, 11, 17), next.LogicalDate);
    }

    [Fact]
    public void Latch_sticks_after_the_clock_looks_consistent_again()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 12, 0), 1_000, China);
        var tampered = TrustedClock.Observe(start, Utc(2026, 11, 16, 8, 0), 61_000, China, tolerance: Tolerance);
        var later = TrustedClock.Observe(tampered, Utc(2026, 11, 16, 8, 1), 121_000, China, tolerance: Tolerance);

        Assert.True(later.TamperLatched);
        Assert.Equal(start.TrustedUtc.AddMinutes(2), later.TrustedUtc);
    }

    [Fact]
    public void Acknowledge_clears_the_latch_without_rewinding_the_date()
    {
        var start = TrustedClock.Observe(null, Utc(2026, 11, 16, 12, 0), 1_000, China);
        var tampered = TrustedClock.Observe(start, Utc(2026, 11, 15, 12, 0), 61_000, China, tolerance: Tolerance);
        var cleared = TrustedClock.Acknowledge(tampered, China);

        Assert.False(cleared.TamperLatched);
        Assert.Equal(tampered.LogicalDate, cleared.LogicalDate);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, TimeSpan.Zero);
}
