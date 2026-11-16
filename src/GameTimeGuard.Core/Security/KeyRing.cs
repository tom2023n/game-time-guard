using System.Security.Cryptography;

namespace GameTimeGuard.Core.Security;

public readonly record struct KeyResolution(byte[]? Key, bool Created, bool Restored, bool Conflict);

public static class KeyRing
{
    public const string Name = "hmac.key";

    public static byte[] Create() => RandomNumberGenerator.GetBytes(32);

    public static KeyResolution Resolve(IBlobStore primary, IBlobStore backup, Func<byte[], bool> isRecognized)
    {
        var a = TryRead(primary);
        var b = TryRead(backup);
        if (a.Unavailable && b.Unavailable)
            return new KeyResolution(null, false, false, false);

        if (a.Bytes is null && b.Bytes is null && a.Present == false && b.Present == false && (a.Unavailable || b.Unavailable))
            return new KeyResolution(null, false, false, false);

        if (a.Bytes is null && b.Bytes is null)
        {
            var created = Create();
            var text = Convert.ToBase64String(created);
            primary.TryWrite(Name, text, out _);
            backup.TryWrite(Name, text, out _);
            return new KeyResolution(created, true, false, false);
        }

        if (a.Bytes is null && b.Bytes is not null)
        {
            primary.TryWrite(Name, Convert.ToBase64String(b.Bytes), out _);
            return new KeyResolution(b.Bytes, false, true, false);
        }

        if (a.Bytes is not null && b.Bytes is null)
        {
            backup.TryWrite(Name, Convert.ToBase64String(a.Bytes), out _);
            return new KeyResolution(a.Bytes, false, true, false);
        }

        if (a.Bytes is not byte[] left || b.Bytes is not byte[] right)
            return new KeyResolution(a.Bytes ?? b.Bytes, false, false, true);

        if (left.AsSpan().SequenceEqual(right))
            return new KeyResolution(left, false, false, false);

        var aOk = isRecognized(left);
        var bOk = isRecognized(right);
        if (aOk && !bOk)
        {
            backup.TryWrite(Name, Convert.ToBase64String(left), out _);
            return new KeyResolution(left, false, true, true);
        }

        if (bOk && !aOk)
        {
            primary.TryWrite(Name, Convert.ToBase64String(right), out _);
            return new KeyResolution(right, false, true, true);
        }

        return new KeyResolution(left, false, false, true);
    }

    private static KeyRead TryRead(IBlobStore store)
    {
        var read = store.Read(Name);
        if (!read.Available)
            return new KeyRead(null, false, true);
        if (string.IsNullOrWhiteSpace(read.Content))
            return new KeyRead(null, false, false);
        try
        {
            var bytes = Convert.FromBase64String(read.Content.Trim());
            if (bytes.Length < 32)
                return new KeyRead(null, true, false);
            return new KeyRead(bytes, true, false);
        }
        catch (FormatException)
        {
            return new KeyRead(null, true, false);
        }
    }

    private readonly record struct KeyRead(byte[]? Bytes, bool Present, bool Unavailable);
}
