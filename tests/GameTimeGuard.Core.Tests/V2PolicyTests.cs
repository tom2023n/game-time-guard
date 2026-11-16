using GameTimeGuard.Core.Engine;
using GameTimeGuard.Core.State;
using GameTimeGuard.Core.Quota;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;

namespace GameTimeGuard.Core.Tests;

public class V2PolicyTests
{
    [Fact]
    public void Three_copies_keep_the_highest_sequence_and_restore_the_rest()
    {
        var key = KeyRing.Create();
        var stores = new[] { new MemoryBlobStore(), new MemoryBlobStore(), new MemoryBlobStore() };
        var older = Integrity.Seal("usage", StateCodec.SerializeUsage(UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)) with { Sequence = 2 }, null), key);
        var newer = Integrity.Seal("usage", StateCodec.SerializeUsage(UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)) with { Sequence = 9, UsedMilliseconds = 5000 }, null), key);
        stores[0].Data["usage.json"] = older;
        stores[2].Data["usage.json"] = newer;

        var read = RedundantStore.ReadMany(stores, "usage.json", key, "usage");
        Assert.Equal(StoreStatus.Authentic, read.Status);
        Assert.Equal(9, read.Sequence);
        Assert.True(read.Restored);
        Assert.All(stores, store => Assert.Equal(newer, store.Data["usage.json"]));
    }

    [Fact]
    public void Tamper_log_appends_and_acknowledgement_clears_alerts()
    {
        var log = TamperLog.Append(null, TamperKinds.ClockChange, "时钟被往回拨", DateTimeOffset.Parse("2026-11-16T12:00:00Z"), alert: true);
        log = TamperLog.Append(log, TamperKinds.ProcessKill, "关闭了游戏", DateTimeOffset.Parse("2026-11-16T12:05:00Z"), alert: false);
        Assert.Equal(2, log.Sequence);
        Assert.Single(TamperLog.Pending(log));

        var acked = TamperLog.AcknowledgeAll(log);
        Assert.Empty(TamperLog.Pending(acked));
        Assert.Equal(3, acked.Sequence);
        Assert.Equal(2, acked.Events.Count);
    }

    [Fact]
    public void Pause_expires_and_only_the_password_flow_changes_protection()
    {
        var config = DefaultPolicy.Uninitialized();
        config.SetupComplete = true;
        var start = DateTimeOffset.Parse("2026-11-16T12:00:00Z");
        Assert.Null(ProtectionControl.Pause(config, start, 30));
        Assert.True(ProtectionControl.IsIdle(config, start.AddMinutes(29)));
        var resumed = ProtectionControl.Normalize(config, start.AddMinutes(30));
        Assert.Equal(ProtectionControl.Enforcing, resumed.ProtectionState);
        Assert.False(ProtectionControl.IsIdle(resumed, start.AddMinutes(30)));
        Assert.Contains("只能暂停", ProtectionControl.Pause(config, start, 10));
    }

    [Fact]
    public void Gap_charges_observed_play_and_does_not_charge_a_parent_pause()
    {
        var start = DateTimeOffset.Parse("2026-11-16T10:00:00Z");
        var end = start.AddHours(1);
        var observed = GapBackfill.Estimate(new GapRequest(start, end, true, null,
            [new ProcessSighting("game", start.AddMinutes(20), true)], "penalty", 30));
        Assert.Equal(GapChargeKind.Observed, observed.Kind);
        Assert.Equal(40 * 60, observed.ChargeSeconds);

        var paused = GapBackfill.Estimate(new GapRequest(start, end, true, end,
            [new ProcessSighting("game", start, true)], "full", 30));
        Assert.Equal(0, paused.ChargeSeconds);

        var uncertain = GapBackfill.Estimate(new GapRequest(start, end, true, null, [], "penalty", 30));
        Assert.Equal(GapChargeKind.Penalty, uncertain.Kind);
        Assert.Equal(30 * 60, uncertain.ChargeSeconds);

        var full = GapBackfill.Estimate(new GapRequest(start, end, true, null, [], "full", 30));
        Assert.Equal(GapChargeKind.FullGap, full.Kind);
        Assert.Equal(60 * 60, full.ChargeSeconds);

        var loggedOff = GapBackfill.Estimate(new GapRequest(start, end, false, null, [], "full", 30));
        Assert.Equal(0, loggedOff.ChargeSeconds);
    }

    [Fact]
    public void Mail_stays_off_until_a_parent_turns_it_on()
    {
        var config = DefaultPolicy.Uninitialized();
        Assert.False(config.EmailEnabled);
        Assert.Null(MailNotices.ForTamper(config, "异常"));

        config.EmailEnabled = true;
        config.SmtpHost = "smtp.example.com";
        config.NotifyAddress = "parent@example.com";
        config.NotifyTamper = true;
        var draft = MailNotices.ForTamper(config, "时钟被改过");
        Assert.Equal("parent@example.com", draft!.To);
        Assert.Contains("时钟", draft.Body);
        Assert.Null(MailNotices.Validate(config));
    }
}
