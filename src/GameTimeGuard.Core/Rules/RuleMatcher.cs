using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.Rules;

public static class RuleMatcher
{
    public static bool IsMatch(PlatformRule rule, ProcessObservation process)
    {
        var path = WindowsPath.Normalize(process.ExecutablePath);
        if (path is null || WindowsPath.IsUnsafe(path))
            return false;

        if (ContainsAny(path, rule.PathNotContainsAny))
            return false;

        if (ContainsAny(path, rule.PathContainsAny))
            return true;

        if (!rule.PublisherAlone || rule.Publishers.Count == 0 || string.IsNullOrWhiteSpace(process.Publisher))
            return false;

        if (!ContainsAny(process.Publisher, rule.Publishers))
            return false;

        if (ContainsAny(path, rule.PublisherPathNotContainsAny))
            return false;

        return true;
    }

    public static bool CustomMatches(CustomGameRule custom, string? executablePath)
    {
        var full = WindowsPath.Normalize(executablePath);
        var target = WindowsPath.Normalize(custom.Path);
        if (full is null || target is null || WindowsPath.IsUnsafe(full) || WindowsPath.IsUnsafe(target))
            return false;
        if (!WindowsPath.IsAbsolute(target))
            return false;

        if (custom.Kind.Equals("exe", StringComparison.OrdinalIgnoreCase))
            return full.Equals(target, StringComparison.OrdinalIgnoreCase);

        if (!custom.Kind.Equals("folder", StringComparison.OrdinalIgnoreCase))
            return false;

        var folder = target.TrimEnd('\\');
        return full.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresPublisherLookup(IEnumerable<PlatformRule> rules, string? path)
    {
        var normalized = WindowsPath.Normalize(path);
        if (normalized is null || WindowsPath.IsUnsafe(normalized))
            return false;

        var matchedByPath = false;
        var couldUsePublisher = false;
        foreach (var rule in rules)
        {
            if (ContainsAny(normalized, rule.PathNotContainsAny))
                continue;
            if (ContainsAny(normalized, rule.PathContainsAny))
            {
                matchedByPath = true;
                continue;
            }

            if (rule.PublisherAlone &&
                rule.Publishers.Count > 0 &&
                !ContainsAny(normalized, rule.PublisherPathNotContainsAny))
            {
                couldUsePublisher = true;
            }
        }

        return couldUsePublisher && !matchedByPath;
    }

    public static IReadOnlyList<ProcessMatch> MatchAll(
        IEnumerable<ProcessObservation> processes,
        IEnumerable<PlatformRule> enabledRules,
        IEnumerable<CustomGameRule> customGames)
    {
        var rules = enabledRules.ToArray();
        var customs = customGames.ToArray();
        var matches = new List<ProcessMatch>();
        foreach (var process in processes)
        {
            if (WindowsPath.IsUnsafe(process.ExecutablePath))
                continue;

            var reasons = new List<string>();
            foreach (var rule in rules)
            {
                if (IsMatch(rule, process))
                    reasons.Add(rule.Id);
            }

            foreach (var custom in customs)
            {
                if (CustomMatches(custom, process.ExecutablePath))
                    reasons.Add("custom:" + custom.Id);
            }

            if (reasons.Count > 0)
                matches.Add(new ProcessMatch(process, reasons));
        }

        return matches;
    }

    private static bool ContainsAny(string haystack, IEnumerable<string> needles)
    {
        foreach (var needle in needles)
        {
            if (string.IsNullOrWhiteSpace(needle))
                continue;
            if (haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
