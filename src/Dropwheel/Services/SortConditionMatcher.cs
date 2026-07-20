using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Condition evaluation and bounded regex cache for sorter rules.</summary>
internal static class SortConditionMatcher
{
    internal static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    internal static readonly IReadOnlyList<string> MediaKinds =
        new[] { "image", "video", "audio", "document", "archive" };

    private static readonly Dictionary<string, string[]> MediaKindExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = new[] { "jpg", "jpeg", "png", "gif", "webp", "bmp", "tiff", "tif", "heic", "heif", "svg", "ico", "raw", "cr2", "nef", "arw", "dng" },
        ["video"] = new[] { "mp4", "mkv", "mov", "avi", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "3gp", "ts", "m2ts" },
        ["audio"] = new[] { "mp3", "wav", "flac", "aac", "ogg", "oga", "m4a", "wma", "opus", "aiff", "aif", "alac" },
        ["document"] = new[] { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "rtf", "txt", "md", "csv", "epub", "pages", "numbers", "key" },
        ["archive"] = new[] { "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "zst", "tgz", "iso", "cab", "lz", "lzma" },
    };
    private static readonly char[] ExtSeparators = { ' ', ',', ';' };
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();

    internal static int MatchedRuleIndex(IReadOnlyList<SortRule> rules, FileMeta meta)
    {
        for (var i = 0; i < rules.Count; i++)
            if (rules[i].Enabled && ScopeIncludes(rules[i].Scope, meta.IsDirectory)
                && rules[i].All.All(condition => Match(condition, meta)))
                return i;
        return -1;
    }

    internal static bool ScopeIncludes(RuleScope scope, bool isDirectory) => scope switch
    {
        RuleScope.Files => !isDirectory,
        RuleScope.Folders => isDirectory,
        RuleScope.Both => true,
        _ => !isDirectory,
    };

    internal static Regex? Compiled(string pattern)
    {
        if (RegexCache.TryGetValue(pattern, out var regex)) return regex;
        try
        {
            regex = new Regex(pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            ErrorLog.Write($"Invalid regular expression in rule: '{pattern}'", ex);
            regex = null;
        }
        RegexCache[pattern] = regex;
        return regex;
    }

    internal static bool TryMatch(Regex regex, string input, string pattern, out Match match)
    {
        try { match = regex.Match(input); return true; }
        catch (RegexMatchTimeoutException ex)
        {
            ErrorLog.Write($"Regular expression timed out in rule: '{pattern}'", ex);
            match = System.Text.RegularExpressions.Match.Empty;
            return false;
        }
    }

    private static bool Match(RuleCondition condition, FileMeta meta)
    {
        var hit = condition.Field switch
        {
            ConditionField.Extension => MatchExtension(condition.Value, meta.Ext),
            ConditionField.NameContains => meta.Name.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            ConditionField.NameRegex => Compiled(condition.Value) is { } regex
                && IsMatch(regex, meta.Name, condition.Value),
            ConditionField.SizeMb => MatchNumber(condition.Op, meta.SizeMb, condition.Value),
            ConditionField.AgeDays => MatchNumber(condition.Op, meta.AgeDays, condition.Value),
            ConditionField.CreatedDaysAgo => MatchNumber(condition.Op, meta.CreationAgeDays, condition.Value),
            ConditionField.MediaKind => MatchMediaKind(condition.Value, meta.Ext),
            _ => false,
        };
        return condition.Negate ? !hit : hit;
    }

    private static bool MatchMediaKind(string kind, string extension) =>
        extension.Length > 0 && MediaKindExtensions.TryGetValue(kind, out var extensions)
        && Array.IndexOf(extensions, extension) >= 0;

    private static bool MatchExtension(string value, string extension)
    {
        if (extension.Length == 0) return false;
        return value.Split(ExtSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.TrimStart('.').ToLowerInvariant()).Contains(extension);
    }

    private static bool MatchNumber(CompareOp op, double actual, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)) return false;
        return op switch
        {
            CompareOp.Gt => actual > target,
            CompareOp.Lt => actual < target,
            CompareOp.Gte => actual >= target,
            CompareOp.Lte => actual <= target,
            _ => false,
        };
    }

    private static bool IsMatch(Regex regex, string input, string pattern)
    {
        try { return regex.IsMatch(input); }
        catch (RegexMatchTimeoutException ex)
        {
            ErrorLog.Write($"Regular expression timed out in rule: '{pattern}'", ex);
            return false;
        }
    }
}
