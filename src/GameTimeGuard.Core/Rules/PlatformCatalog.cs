namespace GameTimeGuard.Core.Rules;

public sealed class PlatformCatalog
{
    private PlatformCatalog(IReadOnlyList<PlatformRule> rules)
    {
        Rules = rules;
    }

    public IReadOnlyList<PlatformRule> Rules { get; }

    public IEnumerable<PlatformRule> Enabled(IEnumerable<string> enabledIds)
    {
        var wanted = new HashSet<string>(enabledIds, StringComparer.OrdinalIgnoreCase);
        return Rules.Where(rule => wanted.Contains(rule.Id));
    }

    public static PlatformCatalog LoadJson(string json)
    {
        var file = GuardJson.Deserialize<PlatformRuleFile>(json) ?? throw new InvalidDataException("平台规则文件是空的");
        if (file.Rules.Count == 0)
            throw new InvalidDataException("平台规则文件里没有任何规则");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in file.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                throw new InvalidDataException("有规则缺少 id");
            if (!seen.Add(rule.Id))
                throw new InvalidDataException($"规则 id 重复：{rule.Id}");
            if (string.IsNullOrWhiteSpace(rule.DisplayName))
                throw new InvalidDataException($"规则 {rule.Id} 缺少显示名称");
        }

        return new PlatformCatalog(file.Rules);
    }

    public static PlatformCatalog LoadEmbedded()
    {
        using var stream = typeof(PlatformCatalog).Assembly.GetManifestResourceStream("GameTimeGuard.Core.platforms.json")
            ?? throw new InvalidDataException("程序内没有内置平台规则");
        using var reader = new StreamReader(stream);
        return LoadJson(reader.ReadToEnd());
    }
}
