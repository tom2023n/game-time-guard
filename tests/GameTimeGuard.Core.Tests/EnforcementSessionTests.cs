using GameTimeGuard.Core.Engine;
using GameTimeGuard.Core.Quota;
using GameTimeGuard.Core.Rules;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;

namespace GameTimeGuard.Core.Tests;

public class EnforcementSessionTests
{
    [Fact]
    public void Grace_expires_and_then_the_game_is_terminated()
    {
        var time = new FakeTime
        {
            Wall = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            Mono = 1_000_000,
            Offset = TimeSpan.FromHours(8)
        };
        var processes = new FakeProcesses();
        var terminator = new FakeTerminator();
        var session = NewSession(time, processes, terminator, minutes: 1);

        session.Poll();
        processes.Current.Add(new ProcessObservation(42, "game", @"D:\Steam\steamapps\common\Demo\game.exe", null));
        time.Advance(TimeSpan.FromSeconds(60));
        var grace = session.Poll();
        Assert.Equal(DecisionKind.Grace, grace.Evaluation.Kind);
        Assert.Empty(grace.TerminatedPids);

        time.Advance(TimeSpan.FromSeconds(30));
        var killed = session.Poll();
        Assert.Equal(DecisionKind.Terminate, killed.Evaluation.Kind);
        Assert.Equal([42], killed.TerminatedPids);
        Assert.Equal([42], terminator.Killed);
    }

    [Fact]
    public void Rolling_the_clock_back_does_not_reset_usage_and_locks_the_day()
    {
        var time = new FakeTime
        {
            Wall = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            Mono = 5_000_000,
            Offset = TimeSpan.FromHours(8)
        };
        var processes = new FakeProcesses();
        var session = NewSession(time, processes, new FakeTerminator(), minutes: 120);
        session.Poll();
        processes.Current.Add(new ProcessObservation(7, "game", @"E:\WeGameApps\Game\a.exe", null));

        time.Advance(TimeSpan.FromMinutes(10));
        var played = session.Poll();
        Assert.Equal(10 * 60 * 1000, played.Evaluation.Usage.UsedMilliseconds);
        Assert.False(played.Evaluation.Usage.TamperLatched);

        time.Wall = time.Wall.AddHours(-5);
        time.Mono += 5_000;
        var tampered = session.Poll();
        Assert.True(tampered.Evaluation.TamperNewlyLatched);
        Assert.Equal(new DateOnly(2026, 11, 16), tampered.Evaluation.Usage.LogicalDate);
        Assert.True(tampered.Evaluation.Usage.UsedMilliseconds >= 10 * 60 * 1000);
        Assert.Equal(0, tampered.Evaluation.RemainingSeconds);
        Assert.Equal(DecisionKind.Grace, tampered.Evaluation.Kind);
    }

    [Fact]
    public void Real_midnight_resets_the_quota()
    {
        var time = new FakeTime
        {
            Wall = new DateTimeOffset(2026, 11, 16, 15, 50, 0, TimeSpan.Zero),
            Mono = 8_000_000,
            Offset = TimeSpan.FromHours(8)
        };
        var processes = new FakeProcesses
        {
            Current = { new ProcessObservation(9, "game", @"D:\三角洲行动\game.exe", null) }
        };
        var session = NewSession(time, processes, new FakeTerminator(), minutes: 120);
        session.Poll();
        time.Advance(TimeSpan.FromMinutes(20));
        var rolled = session.Poll();

        Assert.Equal(new DateOnly(2026, 11, 17), rolled.Clock.LogicalDate);
        Assert.Equal(20 * 60 * 1000, rolled.Evaluation.Usage.UsedMilliseconds);
        Assert.Equal(100 * 60, rolled.Evaluation.RemainingSeconds);
        Assert.False(rolled.Evaluation.Usage.TamperLatched);
    }

    [Fact]
    public void Password_is_required_for_bonus_and_settings()
    {
        var time = new FakeTime
        {
            Wall = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            Mono = 1,
            Offset = TimeSpan.FromHours(8)
        };
        var session = NewSession(time, new FakeProcesses(), new FakeTerminator(), minutes: 120);
        Assert.False(string.IsNullOrEmpty(session.GrantBonus("nope", 30)));
        Assert.Null(session.GrantBonus("correct-horse", 30));
        Assert.Equal(30 * 60, session.Usage.BonusSeconds);
        Assert.False(string.IsNullOrEmpty(session.UpdateSettings("nope", DefaultPolicy.Create())));
    }

    private static EnforcementSession NewSession(FakeTime time, FakeProcesses processes, FakeTerminator terminator, int minutes)
    {
        var config = DefaultPolicy.Uninitialized();
        config.SetupComplete = true;
        config.ParentPassword = ParentPasswordHash();
        config.DailyMinutes = DayNames.All.Select(day => new DayQuota { Day = day, Minutes = minutes }).ToList();
        return new EnforcementSession(
            config,
            UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)),
            null,
            PlatformCatalog.LoadEmbedded(),
            processes,
            terminator,
            time,
            TimeSpan.FromHours(1))
        {
            PasswordIterations = 10_000
        };
    }

    private static PasswordHash ParentPasswordHash() => ParentPassword.Hash("correct-horse", iterations: 10_000);

    private sealed class FakeTime : IClockSource
    {
        public DateTimeOffset Wall { get; set; }
        public long Mono { get; set; }
        public TimeSpan Offset { get; set; }
        public DateTimeOffset GetWallUtc() => Wall;
        public long GetMonotonicMilliseconds() => Mono;
        public TimeSpan GetUtcOffset() => Offset;

        public void Advance(TimeSpan by)
        {
            Wall = Wall.Add(by);
            Mono += (long)by.TotalMilliseconds;
        }
    }

    private sealed class FakeProcesses : IProcessQuery
    {
        public List<ProcessObservation> Current { get; set; } = [];
        public IReadOnlyList<ProcessObservation> Query() => Current;
    }

    private sealed class FakeTerminator : IProcessTerminator
    {
        public List<int> Killed { get; } = [];
        public void Terminate(int processId) => Killed.Add(processId);
    }
}
