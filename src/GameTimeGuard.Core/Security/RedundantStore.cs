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
        => ReadMany([primary, backup], name, key, expectedKind);

    public static StoreRead ReadMany(IReadOnlyList<IBlobStore> stores, string name, byte[] key, string expectedKind)
    {
        if (stores.Count == 0)
            return StoreRead.Unavailable();

        var raw = stores.Select(store => store.Read(name)).ToArray();
        if (raw.All(item => !item.Available))
            return StoreRead.Unavailable();

        var verified = raw.Select(item => item.Available ? Verify(item.Content, key, expectedKind) : null).ToArray();
        var garbage = false;
        Verified? winner = null;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i].Available && raw[i].Content is not null && verified[i] is null)
                garbage = true;
            var candidate = verified[i];
            if (candidate is null)
                continue;
            if (winner is not null && candidate.Value.Sequence == winner.Value.Sequence && candidate.Value.Payload != winner.Value.Payload)
                return StoreRead.Tampered();
            if (winner is null || candidate.Value.Sequence > winner.Value.Sequence)
                winner = candidate;
        }

        if (winner is null)
            return garbage ? StoreRead.Tampered() : StoreRead.Missing();

        var restored = false;
        for (var i = 0; i < stores.Count; i++)
        {
            if (!raw[i].Available || verified[i]?.Raw == winner.Value.Raw)
                continue;
            if (stores[i].TryWrite(name, winner.Value.Raw, out _))
                restored = true;
        }

        return StoreRead.Authentic(winner.Value.Payload, winner.Value.Sequence, restored);
    }

    public static bool WriteBoth(IBlobStore primary, IBlobStore backup, string name, string envelope, out string? error)
        => WriteMany([primary, backup], name, envelope, out error);

    public static bool WriteMany(IReadOnlyList<IBlobStore> stores, string name, string envelope, out string? error)
    {
        var wrote = false;
        string? lastError = null;
        foreach (var store in stores)
        {
            if (store.TryWrite(name, envelope, out var storeError))
                wrote = true;
            else
                lastError = storeError;
        }

        error = wrote ? null : lastError ?? "记录都写失败了";
        return wrote;
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
