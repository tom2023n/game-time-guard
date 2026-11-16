using GameTimeGuard.Core;
using GameTimeGuard.Core.Ipc;
using GameTimeGuard.Core.Quota;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;
using GameTimeGuard.Core.State;

namespace GameTimeGuard.Core.Tests;

public class SecurityTests
{
    [Fact]
    public void Password_hash_verifies_and_rejects_the_wrong_password()
    {
        var hash = ParentPassword.Hash("parent-secret", iterations: 10_000);
        Assert.Equal("PBKDF2-SHA256", hash.Algorithm);
        Assert.True(ParentPassword.Verify("parent-secret", hash));
        Assert.False(ParentPassword.Verify("parent-secreT", hash));
        Assert.False(ParentPassword.Verify("", hash));
        Assert.False(ParentPassword.Verify("parent-secret", null));
        Assert.NotEqual(hash.Salt, ParentPassword.Hash("parent-secret", iterations: 10_000).Salt);
        Assert.Equal(600_000, ParentPassword.DefaultIterations);
        Assert.Throws<ArgumentException>(() => ParentPassword.Hash("abc"));
    }

    [Fact]
    public void Signature_covers_payload_and_sequence()
    {
        var key = KeyRing.Create();
        var payload = StateCodec.SerializeUsage(UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)) with { Sequence = 3, UsedMilliseconds = 1500 }, null);
        var envelope = Integrity.Seal("usage", payload, key);

        Assert.True(Integrity.TryOpen(envelope, key, "usage", out var opened, out var sequence));
        Assert.Equal(3, sequence);
        Assert.Equal(payload, opened);
        Assert.False(Integrity.TryOpen(envelope, key, "config", out _, out _));
        Assert.False(Integrity.TryOpen(envelope, KeyRing.Create(), "usage", out _, out _));

        var other = StateCodec.SerializeUsage(UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)) with { Sequence = 4, UsedMilliseconds = 1500 }, null);
        var otherEnvelope = Integrity.Seal("usage", other, key);
        Assert.NotEqual(envelope, otherEnvelope);
    }

    [Fact]
    public void Changed_payload_fails_verification()
    {
        var key = KeyRing.Create();
        var payload = StateCodec.SerializeConfig(ConfigWithSequence(7));
        var envelope = Integrity.Seal("config", payload, key);
        var signed = GuardJson.Deserialize<SignedEnvelope>(envelope)!;
        signed.Payload = signed.Payload.Replace("\"graceSeconds\":30", "\"graceSeconds\":999", StringComparison.Ordinal);
        var tampered = GuardJson.Serialize(signed);
        Assert.NotEqual(envelope, tampered);
        Assert.False(Integrity.TryOpen(tampered, key, "config", out _, out _));
    }

    [Fact]
    public void Higher_sequence_wins_and_a_deleted_copy_is_restored()
    {
        var key = KeyRing.Create();
        var primary = new MemoryBlobStore();
        var backup = new MemoryBlobStore();
        var older = Integrity.Seal("usage", StateCodec.SerializeUsage(UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)) with { Sequence = 2, UsedMilliseconds = 1000 }, null), key);
        var newer = Integrity.Seal("usage", StateCodec.SerializeUsage(UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)) with { Sequence = 5, UsedMilliseconds = 9000 }, null), key);
        primary.Data["usage.json"] = older;
        backup.Data["usage.json"] = newer;

        var read = RedundantStore.Read(primary, backup, "usage.json", key, "usage");
        Assert.Equal(StoreStatus.Authentic, read.Status);
        Assert.Equal(5, read.Sequence);
        Assert.True(read.Restored);
        Assert.Equal(newer, primary.Data["usage.json"]);

        primary.Data.Remove("usage.json");
        var restored = RedundantStore.Read(primary, backup, "usage.json", key, "usage");
        Assert.Equal(StoreStatus.Authentic, restored.Status);
        Assert.Equal(newer, primary.Data["usage.json"]);
    }

    [Fact]
    public void Invalid_records_are_tamper_and_a_missing_pair_is_not()
    {
        var key = KeyRing.Create();
        var primary = new MemoryBlobStore();
        var backup = new MemoryBlobStore();
        Assert.Equal(StoreStatus.Missing, RedundantStore.Read(primary, backup, "config.json", key, "config").Status);

        primary.Data["config.json"] = "not-json";
        backup.Data["config.json"] = "also-bad";
        Assert.Equal(StoreStatus.Tampered, RedundantStore.Read(primary, backup, "config.json", key, "config").Status);

        backup.Available = false;
        primary.Available = false;
        Assert.Equal(StoreStatus.Unavailable, RedundantStore.Read(primary, backup, "config.json", key, "config").Status);
    }

    [Fact]
    public void Key_ring_creates_once_and_restores_the_missing_side()
    {
        var primary = new MemoryBlobStore();
        var backup = new MemoryBlobStore();
        var created = KeyRing.Resolve(primary, backup, _ => false);
        Assert.True(created.Created);
        Assert.NotNull(created.Key);

        backup.Data.Remove(KeyRing.Name);
        var restored = KeyRing.Resolve(primary, backup, _ => false);
        Assert.True(restored.Restored);
        Assert.Equal(created.Key, restored.Key);
        Assert.True(backup.Data.ContainsKey(KeyRing.Name));
    }

    [Fact]
    public void Key_conflict_prefers_the_key_that_verifies_data()
    {
        var primary = new MemoryBlobStore();
        var backup = new MemoryBlobStore();
        var good = KeyRing.Create();
        var bad = KeyRing.Create();
        primary.Data[KeyRing.Name] = Convert.ToBase64String(bad);
        backup.Data[KeyRing.Name] = Convert.ToBase64String(good);

        var resolved = KeyRing.Resolve(primary, backup, key => key.SequenceEqual(good));
        Assert.True(resolved.Conflict);
        Assert.Equal(good, resolved.Key);
        Assert.Equal(Convert.ToBase64String(good), primary.Data[KeyRing.Name]);
    }

    [Fact]
    public void Startup_locks_when_records_disappear_after_setup()
    {
        var first = StartupRecovery.Resolve(StoreRead.Missing(), StoreRead.Missing(), hmacKeyExisted: false);
        Assert.True(first.FirstRun);
        Assert.False(first.TamperLocked);

        var wiped = StartupRecovery.Resolve(StoreRead.Missing(), StoreRead.Missing(), hmacKeyExisted: true);
        Assert.True(wiped.TamperLocked);
        Assert.False(wiped.FirstRun);

        var configRead = StoreRead.Authentic(StateCodec.SerializeConfig(ConfigWithSequence(4, lastUsage: 6)), 4, false);
        var deletedUsage = StartupRecovery.Resolve(configRead, StoreRead.Missing(), hmacKeyExisted: true);
        Assert.True(deletedUsage.TamperLocked);

        var rolledUsage = StoreRead.Authentic(StateCodec.SerializeUsage(UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)) with { Sequence = 2, UsedMilliseconds = 1000 }, null), 2, false);
        var rollback = StartupRecovery.Resolve(configRead, rolledUsage, hmacKeyExisted: true);
        Assert.True(rollback.TamperLocked);
        Assert.Equal(0, rollback.Usage.UsedMilliseconds);
    }

    [Fact]
    public void Usage_document_round_trips_with_the_clock_anchor()
    {
        var usage = UsageSnapshot.Fresh(new DateOnly(2026, 11, 16)) with
        {
            Sequence = 8,
            UsedMilliseconds = 4321,
            BonusSeconds = 900,
            Warned10 = true
        };
        var clock = new GameTimeGuard.Core.Clock.ClockSample
        {
            HasSample = true,
            TrustedUtc = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            WallUtc = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            MonotonicMilliseconds = 123456,
            StoredOffset = TimeSpan.FromHours(8),
            LogicalDate = new DateOnly(2026, 11, 16)
        };
        var json = StateCodec.SerializeUsage(usage, clock);
        Assert.True(StateCodec.TryParseUsage(json, out var parsed, out var parsedClock));
        Assert.Equal(usage, parsed);
        Assert.Equal(clock, parsedClock);
    }

    [Fact]
    public void Password_throttle_locks_after_repeated_failures()
    {
        var throttle = new PasswordThrottle(limit: 3, window: TimeSpan.FromMinutes(10), lockout: TimeSpan.FromMinutes(5));
        Assert.False(throttle.IsLocked(0));
        throttle.Fail(1_000);
        throttle.Fail(2_000);
        Assert.False(throttle.IsLocked(2_000));
        throttle.Fail(3_000);
        Assert.True(throttle.IsLocked(3_000));
        Assert.False(throttle.IsLocked(3_000 + (long)TimeSpan.FromMinutes(5).TotalMilliseconds));
    }

    [Fact]
    public void Ipc_messages_round_trip()
    {
        var request = new IpcRequest { Id = "7", Op = IpcOps.GrantBonus, Password = "secret", Minutes = 30 };
        Assert.True(IpcProtocol.TryRead(IpcProtocol.WriteRequest(request), out var parsed, out _, out _));
        Assert.Equal(IpcOps.GrantBonus, parsed!.Op);
        Assert.Equal(30, parsed.Minutes);

        var evt = new IpcEvent { Name = "grace", Reason = ReasonCodes.Quota, Seconds = 30, Message = "即将关闭" };
        Assert.True(IpcProtocol.TryRead(IpcProtocol.WriteEvent(evt), out _, out _, out var parsedEvent));
        Assert.Equal("grace", parsedEvent!.Name);
    }

    private static GuardConfig ConfigWithSequence(long sequence, long lastUsage = 0)
    {
        var config = DefaultPolicy.Uninitialized();
        config.SetupComplete = true;
        config.Sequence = sequence;
        config.LastUsageSequence = lastUsage;
        config.ParentPassword = ParentPassword.Hash("parent-secret", iterations: 10_000);
        return config;
    }
}
