using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Creates quick-access targets from dragged links such as tg:// and https://t.me/...</summary>
public static class LinkTargetService
{
    private static readonly Regex LaunchUri =
        new(@"\b(?:(?:https?://|tg://)[^\s<>'""]+|(?:t\.me|telegram\.me|telegram\.dog)/[^\s<>'""]+|[a-z0-9_]{2,64}\.t\.me(?:/[^\s<>'""]*)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SavedMessagesLabel =
        new(@"^\s*(?:saved messages?|избранное)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool HasLaunchUri(IDataObject data) => TryGetLaunchUri(data, out _);

    public static bool HasPotentialLaunchUriData(IDataObject data) =>
        data.GetDataPresent("UniformResourceLocatorW")
        || data.GetDataPresent("UniformResourceLocator")
        || data.GetDataPresent("text/x-moz-url")
        || data.GetDataPresent(DataFormats.Html)
        || TextDropService.HasText(data);

    public static TargetItem? CreateTarget(IDataObject data) =>
        TryGetLaunchUri(data, out var uriText) ? CreateTarget(uriText) : null;

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
        return new TargetItem { Name = NameFor(uriText), Path = uriText };
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

    private static bool TryGetLaunchUri(IDataObject data, out string uriText)
    {
        foreach (var text in TextCandidates(data))
        {
            if (TryExtractLaunchUri(text, out uriText)) return true;
        }

        uriText = "";
        return false;
    }

    private static IEnumerable<string> TextCandidates(IDataObject data)
    {
        if (ReadData(data, "UniformResourceLocatorW", Encoding.Unicode) is { } urlW) yield return urlW;
        if (ReadData(data, "UniformResourceLocator", Encoding.Default) is { } url) yield return url;
        if (ReadData(data, "text/x-moz-url", Encoding.Unicode) is { } moz) yield return FirstLine(moz);
        if (data.GetData(DataFormats.Html) is string html) yield return html;
        if (TextDropService.GetText(data) is { } text) yield return text;
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

    private static string TrimUri(string value) =>
        value.Trim().TrimEnd('\0', '.', ',', ';', '!', ')', ']', '}');

    private static string NormalizeUri(string value) =>
        IsBareTelegramLink(value) ? "https://" + value : value;

    private static bool IsBareTelegramLink(string value) =>
        value.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("telegram.me/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("telegram.dog/", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(value, @"^[a-z0-9_]{2,64}\.t\.me(?:/|$)", RegexOptions.IgnoreCase);

    private static string NameFor(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return uriText;
        if (IsTelegramUri(uri)) return TelegramName(uri);
        return string.IsNullOrWhiteSpace(uri.Host) ? uriText : uri.Host;
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
