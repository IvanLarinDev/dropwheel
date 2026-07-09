using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Dropwheel.Services;

/// <summary>Materialises text dragged from a browser or editor into a file in the target folder.
/// The extension is picked from the content: markdown-looking text becomes .md, otherwise .txt.</summary>
public static class TextDropService
{
    public static bool HasText(IDataObject data) =>
        data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);

    public static string? GetText(IDataObject data) =>
        data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;

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
