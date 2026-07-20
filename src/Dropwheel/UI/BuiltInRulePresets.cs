namespace Dropwheel.UI;

/// <summary>Built-in rule presets that are always available independently of user configuration.</summary>
internal static class BuiltInRulePresets
{
    internal static readonly (string Label, string Destination)[] Dated =
    {
        ("Today's date  —  ${date}", "${date}"),
        ("Year \\ month  —  ${year}\\${month}", "${year}\\${month}"),
        ("Year \\ month \\ day", "${year}\\${month}\\${day}"),
        ("ISO week  —  ${year}\\week-${week}", "${year}\\week-${week}"),
        ("Quarter  —  ${year}\\${quarter}", "${year}\\${quarter}"),
        ("By file's modified month  —  ${fyear}\\${fmonth}", "${fyear}\\${fmonth}"),
    };

    internal static readonly (string Label, string Destination)[] BySize =
    {
        ("Default buckets  —  ${size}", "by-size\\${size}"),
        ("Custom buckets  —  ${size: tiny 0.5, …}",
            "by-size\\${size:tiny 0.5, small 10, medium 100, large 1000, huge}"),
    };
}
