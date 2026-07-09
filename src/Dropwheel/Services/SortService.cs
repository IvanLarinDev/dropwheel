using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Distributes files according to a sorter target's rules.</summary>
public static class SortService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Returns a plan: destination folder → files. Uses the rich Rules engine when
    /// the target has Rules, otherwise the legacy SortRules. With no match and no catch-all
    /// a file goes to the target root (t.Path).</summary>
    public static Dictionary<string, List<string>> Plan(TargetItem t, IEnumerable<string> files)
    {
        bool useV2 = t.Rules is { Count: > 0 };
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var folder = useV2 ? ResolveFolderV2(t, f) : ResolveFolder(t, f);
            if (!map.TryGetValue(folder, out var list)) map[folder] = list = new();
            list.Add(f);
        }
        return map;
    }

    public static Dictionary<string, string[]> MovePlan(TargetItem t, IEnumerable<string> files)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, group) in Plan(t, files))
        {
            var moving = group.Where(file => !SameFolder(folder, file)).ToArray();
            if (moving.Length > 0) map[folder] = moving;
        }
        return map;
    }

    /// <summary>The destination folder is the file's own folder. Then there is nothing to move and,
    /// more importantly, moving into the same folder could make a watched sorter loop.</summary>
    public static bool SameFolder(string destFolder, string file)
    {
        var src = Path.GetDirectoryName(Path.GetFullPath(file));
        if (src == null) return false;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(destFolder)),
            Path.TrimEndingDirectorySeparator(src),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rules engine: first rule whose conditions all match wins. An empty condition
    /// list is a catch-all. No match → target root.</summary>
    private static string ResolveFolderV2(TargetItem t, string file)
    {
        int idx = MatchedRuleIndex(t.Rules!, file);
        if (idx < 0) return t.Path;
        return ExpandDest(t.Rules![idx], Path.GetFileName(file), t.Path);
    }

    /// <summary>Index of the first rule that fully matches the file, or -1 for no match (file goes
    /// to the sorter root). Shared by the real router and the editor preview so both agree exactly
    /// on which rule catches a file — even when several rules share the same destination.</summary>
    public static int MatchedRuleIndex(IReadOnlyList<SortRule> rules, string file)
    {
        var meta = FileMeta.Read(file);
        for (int i = 0; i < rules.Count; i++)
            if (rules[i].All.All(c => Match(c, meta)))
                return i;
        return -1;
    }

    /// <summary>Legacy resolver: match by file extension, "*" is the fallback.</summary>
    private static string ResolveFolder(TargetItem t, string file)
    {
        var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        string? dest = null, fallback = null;
        foreach (var (key, value) in t.SortRules!)
        {
            if (key.Trim() == "*") { fallback = value; continue; }
            var exts = key.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Select(x => x.TrimStart('.').ToLowerInvariant());
            if (ext.Length > 0 && exts.Contains(ext)) { dest = value; break; }
        }
        dest ??= fallback;
        return dest == null ? t.Path : Combine(t.Path, dest);
    }

    /// <summary>Resolves a rule Dest against the sorter root: empty → root, absolute → verbatim,
    /// otherwise relative to root.</summary>
    private static string Combine(string root, string dest) =>
        string.IsNullOrWhiteSpace(dest) ? root
        : Path.IsPathRooted(dest) ? dest : Path.Combine(root, dest);

    /// <summary>Matches a ${name} placeholder inside a Dest template.</summary>
    private static readonly Regex TokenRx = new(@"\$\{(\w+)\}", RegexOptions.Compiled);

    /// <summary>Resolves the folder for a matched file, expanding ${name} placeholders in the rule
    /// Dest from its NameRegex groups. When any placeholder cannot be filled the file goes to the
    /// sorter root instead of a half-built path.</summary>
    private static string ExpandDest(SortRule rule, string fileName, string root)
    {
        if (!rule.Dest.Contains("${", StringComparison.Ordinal)) return Combine(root, rule.Dest);
        var expanded = ExpandTemplate(rule, fileName, out bool ok);
        return ok ? Combine(root, expanded) : root;
    }

    /// <summary>Substitutes ${name} tokens in the rule Dest with sanitized values captured by the
    /// rule's NameRegex conditions against the file name. Sets ok=false when a token has no matching
    /// group or the captured value is empty after sanitizing. Shared by the router and the editor
    /// preview so both show the same resolved path.</summary>
    public static string ExpandTemplate(SortRule rule, string fileName, out bool ok)
    {
        var groups = CollectGroups(rule, fileName);
        bool allResolved = true;
        var result = TokenRx.Replace(rule.Dest, m =>
        {
            if (groups.TryGetValue(m.Groups[1].Value, out var raw))
            {
                var clean = SanitizeSegment(raw);
                if (clean.Length > 0) return clean;
            }
            allResolved = false;
            return m.Value;
        });
        ok = allResolved;
        return result;
    }

    /// <summary>Named-group captures from every NameRegex condition of the rule, matched against the
    /// file name. Numeric group names are skipped; a later condition wins on a name clash.</summary>
    private static Dictionary<string, string> CollectGroups(SortRule rule, string fileName)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in rule.All)
        {
            if (c.Field != ConditionField.NameRegex || Compiled(c.Value) is not { } rx) continue;
            if (!TryMatch(rx, fileName, c.Value, out var m)) continue;
            if (!m.Success) continue;
            foreach (var name in rx.GetGroupNames())
            {
                if (int.TryParse(name, out _)) continue;
                var g = m.Groups[name];
                if (g.Success) map[name] = g.Value;
            }
        }
        return map;
    }

    /// <summary>The distinct ${name} tokens a Dest template references.</summary>
    public static IReadOnlyList<string> TokensIn(string dest) =>
        TokenRx.Matches(dest).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The named groups a rule's NameRegex conditions expose for token substitution. Used by
    /// the editor to hint available tokens and to block a Dest that references a missing one.</summary>
    public static IReadOnlyCollection<string> AvailableTokens(SortRule rule)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in rule.All)
        {
            if (c.Field != ConditionField.NameRegex || Compiled(c.Value) is not { } rx) continue;
            foreach (var name in rx.GetGroupNames())
                if (!int.TryParse(name, out _)) set.Add(name);
        }
        return set;
    }

    /// <summary>Strips characters illegal in a folder name (plus trailing dots and spaces that
    /// Windows forbids) from a captured token so it can serve as a path segment.</summary>
    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Where(ch => Array.IndexOf(invalid, ch) < 0).ToArray();
        return new string(chars).Trim().TrimEnd('.', ' ');
    }

    private static bool Match(RuleCondition c, FileMeta meta) => c.Field switch
    {
        ConditionField.Extension => MatchExtension(c.Value, meta.Ext),
        ConditionField.NameContains => meta.Name.Contains(c.Value, StringComparison.OrdinalIgnoreCase),
        ConditionField.NameRegex => Compiled(c.Value) is { } rx && IsMatch(rx, meta.Name, c.Value),
        ConditionField.SizeMb => MatchNumber(c.Op, meta.SizeMb, c.Value),
        ConditionField.AgeDays => MatchNumber(c.Op, meta.AgeDays, c.Value),
        _ => false,
    };

    private static readonly char[] ExtSeparators = { ' ', ',', ';' };

    private static bool MatchExtension(string value, string ext)
    {
        if (ext.Length == 0) return false;
        var exts = value.Split(ExtSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(x => x.TrimStart('.').ToLowerInvariant());
        return exts.Contains(ext);
    }

    private static bool MatchNumber(CompareOp op, double actual, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
            return false;
        return op switch
        {
            CompareOp.Gt => actual > target,
            CompareOp.Lt => actual < target,
            CompareOp.Gte => actual >= target,
            CompareOp.Lte => actual <= target,
            _ => false,
        };
    }

    /// <summary>Regex cache keyed by pattern. Thread-safe: the folder watcher calls Plan from
    /// background threads, so this must not assume the UI thread. A race may compile the same pattern
    /// twice, but the results are identical, so last-write-wins is harmless.</summary>
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();

    /// <summary>Compiled regex for the pattern, or null when the pattern is invalid. A broken
    /// pattern (possible in a hand-edited config) must not crash a drop, so it is cached as null
    /// and treated as "never matches"; the rule with it simply lets the file fall through.</summary>
    private static Regex? Compiled(string pattern)
    {
        if (RegexCache.TryGetValue(pattern, out var rx)) return rx;
        try
        {
            rx = new Regex(pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }
        catch (ArgumentException ex) // invalid pattern (RegexParseException derives from this)
        {
            ErrorLog.Write($"Invalid regular expression in rule: '{pattern}'", ex);
            rx = null;
        }
        RegexCache[pattern] = rx;
        return rx;
    }

    private static bool IsMatch(Regex rx, string input, string pattern)
    {
        try { return rx.IsMatch(input); }
        catch (RegexMatchTimeoutException ex)
        {
            ErrorLog.Write($"Regular expression timed out in rule: '{pattern}'", ex);
            return false;
        }
    }

    private static bool TryMatch(Regex rx, string input, string pattern, out System.Text.RegularExpressions.Match match)
    {
        try
        {
            match = rx.Match(input);
            return true;
        }
        catch (RegexMatchTimeoutException ex)
        {
            ErrorLog.Write($"Regular expression timed out in rule: '{pattern}'", ex);
            match = System.Text.RegularExpressions.Match.Empty;
            return false;
        }
    }
}
