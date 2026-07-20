using System.IO;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Distributes files according to a sorter target's rules.</summary>
public static class SortService
{
    public static Dictionary<string, List<string>> Plan(TargetItem target, IEnumerable<string> files)
    {
        var useRichRules = target.Rules is { Count: > 0 };
        var now = DateTime.Now;
        var plan = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var folder = useRichRules
                ? ResolveFolder(target, file, now)
                : ResolveLegacyFolder(target, file);
            if (!plan.TryGetValue(folder, out var group)) plan[folder] = group = [];
            group.Add(file);
        }
        return plan;
    }

    public static Dictionary<string, string[]> MovePlan(TargetItem target, IEnumerable<string> files)
    {
        var plan = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, group) in Plan(target, files))
        {
            var moving = group.Where(file => !SameFolder(folder, file)).ToArray();
            if (moving.Length > 0) plan[folder] = moving;
        }
        return plan;
    }

    public static bool SameFolder(string destinationFolder, string file)
    {
        var sourceFolder = Path.GetDirectoryName(Path.GetFullPath(file));
        if (sourceFolder == null) return false;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationFolder)),
            Path.TrimEndingDirectorySeparator(sourceFolder),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFolder(TargetItem target, string file, DateTime now)
    {
        var metadata = FileMeta.Read(file);
        var ruleIndex = MatchedRuleIndex(target.Rules!, metadata);
        if (ruleIndex < 0) return target.Path;
        var rule = target.Rules![ruleIndex];
        if (metadata.IsDirectory && SortTemplate.AlreadyPlaced(rule, file, target.Path))
            return OwnFolder(file);
        var destination = SortTemplate.ExpandDest(rule, file, target.Path, now);
        if (metadata.IsDirectory && SortTemplate.DestinationInsideSource(destination, file))
            return OwnFolder(file);
        return destination;
    }

    private static string OwnFolder(string file) =>
        Path.GetDirectoryName(Path.GetFullPath(file)) ?? file;

    public static int MatchedRuleIndex(IReadOnlyList<SortRule> rules, string file) =>
        MatchedRuleIndex(rules, FileMeta.Read(file));

    public static int MatchedRuleIndex(IReadOnlyList<SortRule> rules, FileMeta metadata) =>
        SortConditionMatcher.MatchedRuleIndex(rules, metadata);

    public static bool ScopeIncludes(RuleScope scope, bool isDirectory) =>
        SortConditionMatcher.ScopeIncludes(scope, isDirectory);

    private static string ResolveLegacyFolder(TargetItem target, string file)
    {
        var extension = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        string? destination = null;
        string? fallback = null;
        foreach (var (key, value) in target.SortRules!)
        {
            if (key.Trim() == "*")
            {
                fallback = value;
                continue;
            }
            var extensions = key.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => item.TrimStart('.').ToLowerInvariant());
            if (extension.Length > 0 && extensions.Contains(extension))
            {
                destination = value;
                break;
            }
        }
        destination ??= fallback;
        return destination == null
            ? target.Path
            : Path.IsPathRooted(destination) ? destination : Path.Combine(target.Path, destination);
    }

    public static readonly IReadOnlyCollection<string> BuiltinTokens = SortTemplate.BuiltinTokens;

    public static string ExpandTemplate(
        SortRule rule,
        string filePath,
        DateTime now,
        out bool ok) =>
        SortTemplate.Expand(rule, filePath, now, out ok);

    public static string ExpandTemplate(SortRule rule, string filePath, out bool ok) =>
        SortTemplate.Expand(rule, filePath, out ok);

    public static string? SizeBucketOf(double megabytes, string? spec = null) =>
        SortTemplate.SizeBucketOf(megabytes, spec);

    public static IReadOnlyList<(string Name, double? Max)>? ParseSizeSpec(string spec) =>
        SortTemplate.ParseSizeSpec(spec);

    public static IReadOnlyList<string> TokensIn(string destination) =>
        SortTemplate.TokensIn(destination);

    public static IReadOnlyList<(string Name, string? Format)> ParseTokens(string destination) =>
        SortTemplate.ParseTokens(destination);

    public static bool TokenTakesFormat(string name) => SortTemplate.TokenTakesFormat(name);

    public static bool IsValidTokenFormat(string name, string? format) =>
        SortTemplate.IsValidTokenFormat(name, format);

    public static bool IsValidDateFormat(string? format) => SortTemplate.IsValidDateFormat(format);

    public enum TokenGroup { DropDate, FileDate, FileName, Size }

    public sealed record TokenDoc(
        string Name,
        TokenGroup Group,
        string Summary,
        string Example,
        bool TakesFormat);

    public static IReadOnlyList<TokenDoc> TokenDocs() => SortTemplate.TokenDocs();

    public static IReadOnlyCollection<string> AvailableTokens(SortRule rule) =>
        SortTemplate.AvailableTokens(rule);

    public static IReadOnlyList<string> MediaKinds => SortConditionMatcher.MediaKinds;
}
