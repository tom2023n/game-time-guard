namespace GameTimeGuard.Core.Security;

public readonly record struct BlobRead(bool Available, string? Content)
{
    public static BlobRead Unavailable() => new(false, null);
    public static BlobRead Empty() => new(true, null);
    public static BlobRead Found(string content) => new(true, content);
}

public interface IBlobStore
{
    BlobRead Read(string name);
    bool TryWrite(string name, string content, out string? error);
}

public sealed class MemoryBlobStore : IBlobStore
{
    public bool Available { get; set; } = true;
    public Dictionary<string, string> Data { get; } = new(StringComparer.OrdinalIgnoreCase);

    public BlobRead Read(string name)
    {
        if (!Available)
            return BlobRead.Unavailable();
        return Data.TryGetValue(name, out var value) ? BlobRead.Found(value) : BlobRead.Empty();
    }

    public bool TryWrite(string name, string content, out string? error)
    {
        if (!Available)
        {
            error = "存储不可用";
            return false;
        }

        Data[name] = content;
        error = null;
        return true;
    }
}

public sealed class FileBlobStore : IBlobStore
{
    private readonly string _directory;

    public FileBlobStore(string directory)
    {
        _directory = directory;
    }

    public BlobRead Read(string name)
    {
        try
        {
            var path = Resolve(name);
            if (!File.Exists(path))
                return BlobRead.Empty();
            return BlobRead.Found(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BlobRead.Unavailable();
        }
    }

    public bool TryWrite(string name, string content, out string? error)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Resolve(name);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new ArgumentException("存储名称无效", nameof(name));
        return Path.Combine(_directory, name);
    }
}
