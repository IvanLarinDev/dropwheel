using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
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
        // One timestamp for the whole drop so every file in it shares the same ${date} folder,
        // even if the loop crosses a second or midnight boundary while running.
        var now = DateTime.Now;
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var folder = useV2 ? ResolveFolderV2(t, f, now) : ResolveFolder(t, f);
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
    /// list is a catch-all. No match → target root. For a folder two extra guards keep a watched
    /// sorter from eating its own output: a folder already sitting at a location that matches the
    /// rule's destination shape stays put, and a destination inside the folder itself is refused. A
    /// no-op is expressed by returning the item's own parent folder, which SameFolder then filters.</summary>
    private static string ResolveFolderV2(TargetItem t, string file, DateTime now)
    {
        var meta = FileMeta.Read(file);
        int idx = MatchedRuleIndex(t.Rules!, meta);
        if (idx < 0) return t.Path;
        var rule = t.Rules![idx];
        if (meta.IsDirectory && AlreadyPlaced(rule, file, t.Path)) return OwnFolder(file);
        var dest = ExpandDest(rule, file, t.Path, now);
        if (meta.IsDirectory && DestinationInsideSource(dest, file)) return OwnFolder(file);
        return dest;
    }

    /// <summary>The folder the item currently lives in. Returning it as a destination makes the move a
    /// no-op, since SameFolder then skips it.</summary>
    private static string OwnFolder(string file) =>
        Path.GetDirectoryName(Path.GetFullPath(file)) ?? file;

    /// <summary>Index of the first rule that fully matches the item, or -1 for no match (item goes
    /// to the sorter root). Shared by the real router and the editor preview so both agree exactly
    /// on which rule catches an item — even when several rules share the same destination.</summary>
    public static int MatchedRuleIndex(IReadOnlyList<SortRule> rules, string file) =>
        MatchedRuleIndex(rules, FileMeta.Read(file));

    /// <summary>Overload taking an already-read <see cref="FileMeta"/> so the router does not stat the
    /// item twice. A rule only catches an item whose kind (file or folder) its Scope allows.</summary>
    public static int MatchedRuleIndex(IReadOnlyList<SortRule> rules, FileMeta meta)
    {
        for (int i = 0; i < rules.Count; i++)
            if (ScopeIncludes(rules[i].Scope, meta.IsDirectory) && rules[i].All.All(c => Match(c, meta)))
                return i;
        return -1;
    }

    /// <summary>Whether a rule of the given scope may catch an item of the given kind.</summary>
    public static bool ScopeIncludes(RuleScope scope, bool isDirectory) => scope switch
    {
        RuleScope.Files => !isDirectory,
        RuleScope.Folders => isDirectory,
        RuleScope.Both => true,
        _ => !isDirectory,
    };

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

    /// <summary>Matches a ${name} or ${name:format} placeholder inside a Dest template. The optional
    /// format after the colon is a .NET format string used by the built-in date tokens.</summary>
    private static readonly Regex TokenRx = new(@"\$\{([A-Za-z][A-Za-z0-9]*)(?::([^}]*))?\}", RegexOptions.Compiled);

    /// <summary>Placeholder names the router fills itself, independent of any Name regex group:
    /// drop-time date and time (date, year, month, day, time, week, quarter), the file's own last-write
    /// date (the f-prefixed twins), the file's creation date (the c-prefixed twins), and file-name
    /// pieces (ext, stem, initial). Reserved — a Name regex group that happens to share one of these
    /// names is shadowed by the built-in.</summary>
    public static readonly IReadOnlyCollection<string> BuiltinTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "date", "year", "month", "day", "time", "week", "quarter",
        "fdate", "fyear", "fmonth", "fday", "fweek", "fquarter",
        "cdate", "cyear", "cmonth", "cday", "cweek", "cquarter",
        "ext", "stem", "initial",
    };

    /// <summary>Default .NET format for each date-derived built-in token, used when the placeholder
    /// carries no explicit :format. The f- and c-prefixed twins take the file's last-write or creation
    /// time instead of the drop clock but share these formats. date uses ISO yyyy-MM-dd so folders sort
    /// chronologically.</summary>
    private static readonly Dictionary<string, string> DateTokenFormat = new(StringComparer.Ordinal)
    {
        ["date"] = "yyyy-MM-dd", ["year"] = "yyyy", ["month"] = "MM", ["day"] = "dd", ["time"] = "HH-mm-ss",
        ["fdate"] = "yyyy-MM-dd", ["fyear"] = "yyyy", ["fmonth"] = "MM", ["fday"] = "dd",
        ["cdate"] = "yyyy-MM-dd", ["cyear"] = "yyyy", ["cmonth"] = "MM", ["cday"] = "dd",
    };

    /// <summary>Resolves the folder for a matched file, expanding ${name} placeholders in the rule
    /// Dest from its NameRegex groups. When any placeholder cannot be filled the file goes to the
    /// sorter root instead of a half-built path.</summary>
    private static string ExpandDest(SortRule rule, string filePath, string root, DateTime now)
    {
        if (!rule.Dest.Contains("${", StringComparison.Ordinal)) return Combine(root, rule.Dest);
        var expanded = ExpandTemplate(rule, filePath, now, out bool ok);
        return ok ? Combine(root, expanded) : root;
    }

    /// <summary>Substitutes ${name} tokens in the rule Dest. A built-in name (date/file-date/ext)
    /// resolves from the drop clock <paramref name="now"/> and the file itself; any other name is
    /// looked up among the sanitized captures of the rule's NameRegex conditions. Built-ins shadow a
    /// same-named group. Sets ok=false when a token cannot be filled — unknown name, missing group,
    /// empty value after sanitizing, unreadable file date, or a date format .NET rejects — so the
    /// caller can route the file to the sorter root instead of a half-built path.</summary>
    public static string ExpandTemplate(SortRule rule, string filePath, DateTime now, out bool ok)
    {
        var fileName = Path.GetFileName(filePath);
        var groups = CollectGroups(rule, fileName);
        bool allResolved = true;
        var result = TokenRx.Replace(rule.Dest, m =>
        {
            var name = m.Groups[1].Value;
            var format = m.Groups[2].Success ? m.Groups[2].Value : null;
            string? value = BuiltinTokens.Contains(name)
                ? ResolveBuiltin(name, format, filePath, now)
                : groups.TryGetValue(name, out var raw) ? raw : null;
            if (value != null)
            {
                var clean = SanitizeSegment(value);
                if (clean.Length > 0) return clean;
            }
            allResolved = false;
            return m.Value;
        });
        ok = allResolved;
        return result;
    }

    /// <summary>Editor/preview overload that expands against the current clock.</summary>
    public static string ExpandTemplate(SortRule rule, string filePath, out bool ok) =>
        ExpandTemplate(rule, filePath, DateTime.Now, out ok);

    /// <summary>Value for a built-in token, or null when it cannot be produced (an f/c-token on a file
    /// that is not on disk, or a date format string .NET rejects). Date/time tokens come from
    /// <paramref name="now"/>; the f-prefixed twins from the file's last-write time, the c-prefixed
    /// twins from its creation time; ext/stem/initial from the path.</summary>
    private static string? ResolveBuiltin(string name, string? format, string filePath, DateTime now)
    {
        switch (name)
        {
            case "ext": return Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            case "stem": return Path.GetFileNameWithoutExtension(filePath);
            case "initial": return Initial(Path.GetFileName(filePath));
        }

        // A leading f/c switches the clock to the item's own last-write or creation time; anything else
        // is a drop-time token read from now. Works for a folder as well as a file.
        char prefix = name[0];
        bool fileToken = prefix is 'f' or 'c';
        DateTime source;
        if (fileToken)
        {
            FileSystemInfo? info = Directory.Exists(filePath) ? new DirectoryInfo(filePath)
                                 : File.Exists(filePath) ? new FileInfo(filePath) : null;
            if (info is null) return null;
            source = prefix == 'f' ? info.LastWriteTime : info.CreationTime;
        }
        else source = now;

        // The name without the file prefix picks which date component; week and quarter are computed,
        // the rest are plain format-string renders that also honour an explicit :format.
        var kind = fileToken ? name[1..] : name;
        return kind switch
        {
            "week" => ISOWeek.GetWeekOfYear(source).ToString("D2", CultureInfo.InvariantCulture),
            "quarter" => "Q" + ((source.Month - 1) / 3 + 1),
            _ => Render(source, string.IsNullOrEmpty(format) ? DateTokenFormat[name] : format),
        };
    }

    /// <summary>Applies a .NET date format, returning null when the format is one .NET rejects so the
    /// file falls back to the sorter root rather than crashing the drop.</summary>
    private static string? Render(DateTime source, string format)
    {
        try { return source.ToString(format, CultureInfo.InvariantCulture); }
        catch (FormatException) { return null; }
    }

    /// <summary>First letter of the file name, upper-cased, for alphabetical buckets. A name whose
    /// first letter is not a Unicode letter (a digit, symbol, or leading punctuation) buckets under
    /// "#".</summary>
    private static string Initial(string fileName)
    {
        foreach (var ch in fileName)
        {
            if (char.IsLetter(ch)) return char.ToUpperInvariant(ch).ToString();
            if (char.IsDigit(ch)) break;
        }
        return "#";
    }

    /// <summary>True when the destination folder is the source folder itself or lies inside it. Moving a
    /// folder into its own subtree is impossible, so such a match becomes a no-op instead.</summary>
    private static bool DestinationInsideSource(string dest, string source)
    {
        var d = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dest));
        var s = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        return d.Equals(s, StringComparison.OrdinalIgnoreCase)
            || d.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a folder already sits at a location that matches the shape of its matched
    /// rule's destination — so it is one of the sorter's own output folders and must be left alone,
    /// otherwise a watched sorter would re-file the very folders it creates. The check derives a
    /// recogniser from the destination template (date tokens become digit patterns, quarter becomes
    /// Q1–Q4, and so on) and matches the folder's path relative to the sorter root against the leading
    /// destination segments.</summary>
    private static bool AlreadyPlaced(SortRule rule, string folderPath, string root)
    {
        if (string.IsNullOrWhiteSpace(rule.Dest)) return false;
        string rel;
        try { rel = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(folderPath)); }
        catch { return false; }
        if (rel is "." or "" || rel.StartsWith("..", StringComparison.Ordinal)) return false;

        var srcSegments = rel.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        var destSegments = rule.Dest.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (srcSegments.Length == 0 || srcSegments.Length > destSegments.Length) return false;

        for (int i = 0; i < srcSegments.Length; i++)
        {
            var recogniser = SegmentRecogniser(destSegments[i]);
            if (recogniser is null || !recogniser.IsMatch(srcSegments[i])) return false;
        }
        return true;
    }

    private static readonly char[] PathSeparators = { '\\', '/' };

    /// <summary>An anchored regex that matches any concrete folder name a Dest segment can expand to:
    /// tokens become shape patterns, literal text is escaped. Null when the segment cannot be compiled
    /// (then the caller treats the folder as not-already-placed and lets the other guards catch a
    /// self-move).</summary>
    private static Regex? SegmentRecogniser(string segment)
    {
        var sb = new StringBuilder("^");
        int pos = 0;
        foreach (Match m in TokenRx.Matches(segment))
        {
            sb.Append(Regex.Escape(segment[pos..m.Index]));
            sb.Append(TokenShape(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null));
            pos = m.Index + m.Length;
        }
        sb.Append(Regex.Escape(segment[pos..]));
        sb.Append('$');
        try { return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The regex fragment a single ${token} can expand to inside a folder name. Date tokens
    /// follow their format string; week/quarter/initial have fixed shapes; ext/stem and Name-regex
    /// groups become "any run of name characters".</summary>
    private static string TokenShape(string name, string? format)
    {
        if (!BuiltinTokens.Contains(name)) return "[^\\\\/]+"; // a Name-regex group capture
        if (DateTokenFormat.ContainsKey(name)) return DateFormatShape(format ?? DateTokenFormat[name]);
        var kind = name[0] is 'f' or 'c' ? name[1..] : name;
        return kind switch
        {
            "week" => "\\d{2}",
            "quarter" => "Q[1-4]",
            "initial" => "[^\\\\/]",
            _ => "[^\\\\/]+", // ext, stem
        };
    }

    /// <summary>Turns a .NET date format string into a loose regex: runs of numeric specifiers become
    /// digit groups, month/day name specifiers and AM/PM become a name run, literals are escaped. Loose
    /// on purpose — it must recognise the sorter's own dated folders without matching ordinary names.</summary>
    private static string DateFormatShape(string format)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < format.Length)
        {
            char c = format[i];
            if (char.IsLetter(c))
            {
                int j = i;
                while (j < format.Length && format[j] == c) j++;
                sb.Append((c, j - i) switch
                {
                    ('y', _) => "\\d{2,4}",
                    ('M', >= 3) or ('d', >= 3) or ('t', _) => "[^\\\\/]+",
                    ('M', _) or ('d', _) or ('H', _) or ('h', _) or ('m', _) or ('s', _) => "\\d{1,2}",
                    _ => "",
                });
                i = j;
            }
            else { sb.Append(Regex.Escape(c.ToString())); i++; }
        }
        return sb.ToString();
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

    /// <summary>Every ${name} / ${name:format} placeholder in a Dest as a (name, format) pair, so the
    /// editor can validate built-in date formats without re-parsing the template. Format is null when
    /// the placeholder carries no colon.</summary>
    public static IReadOnlyList<(string Name, string? Format)> ParseTokens(string dest) =>
        TokenRx.Matches(dest)
            .Select(m => (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : (string?)null))
            .ToList();

    /// <summary>Whether a built-in token applies a :format string. Only the plain date/time tokens do;
    /// week, quarter, ext, stem and initial ignore any format. Used by the editor to decide which
    /// tokens to format-check.</summary>
    public static bool TokenAcceptsFormat(string name) => DateTokenFormat.ContainsKey(name);

    /// <summary>True when a date token's format string is one .NET can apply. An empty format uses the
    /// token default and is always valid. Used by the editor to block Save on ${date:garbage}.</summary>
    public static bool IsValidDateFormat(string? format)
    {
        if (string.IsNullOrEmpty(format)) return true;
        try { _ = new DateTime(2001, 2, 3, 4, 5, 6).ToString(format, CultureInfo.InvariantCulture); return true; }
        catch (FormatException) { return false; }
    }

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
