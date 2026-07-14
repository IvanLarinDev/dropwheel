using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Creates quick-access targets from dragged links such as tg:// and https://t.me/...</summary>
public static class LinkTargetService
{
    private sealed record LinkDropCandidate(string Text, string? Title = null);

    private static readonly Regex LaunchUri =
        new(@"\b(?:(?:https?://|tg://)[^\s<>'""]+|(?:t\.me|telegram\.me|telegram\.dog)/[^\s<>'""]+|[a-z0-9_]{2,64}\.t\.me(?:/[^\s<>'""]*)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SavedMessagesLabel =
        new(@"^\s*(?:saved messages?|избранное)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool HasLaunchUri(IDataObject data) => TryGetLaunchUri(data, out _);

    /// <summary>True when the drop carries real selected text rather than a bare link — text that is not
    /// just a URL on its own. A browser attaches the page URL (and the selection's HTML) even to a plain
    /// text selection, so a folder drop must treat such text as a file to save, not as a link target to
    /// add. A drop whose whole text is just a URL is still a link.</summary>
    public static bool HasSelectedText(IDataObject data)
    {
        var text = TextDropService.GetText(data);
        if (string.IsNullOrWhiteSpace(text)) return false;
        return !(TryExtractLaunchUri(text, out var uri)
                 && string.Equals(uri.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasPotentialLaunchUriData(IDataObject data) =>
        data.GetDataPresent("UniformResourceLocatorW")
        || data.GetDataPresent("UniformResourceLocator")
        || data.GetDataPresent("text/x-moz-url")
        || data.GetDataPresent(DataFormats.Html)
        || TextDropService.HasPotentialText(data);

    public static TargetItem? CreateTarget(IDataObject data) =>
        TryGetLaunchUri(data, out var candidate) ? CreateTarget(candidate.Text, candidate.Title) : null;

    /// <summary>Targets for every distinct link in a drop. When the dropped plain text holds two or more
    /// links (a multi-line selection of URLs), each becomes its own tile, de-duplicated by resolved path.
    /// Otherwise it falls back to the single-link path so an ordinary link drag keeps its title and
    /// explicit URL formats. The plain text is used on purpose — scanning the HTML fragment would pull
    /// dozens of URLs out of the markup.</summary>
    public static IReadOnlyList<TargetItem> CreateTargets(IDataObject data)
    {
        var text = TextDropService.GetText(data);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var many = ExtractAll(text);
            if (many.Count >= 2) return many;
        }
        return CreateTarget(data) is { } single ? new[] { single } : Array.Empty<TargetItem>();
    }

    /// <summary>Every distinct launch URI in the text, in order, de-duplicated by resolved target path.</summary>
    private static List<TargetItem> ExtractAll(string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<TargetItem>();
        foreach (Match m in LaunchUri.Matches(text))
        {
            var candidate = NormalizeUri(TrimUri(m.Value));
            if (!TargetItem.IsLaunchUri(candidate)) continue;
            var target = CreateTarget(candidate, titleHint: null);
            if (seen.Add(target.Path)) list.Add(target);
        }
        return list;
    }

    public static bool HasSavedMessagesLabel(IDataObject data) =>
        TextCandidates(data).Any(IsSavedMessagesText);

    public static TargetItem? CreateSavedMessagesTarget(string account)
    {
        account = account.Trim();
        if (account.Length == 0) return null;

        if (CreateTarget(account) is { } linkTarget)
            return new TargetItem { Name = "Saved Messages", Path = linkTarget.Path };

        account = account.TrimStart('@');
        if (account.Length == 0) return null;

        var parameter = account.StartsWith('+') || account.Any(char.IsDigit) && account.All(c => char.IsDigit(c) || c is '+' or '-' or '(' or ')' or ' ')
            ? "phone"
            : "domain";
        var value = parameter == "phone"
            ? new string(account.Where(c => char.IsDigit(c) || c == '+').ToArray())
            : account;
        if (value.Length == 0) return null;

        return new TargetItem
        {
            Name = "Saved Messages",
            Path = $"tg://resolve?{parameter}={Uri.EscapeDataString(value)}",
        };
    }

    internal static TargetItem? CreateTarget(string text)
    {
        if (!TryExtractLaunchUri(text, out var uriText)) return null;
        return CreateTarget(uriText, titleHint: null);
    }

    internal static bool TryExtractLaunchUri(string? text, out string uriText)
    {
        uriText = "";
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = LaunchUri.Match(text);
        if (!match.Success) return false;

        var candidate = NormalizeUri(TrimUri(match.Value));
        if (!TargetItem.IsLaunchUri(candidate)) return false;

        uriText = candidate;
        return true;
    }

    internal static bool IsSavedMessagesText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Any(line => SavedMessagesLabel.IsMatch(line));
    }

    private static bool TryGetLaunchUri(IDataObject data, out LinkDropCandidate candidate)
    {
        LinkDropCandidate? fallback = null;
        foreach (var text in LinkDropCandidates(data))
        {
            if (!TryExtractLaunchUri(text.Text, out var uriText)) continue;
            var current = text with { Text = uriText };
            if (!string.IsNullOrWhiteSpace(current.Title))
            {
                candidate = current;
                return true;
            }

            fallback ??= current;
        }

        candidate = fallback ?? new LinkDropCandidate("");
        return fallback != null;
    }

    private static IEnumerable<string> TextCandidates(IDataObject data)
        => LinkDropCandidates(data).Select(candidate => candidate.Text);

    private static IEnumerable<LinkDropCandidate> LinkDropCandidates(IDataObject data)
    {
        if (ReadData(data, "UniformResourceLocatorW", Encoding.Unicode) is { } urlW)
            yield return new LinkDropCandidate(urlW);
        if (ReadData(data, "UniformResourceLocator", Encoding.Default) is { } url)
            yield return new LinkDropCandidate(url);
        if (ReadData(data, "text/x-moz-url", Encoding.Unicode) is { } moz)
            yield return new LinkDropCandidate(FirstLine(moz), SecondLine(moz));
        if (data.GetData(DataFormats.Html) is string html)
            yield return new LinkDropCandidate(html, TitleFromHtml(html));
        if (TextDropService.GetText(data) is { } text)
            yield return new LinkDropCandidate(text);
    }

    private static string? ReadData(IDataObject data, string format, Encoding encoding)
    {
        if (!data.GetDataPresent(format)) return null;
        return data.GetData(format) switch
        {
            string text => text,
            byte[] bytes => encoding.GetString(bytes),
            MemoryStream stream => encoding.GetString(stream.ToArray()),
            Stream stream => ReadStream(stream, encoding),
            _ => null,
        };
    }

    private static string ReadStream(Stream stream, Encoding encoding)
    {
        if (stream.CanSeek) stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return encoding.GetString(copy.ToArray());
    }

    private static string FirstLine(string text) =>
        text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0];

    private static string? SecondLine(string text) =>
        text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Skip(1).FirstOrDefault()?.Trim();

    private static string TrimUri(string value) =>
        value.Trim().TrimEnd('\0', '.', ',', ';', '!', ')', ']', '}');

    private static string NormalizeUri(string value) =>
        IsBareTelegramLink(value) ? "https://" + value : value;

    private static bool IsBareTelegramLink(string value) =>
        value.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("telegram.me/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("telegram.dog/", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(value, @"^[a-z0-9_]{2,64}\.t\.me(?:/|$)", RegexOptions.IgnoreCase);

    private static TargetItem CreateTarget(string uriText, string? titleHint)
    {
        var path = TargetPathFor(uriText);
        return new TargetItem
        {
            Name = NameFor(uriText, titleHint),
            Path = path,
            SourceUrl = IsWebUri(uriText) ? uriText : null,
        };
    }

    private static string NameFor(string uriText, string? titleHint)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return uriText;
        if (CleanTitle(titleHint, uriText) is { } title) return title;
        if (IsTelegramUri(uri)) return TelegramName(uri);
        return string.IsNullOrWhiteSpace(uri.Host) ? uriText : uri.Host;
    }

    private static string? CleanTitle(string? title, string uriText)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        title = Regex.Replace(title, @"\s+", " ").Trim();
        return title.Length > 0 && !title.Equals(uriText, StringComparison.OrdinalIgnoreCase)
            ? title
            : null;
    }

    private static string? TitleFromHtml(string html)
    {
        var title = Regex.Match(html, @"(?is)<title[^>]*>(.*?)</title>");
        if (title.Success) return DecodeHtmlText(title.Groups[1].Value);

        var anchor = Regex.Match(HtmlFragment(html), @"(?is)<a\b[^>]*>(.*?)</a>");
        return anchor.Success ? DecodeHtmlText(anchor.Groups[1].Value) : null;
    }

    private static string HtmlFragment(string html)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var markerStart = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var markerEnd = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        if (markerStart >= 0 && markerEnd > markerStart)
            return html[(markerStart + startMarker.Length)..markerEnd];

        return html;
    }

    private static string DecodeHtmlText(string value)
    {
        value = Regex.Replace(value, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(value).Trim();
    }

    private static string TargetPathFor(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) || !IsTelegramWebUri(uri))
            return uriText;

        return TelegramWebDeepLink(uri) ?? uriText;
    }

    private static bool IsTelegramWebUri(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool IsWebUri(string uriText) =>
        Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
        && IsTelegramWebUri(uri);

    private static string? TelegramWebDeepLink(Uri uri)
    {
        if (!IsTelegramUri(uri)) return null;

        if (uri.Host.EndsWith(".t.me", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.Equals("t.me", StringComparison.OrdinalIgnoreCase))
        {
            var domain = uri.Host[..^".t.me".Length];
            return $"tg://resolve?domain={Uri.EscapeDataString(domain)}";
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        var first = Uri.UnescapeDataString(segments[0]);
        if (first.Equals("c", StringComparison.OrdinalIgnoreCase) && segments.Length >= 3)
        {
            if (segments.Length >= 4)
                return $"tg://privatepost?channel={Uri.EscapeDataString(segments[1])}&topic={Uri.EscapeDataString(segments[2])}&post={Uri.EscapeDataString(segments[3])}";

            return $"tg://privatepost?channel={Uri.EscapeDataString(segments[1])}&post={Uri.EscapeDataString(segments[2])}";
        }

        if (first.StartsWith('+') && first.Length > 1)
            return $"tg://join?invite={Uri.EscapeDataString(first[1..])}";

        if (first.Equals("joinchat", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
            return $"tg://join?invite={Uri.EscapeDataString(segments[1])}";

        var resolvedDomain = Uri.EscapeDataString(first.TrimStart('@'));
        if (segments.Length >= 3 && int.TryParse(segments[1], out _) && int.TryParse(segments[2], out _))
            return $"tg://resolve?domain={resolvedDomain}&topic={Uri.EscapeDataString(segments[1])}&post={Uri.EscapeDataString(segments[2])}";

        if (segments.Length >= 2 && int.TryParse(segments[1], out _))
            return $"tg://resolve?domain={resolvedDomain}&post={Uri.EscapeDataString(segments[1])}";

        return $"tg://resolve?domain={resolvedDomain}";
    }

    private static bool IsTelegramUri(Uri uri) =>
        uri.Scheme.Equals("tg", StringComparison.OrdinalIgnoreCase)
        || (uri.Host.Equals("t.me", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("telegram.me", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("telegram.dog", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".t.me", StringComparison.OrdinalIgnoreCase));

    private static string TelegramName(Uri uri)
    {
        if (uri.Scheme.Equals("tg", StringComparison.OrdinalIgnoreCase))
        {
            var domain = QueryValue(uri, "domain");
            if (!string.IsNullOrWhiteSpace(domain)) return "Telegram: " + domain.TrimStart('@');

            var id = QueryValue(uri, "id") ?? QueryValue(uri, "user_id");
            if (!string.IsNullOrWhiteSpace(id)) return "Telegram: " + id;

            return "Telegram";
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var firstSegment = segments.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstSegment)) return "Telegram";

        firstSegment = Uri.UnescapeDataString(firstSegment);
        if (firstSegment.StartsWith('+') || firstSegment.Equals("joinchat", StringComparison.OrdinalIgnoreCase))
            return "Telegram invite";
        if (firstSegment.Equals("c", StringComparison.OrdinalIgnoreCase))
            return segments.Length >= 3 ? "Telegram topic" : "Telegram chat";

        return "Telegram: " + firstSegment.TrimStart('@');
    }

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pieces[0]);
            if (!key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : "";
        }

        return null;
    }
}
