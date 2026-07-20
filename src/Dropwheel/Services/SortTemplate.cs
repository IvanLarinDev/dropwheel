using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Parses, validates, documents, and expands sorter destination templates.</summary>
internal static class SortTemplate
{
    private static readonly Regex TokenRx = new(
        @"\$\{([A-Za-z][A-Za-z0-9]*)(?::([^}]*))?\}",
        RegexOptions.Compiled);

    internal static readonly IReadOnlyCollection<string> BuiltinTokens =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "date", "year", "month", "day", "time", "week", "quarter",
            "fdate", "fyear", "fmonth", "fday", "fweek", "fquarter",
            "cdate", "cyear", "cmonth", "cday", "cweek", "cquarter",
            "ext", "stem", "initial", "size", "slug",
        };

    private static readonly Dictionary<string, string> DateTokenFormat = new(StringComparer.Ordinal)
    {
        ["date"] = "yyyy-MM-dd",
        ["year"] = "yyyy",
        ["month"] = "MM",
        ["day"] = "dd",
        ["time"] = "HH-mm-ss",
        ["fdate"] = "yyyy-MM-dd",
        ["fyear"] = "yyyy",
        ["fmonth"] = "MM",
        ["fday"] = "dd",
        ["cdate"] = "yyyy-MM-dd",
        ["cyear"] = "yyyy",
        ["cmonth"] = "MM",
        ["cday"] = "dd",
    };

    private static readonly IReadOnlyList<(string Name, double? Max)> DefaultSizeBuckets =
        new (string, double?)[]
        {
            ("tiny", 1), ("small", 10), ("medium", 100), ("large", 1000), ("huge", null),
        };

    private static readonly char[] PathSeparators = ['\\', '/'];
    private static readonly DateTime SampleTime = new(2026, 3, 14, 15, 9, 26);

    private static readonly (
        string Name,
        SortService.TokenGroup Group,
        string Summary,
        string? Example)[] TokenInfo =
    {
        ("date", SortService.TokenGroup.DropDate, "Drop date, ISO so folders sort by time.", null),
        ("year", SortService.TokenGroup.DropDate, "Drop year.", null),
        ("month", SortService.TokenGroup.DropDate, "Drop month, two digits.", null),
        ("day", SortService.TokenGroup.DropDate, "Drop day of month, two digits.", null),
        ("time", SortService.TokenGroup.DropDate, "Drop time of day.", null),
        ("week", SortService.TokenGroup.DropDate, "ISO week number of the drop.", null),
        ("quarter", SortService.TokenGroup.DropDate, "Calendar quarter of the drop.", null),
        ("fdate", SortService.TokenGroup.FileDate, "File's modified date (f- prefix on any date token).", null),
        ("fyear", SortService.TokenGroup.FileDate, "File's modified year.", null),
        ("fmonth", SortService.TokenGroup.FileDate, "File's modified month.", null),
        ("fday", SortService.TokenGroup.FileDate, "File's modified day.", null),
        ("fweek", SortService.TokenGroup.FileDate, "File's modified ISO week.", null),
        ("fquarter", SortService.TokenGroup.FileDate, "File's modified quarter.", null),
        ("cdate", SortService.TokenGroup.FileDate, "File's created date (c- prefix on any date token).", null),
        ("cyear", SortService.TokenGroup.FileDate, "File's created year.", null),
        ("cmonth", SortService.TokenGroup.FileDate, "File's created month.", null),
        ("cday", SortService.TokenGroup.FileDate, "File's created day.", null),
        ("cweek", SortService.TokenGroup.FileDate, "File's created ISO week.", null),
        ("cquarter", SortService.TokenGroup.FileDate, "File's created quarter.", null),
        ("ext", SortService.TokenGroup.FileName, "File extension, lower-case, no dot.", "jpg"),
        ("stem", SortService.TokenGroup.FileName, "File name without the extension; a :N caps it to N characters.", "Holiday Photo"),
        ("initial", SortService.TokenGroup.FileName, "First letter of the name, upper-case; '#' when not a letter.", "H"),
        ("slug", SortService.TokenGroup.FileName, "Slug of a text file's first non-blank line.", "meeting-notes"),
        ("size", SortService.TokenGroup.Size, "Coarse size bucket; a :spec names the buckets.", "large"),
    };

    internal static string ExpandDest(SortRule rule, string filePath, string root, DateTime now)
    {
        if (!rule.Dest.Contains("${", StringComparison.Ordinal)) return Combine(root, rule.Dest);
        var expanded = Expand(rule, filePath, now, out var ok);
        return ok ? Combine(root, expanded) : root;
    }

    internal static string Expand(SortRule rule, string filePath, DateTime now, out bool ok)
    {
        var groups = CollectGroups(rule, Path.GetFileName(filePath));
        var allResolved = true;
        var result = TokenRx.Replace(rule.Dest, match =>
        {
            var name = match.Groups[1].Value;
            var format = match.Groups[2].Success ? match.Groups[2].Value : null;
            var value = BuiltinTokens.Contains(name)
                ? ResolveBuiltin(name, format, filePath, now)
                : groups.TryGetValue(name, out var raw) ? PadGroup(raw, format) : null;
            if (value != null)
            {
                var clean = SanitizeSegment(value);
                if (clean.Length > 0) return clean;
            }
            allResolved = false;
            return match.Value;
        });
        ok = allResolved;
        return result;
    }

    internal static string Expand(SortRule rule, string filePath, out bool ok) =>
        Expand(rule, filePath, DateTime.Now, out ok);

    private static string? ResolveBuiltin(
        string name,
        string? format,
        string filePath,
        DateTime now)
    {
        switch (name)
        {
            case "ext": return Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            case "stem": return Truncate(Path.GetFileNameWithoutExtension(filePath), format);
            case "initial": return Initial(Path.GetFileName(filePath));
            case "size": return SizeBucket(filePath, format);
            case "slug": return FileSlug(filePath);
        }

        var prefix = name[0];
        var fileToken = prefix is 'f' or 'c';
        DateTime source;
        if (fileToken)
        {
            FileSystemInfo? info = Directory.Exists(filePath)
                ? new DirectoryInfo(filePath)
                : File.Exists(filePath) ? new FileInfo(filePath) : null;
            if (info is null) return null;
            source = prefix == 'f' ? info.LastWriteTime : info.CreationTime;
        }
        else
        {
            source = now;
        }

        var kind = fileToken ? name[1..] : name;
        return kind switch
        {
            "week" => ISOWeek.GetWeekOfYear(source).ToString("D2", CultureInfo.InvariantCulture),
            "quarter" => "Q" + ((source.Month - 1) / 3 + 1),
            _ => Render(source, string.IsNullOrEmpty(format) ? DateTokenFormat[name] : format),
        };
    }

    private static string? Render(DateTime source, string format)
    {
        try { return source.ToString(format, CultureInfo.InvariantCulture); }
        catch (FormatException) { return null; }
    }

    private static string? FileSlug(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            foreach (var line in File.ReadLines(filePath).Take(20))
            {
                var slug = TextDropService.SlugOf(line);
                if (slug.Length > 0) return slug;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
        return null;
    }

    private static string Initial(string fileName)
    {
        foreach (var ch in fileName)
        {
            if (char.IsLetter(ch)) return char.ToUpperInvariant(ch).ToString();
            if (char.IsDigit(ch)) break;
        }
        return "#";
    }

    private static int? ParseCount(string? format) =>
        !string.IsNullOrEmpty(format)
        && int.TryParse(format, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
        && count > 0
            ? count
            : null;

    private static string Truncate(string value, string? format) =>
        ParseCount(format) is { } max && value.Length > max ? value[..max] : value;

    private static string PadGroup(string raw, string? format) =>
        ParseCount(format) is { } width && raw.Length > 0 && raw.All(char.IsAsciiDigit)
            ? raw.PadLeft(width, '0')
            : raw;

    internal static string? SizeBucketOf(double megabytes, string? spec = null)
    {
        var buckets = string.IsNullOrWhiteSpace(spec) ? DefaultSizeBuckets : ParseSizeSpec(spec);
        if (buckets is null) return null;
        foreach (var (name, max) in buckets)
            if (max is null || megabytes < max) return name;
        return null;
    }

    private static string? SizeBucket(string filePath, string? spec)
    {
        if (!File.Exists(filePath)) return null;
        return SizeBucketOf(new FileInfo(filePath).Length / (1024.0 * 1024.0), spec);
    }

    internal static IReadOnlyList<(string Name, double? Max)>? ParseSizeSpec(string spec)
    {
        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var buckets = new List<(string Name, double? Max)>();
        var previous = double.NegativeInfinity;
        for (var i = 0; i < parts.Length; i++)
        {
            var bits = parts[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length is 0 or > 2) return null;
            var last = i == parts.Length - 1;
            if (bits.Length == 1)
            {
                if (!last) return null;
                buckets.Add((bits[0], null));
                continue;
            }
            if (!double.TryParse(
                    bits[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var maximum)
                || maximum <= 0
                || maximum <= previous)
                return null;
            previous = maximum;
            buckets.Add((bits[0], maximum));
        }
        return buckets;
    }

    internal static bool DestinationInsideSource(string destination, string source)
    {
        var normalizedDestination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var normalizedSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        return normalizedDestination.Equals(normalizedSource, StringComparison.OrdinalIgnoreCase)
            || normalizedDestination.StartsWith(
                normalizedSource + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool AlreadyPlaced(SortRule rule, string folderPath, string root)
    {
        if (string.IsNullOrWhiteSpace(rule.Dest)) return false;
        string relative;
        try { relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(folderPath)); }
        catch { return false; }
        if (relative is "." or "" || relative.StartsWith("..", StringComparison.Ordinal)) return false;

        var sourceSegments = relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        var destinationSegments = rule.Dest.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (sourceSegments.Length == 0 || sourceSegments.Length > destinationSegments.Length) return false;
        for (var i = 0; i < sourceSegments.Length; i++)
        {
            var recognizer = SegmentRecognizer(destinationSegments[i]);
            if (recognizer is null || !recognizer.IsMatch(sourceSegments[i])) return false;
        }
        return true;
    }

    private static Regex? SegmentRecognizer(string segment)
    {
        var builder = new StringBuilder("^");
        var position = 0;
        foreach (Match match in TokenRx.Matches(segment))
        {
            builder.Append(Regex.Escape(segment[position..match.Index]));
            builder.Append(TokenShape(
                match.Groups[1].Value,
                match.Groups[2].Success ? match.Groups[2].Value : null));
            position = match.Index + match.Length;
        }
        builder.Append(Regex.Escape(segment[position..]));
        builder.Append('$');
        try
        {
            return new Regex(
                builder.ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                SortConditionMatcher.RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string TokenShape(string name, string? format)
    {
        if (!BuiltinTokens.Contains(name)) return "[^\\\\/]+";
        if (DateTokenFormat.TryGetValue(name, out var defaultFormat))
            return DateFormatShape(format ?? defaultFormat);
        var kind = name[0] is 'f' or 'c' ? name[1..] : name;
        return kind switch
        {
            "week" => "\\d{2}",
            "quarter" => "Q[1-4]",
            "initial" => "[^\\\\/]",
            "size" => SizeShape(format),
            _ => "[^\\\\/]+",
        };
    }

    private static string SizeShape(string? format)
    {
        var buckets = string.IsNullOrWhiteSpace(format) ? DefaultSizeBuckets : ParseSizeSpec(format);
        if (buckets is null || buckets.Count == 0) return "[^\\\\/]+";
        return "(?:" + string.Join("|", buckets.Select(bucket => Regex.Escape(bucket.Name))) + ")";
    }

    private static string DateFormatShape(string format)
    {
        var builder = new StringBuilder();
        var index = 0;
        while (index < format.Length)
        {
            var character = format[index];
            if (char.IsLetter(character))
            {
                var end = index;
                while (end < format.Length && format[end] == character) end++;
                builder.Append((character, end - index) switch
                {
                    ('y', _) => "\\d{2,4}",
                    ('M', >= 3) or ('d', >= 3) or ('t', _) => "[^\\\\/]+",
                    ('M', _) or ('d', _) or ('H', _) or ('h', _) or ('m', _) or ('s', _) => "\\d{1,2}",
                    _ => "",
                });
                index = end;
            }
            else
            {
                builder.Append(Regex.Escape(character.ToString()));
                index++;
            }
        }
        return builder.ToString();
    }

    private static Dictionary<string, string> CollectGroups(SortRule rule, string fileName)
    {
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var condition in rule.All)
        {
            if (condition.Field != ConditionField.NameRegex
                || SortConditionMatcher.Compiled(condition.Value) is not { } regex
                || !SortConditionMatcher.TryMatch(regex, fileName, condition.Value, out var match)
                || !match.Success)
                continue;
            foreach (var name in regex.GetGroupNames())
            {
                if (int.TryParse(name, out _)) continue;
                var group = match.Groups[name];
                if (group.Success) groups[name] = group.Value;
            }
        }
        return groups;
    }

    internal static IReadOnlyList<string> TokensIn(string destination) =>
        TokenRx.Matches(destination)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    internal static IReadOnlyList<(string Name, string? Format)> ParseTokens(string destination) =>
        TokenRx.Matches(destination)
            .Select(match => (
                match.Groups[1].Value,
                match.Groups[2].Success ? match.Groups[2].Value : (string?)null))
            .ToList();

    internal static bool TokenTakesFormat(string name) =>
        DateTokenFormat.ContainsKey(name) || name is "size" or "stem";

    internal static bool IsValidTokenFormat(string name, string? format)
    {
        if (name == "size") return string.IsNullOrEmpty(format) || ParseSizeSpec(format) is not null;
        if (DateTokenFormat.ContainsKey(name)) return IsValidDateFormat(format);
        if (name == "stem") return string.IsNullOrEmpty(format) || ParseCount(format) is not null;
        return true;
    }

    internal static bool IsValidDateFormat(string? format)
    {
        if (string.IsNullOrEmpty(format)) return true;
        try
        {
            _ = new DateTime(2001, 2, 3, 4, 5, 6).ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<SortService.TokenDoc> TokenDocs() =>
        TokenInfo.Select(info => new SortService.TokenDoc(
            info.Name,
            info.Group,
            info.Summary,
            info.Example ?? DateExample(info.Name),
            TokenTakesFormat(info.Name))).ToList();

    private static string DateExample(string name)
    {
        var kind = name[0] is 'f' or 'c' ? name[1..] : name;
        return kind switch
        {
            "week" => ISOWeek.GetWeekOfYear(SampleTime).ToString("D2", CultureInfo.InvariantCulture),
            "quarter" => "Q" + ((SampleTime.Month - 1) / 3 + 1),
            _ => Render(SampleTime, DateTokenFormat[name]) ?? "",
        };
    }

    internal static IReadOnlyCollection<string> AvailableTokens(SortRule rule)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in rule.All)
        {
            if (condition.Field != ConditionField.NameRegex
                || SortConditionMatcher.Compiled(condition.Value) is not { } regex)
                continue;
            foreach (var name in regex.GetGroupNames())
                if (!int.TryParse(name, out _)) tokens.Add(name);
        }
        return tokens;
    }

    private static string Combine(string root, string destination) =>
        string.IsNullOrWhiteSpace(destination)
            ? root
            : Path.IsPathRooted(destination) ? destination : Path.Combine(root, destination);

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = value.Where(character => Array.IndexOf(invalid, character) < 0).ToArray();
        return new string(characters).Trim().TrimEnd('.', ' ');
    }
}
