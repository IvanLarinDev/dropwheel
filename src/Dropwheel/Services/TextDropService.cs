using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Dropwheel.Services;

/// <summary>Materialises text dragged from a browser or editor into a file in the target folder.
/// The extension is picked from the content: markdown-looking text becomes .md, otherwise .txt.</summary>
public static class TextDropService
{
    private static readonly string[] PlainTextFormats =
    {
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.StringFormat,
        DataFormats.OemText,
        "UTF8_STRING",
        "text/plain",
        "text/plain;charset=utf-8",
    };

    public static bool HasPotentialText(IDataObject data) =>
        TextFormatCandidates(data).Any();

    public static bool HasText(IDataObject data) => !string.IsNullOrEmpty(GetText(data));

    public static string? GetText(IDataObject data)
    {
        foreach (var format in TextFormatCandidates(data))
        {
            if (TryGetText(data, format) is { Length: > 0 } text) return text;
        }

        return null;
    }

    public static string DescribeFormats(IDataObject data)
    {
        try { return string.Join(", ", data.GetFormats()); }
        catch (Exception ex) { return $"<formats unavailable: {ex.GetType().Name}>"; }
    }

    /// <summary>Saves dragged text into the folder, or null when the drop carries no text.</summary>
    public static string? SaveFrom(IDataObject data, string folder, DateTime now)
    {
        var text = GetText(data);
        return string.IsNullOrEmpty(text) ? null : Save(text, folder, now);
    }

    /// <summary>Writes UTF-8 text into "text_yyyy-MM-dd_HH-mm-ss.&lt;ext&gt;", avoiding name
    /// collisions with a "(2)" suffix. Returns the created path.</summary>
    public static string Save(string text, string folder, DateTime now)
    {
        Directory.CreateDirectory(folder);
        var name = $"text_{now:yyyy-MM-dd_HH-mm-ss}.{ExtensionFor(text)}";
        var path = Unique(folder, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    public static string ExtensionFor(string text) => LooksLikeMarkdown(text) ? "md" : "txt";

    private static readonly Regex Heading = new(@"(?m)^\s{0,3}#{1,6}\s", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[[^\]]+\]\([^)]+\)", RegexOptions.Compiled);

    /// <summary>Conservative markdown check: an ATX heading, a fenced code block, or a link.</summary>
    public static bool LooksLikeMarkdown(string text) =>
        text.Contains("```") || Heading.IsMatch(text) || Link.IsMatch(text);

    private static string? TryGetText(IDataObject data, string format)
    {
        foreach (var autoConvert in new[] { false, true })
        {
            try
            {
                if (!data.GetDataPresent(format, autoConvert)) continue;
                if (TextFromFormat(format, data.GetData(format, autoConvert)) is { Length: > 0 } text)
                    return text;
            }
            catch
            {
                // Some delayed-rendered drag formats throw until the final Drop. Try the next mode.
            }
        }

        return null;
    }

    private static string? TextFromFormat(string format, object? data)
    {
        var text = TextFromData(data);
        if (string.IsNullOrEmpty(text)) return null;
        if (IsHtmlFormat(format)) return TextFromHtml(text);
        if (IsRtfFormat(format)) return TextFromRtf(text);
        return text.TrimEnd('\0');
    }

    private static string? TextFromData(object? data)
    {
        if (data is string text) return text;
        if (data is byte[] bytes) return TextFromBytes(bytes);
        if (data is not Stream stream) return null;

        if (stream.CanSeek) stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return TextFromBytes(copy.ToArray());
    }

    private static string? TextFromBytes(byte[] bytes)
    {
        bytes = bytes.TrimTrailingZeros();
        if (bytes.Length == 0) return null;
        return LooksLikeUtf16(bytes)
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
    }

    private static IEnumerable<string> TextFormatCandidates(IDataObject data)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var formats = SafeGetFormats(data);

        foreach (var format in PlainTextFormats)
        {
            if ((formats.Contains(format) || SafeGetDataPresent(data, format)) && seen.Add(format))
                yield return format;
        }

        foreach (var format in formats)
        {
            if (LooksLikeTextFormat(format) && seen.Add(format))
                yield return format;
        }
    }

    private static string[] SafeGetFormats(IDataObject data)
    {
        try { return data.GetFormats(autoConvert: true); }
        catch { return Array.Empty<string>(); }
    }

    private static bool SafeGetDataPresent(IDataObject data, string format)
    {
        try { return data.GetDataPresent(format, autoConvert: true); }
        catch { return false; }
    }

    private static bool LooksLikeTextFormat(string format)
    {
        var normalized = format.ToLowerInvariant();
        return normalized.Contains("text")
            || normalized.Contains("string")
            || normalized.Contains("utf8")
            || normalized.Contains("unicode")
            || normalized.Contains("html")
            || normalized.Contains("rtf")
            || normalized.Contains("rich text");
    }

    private static bool IsHtmlFormat(string format) =>
        format.Contains("html", StringComparison.OrdinalIgnoreCase);

    private static bool IsRtfFormat(string format) =>
        format.Contains("rtf", StringComparison.OrdinalIgnoreCase)
        || format.Contains("rich text", StringComparison.OrdinalIgnoreCase);

    private static string? TextFromHtml(string html)
    {
        var fragment = HtmlFragment(html);
        fragment = Regex.Replace(fragment, @"(?i)<br\s*/?>", "\n");
        fragment = Regex.Replace(fragment, @"(?i)</p\s*>", "\n");
        fragment = Regex.Replace(fragment, "<[^>]+>", "");
        return WebUtility.HtmlDecode(fragment).Trim();
    }

    private static string HtmlFragment(string html)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var markerStart = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var markerEnd = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        if (markerStart >= 0 && markerEnd > markerStart)
            return html[(markerStart + startMarker.Length)..markerEnd];

        var start = Regex.Match(html, @"StartFragment:(\d+)");
        var end = Regex.Match(html, @"EndFragment:(\d+)");
        if (start.Success
            && end.Success
            && int.TryParse(start.Groups[1].Value, out var startIndex)
            && int.TryParse(end.Groups[1].Value, out var endIndex)
            && startIndex >= 0
            && endIndex > startIndex
            && endIndex <= html.Length)
            return html[startIndex..endIndex];

        return html;
    }

    private static string? TextFromRtf(string rtf)
    {
        var text = Regex.Replace(rtf, @"\\'[0-9a-fA-F]{2}", "");
        text = Regex.Replace(text, @"\\[a-zA-Z]+\d* ?", "");
        text = text.Replace("{", "").Replace("}", "").Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool LooksLikeUtf16(byte[] bytes) =>
        bytes.Length >= 2 && bytes.Where((_, i) => i % 2 == 1).Take(16).Count(b => b == 0) >= 4;

    private static string Unique(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (!PathExists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!PathExists(path)) return path;
        }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);
}

internal static class ByteArrayExtensions
{
    public static byte[] TrimTrailingZeros(this byte[] bytes)
    {
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0) length--;
        return length == bytes.Length ? bytes : bytes[..length];
    }
}
