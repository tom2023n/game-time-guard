namespace GameTimeGuard.Core.Security;

public enum StoreStatus
{
    Unavailable,
    Missing,
    Authentic,
    Tampered
}

public sealed record StoreRead(StoreStatus Status, string? Payload, long Sequence, bool Restored)
{
    public static StoreRead Unavailable() => new(StoreStatus.Unavailable, null, 0, false);
    public static StoreRead Missing() => new(StoreStatus.Missing, null, 0, false);
    public static StoreRead Authentic(string payload, long sequence, bool restored) => new(StoreStatus.Authentic, payload, sequence, restored);
    public static StoreRead Tampered() => new(StoreStatus.Tampered, null, 0, false);
}

public static class RedundantStore
{
    public static StoreRead Read(IBlobStore primary, IBlobStore backup, string name, byte[] key, string expectedKind)
    {
        var rawA = primary.Read(name);
        var rawB = backup.Read(name);
        if (!rawA.Available && !rawB.Available)
            return StoreRead.Unavailable();

        var a = rawA.Available ? Verify(rawA.Content, key, expectedKind) : null;
        var b = rawB.Available ? Verify(rawB.Content, key, expectedKind) : null;
        var aGarbage = rawA.Available && rawA.Content is not null && a is null;
        var bGarbage = rawB.Available && rawB.Content is not null && b is null;

        if (a is null && b is null)
            return aGarbage || bGarbage ? StoreRead.Tampered() : StoreRead.Missing();

        if (a is not null && b is not null && a.Value.Sequence == b.Value.Sequence && a.Value.Payload != b.Value.Payload)
            return StoreRead.Tampered();

        var winner = a;
        if (winner is null || (b is not null && b.Value.Sequence > winner.Value.Sequence))
            winner = b;

        var restored = false;
        if (rawA.Available && (a is null || a.Value.Raw != winner!.Value.Raw))
        {
            if (primary.TryWrite(name, winner!.Value.Raw, out _))
                restored = true;
        }

        if (rawB.Available && (b is null || b.Value.Raw != winner!.Value.Raw))
        {
            if (backup.TryWrite(name, winner!.Value.Raw, out _))
                restored = true;
        }

        return StoreRead.Authentic(winner!.Value.Payload, winner.Value.Sequence, restored);
    }

    public static bool WriteBoth(IBlobStore primary, IBlobStore backup, string name, string envelope, out string? error)
    {
        var primaryOk = primary.TryWrite(name, envelope, out var primaryError);
        var backupOk = backup.TryWrite(name, envelope, out var backupError);
        if (primaryOk || backupOk)
        {
            error = primaryOk && backupOk ? null : primaryError ?? backupError;
            return true;
        }

        error = primaryError ?? backupError ?? "两份记录都写失败了";
        return false;
    }

    private static Verified? Verify(string? raw, byte[] key, string expectedKind)
    {
        if (raw is null)
            return null;
        if (!Integrity.TryOpen(raw, key, expectedKind, out var payload, out var sequence))
            return null;
        return new Verified(sequence, payload, raw);
    }

    private readonly record struct Verified(long Sequence, string Payload, string Raw);
}
