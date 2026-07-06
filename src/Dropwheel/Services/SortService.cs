using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Distributes files according to a sorter target's rules.</summary>
public static class SortService
{
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

    /// <summary>Rules engine: first rule whose conditions all match wins. An empty condition
    /// list is a catch-all. No match → target root.</summary>
    private static string ResolveFolderV2(TargetItem t, string file)
    {
        var meta = FileMeta.Read(file);
        foreach (var rule in t.Rules!)
            if (rule.All.All(c => Match(c, meta)))
                return Combine(t.Path, rule.Dest);
        return t.Path;
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

    private static bool Match(RuleCondition c, FileMeta meta) => c.Field switch
    {
        ConditionField.Extension => MatchExtension(c.Value, meta.Ext),
        ConditionField.NameContains => meta.Name.Contains(c.Value, StringComparison.OrdinalIgnoreCase),
        ConditionField.NameRegex => Compiled(c.Value).IsMatch(meta.Name),
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

    /// <summary>Regex cache keyed by pattern. Assumes single-threaded use (UI thread); switch to
    /// a concurrent map when the watch feature calls Plan off-thread.</summary>
    private static readonly Dictionary<string, Regex> RegexCache = new();

    private static Regex Compiled(string pattern)
    {
        if (!RegexCache.TryGetValue(pattern, out var rx))
            RegexCache[pattern] = rx = new Regex(pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return rx;
    }
}
