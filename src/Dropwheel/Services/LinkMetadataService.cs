using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dropwheel.Models;

namespace Dropwheel.Services;

public sealed record LinkMetadataUpdate(string? Title, string? IconPath);

public static class LinkMetadataService
{
    private const int MaxHtmlBytes = 512 * 1024;
    private const int MaxIconBytes = 1024 * 1024;

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Dropwheel/1.0");
        return client;
    }

    public static async Task<LinkMetadataUpdate?> FetchAsync(TargetItem target, CancellationToken ct = default)
    {
        if (SourceUri(target) is not { } pageUri) return null;

        string? html = null;
        try { html = await FetchHtmlAsync(pageUri, ct); }
        catch (Exception ex) { ErrorLog.Write($"Could not fetch link metadata for '{pageUri}'", ex); }

        var title = html == null ? null : ExtractTitle(html);
        var iconUri = html == null ? new Uri(pageUri, "/favicon.ico") : ExtractIconUri(pageUri, html);
        var iconPath = iconUri == null ? null : await TryDownloadIconAsync(iconUri, ct);

        return string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(iconPath)
            ? null
            : new LinkMetadataUpdate(title, iconPath);
    }

    internal static Uri? SourceUri(TargetItem target)
    {
        var source = string.IsNullOrWhiteSpace(target.SourceUrl) ? target.Path : target.SourceUrl;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) && IsWebUri(uri) ? uri : null;
    }

    internal static string? ExtractTitle(string html)
    {
        var match = Regex.Match(html, @"(?is)<title[^>]*>(.*?)</title>");
        if (!match.Success) return null;

        var title = Regex.Replace(match.Groups[1].Value, "<[^>]+>", "");
        title = WebUtility.HtmlDecode(title);
        title = Regex.Replace(title, @"\s+", " ").Trim();
        return title.Length == 0 ? null : title;
    }

    internal static Uri? ExtractIconUri(Uri pageUri, string html)
    {
        foreach (Match match in Regex.Matches(html, @"(?is)<link\b[^>]*>"))
        {
            var tag = match.Value;
            var rel = AttributeValue(tag, "rel");
            if (rel == null || !rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => part.Contains("icon", StringComparison.OrdinalIgnoreCase)))
                continue;

            var href = AttributeValue(tag, "href");
            if (string.IsNullOrWhiteSpace(href)) continue;

            href = WebUtility.HtmlDecode(href);
            if (!Uri.TryCreate(pageUri, href, out var iconUri)) continue;
            if (IsUnsupportedIcon(iconUri, contentType: null)) continue;
            return iconUri;
        }

        return new Uri(pageUri, "/favicon.ico");
    }

    private static async Task<string> FetchHtmlAsync(Uri uri, CancellationToken ct)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var copy = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (copy.Length <= MaxHtmlBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0) break;
            copy.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(copy.ToArray());
    }

    private static async Task<string?> TryDownloadIconAsync(Uri iconUri, CancellationToken ct)
    {
        // Fast path: when the icon URL carries an unambiguous image extension its cache file name is
        // fully determined without the server's Content-Type, so a favicon downloaded on an earlier add
        // or capture is returned with no network round trip at all.
        if (CachedIconPath(iconUri) is { } cached) return cached;

        try
        {
            using var response = await Client.GetAsync(iconUri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (IsUnsupportedIcon(iconUri, contentType)) return null;

            var ext = IconExtension(iconUri, contentType);
            if (ext == null) return null;

            var path = IconPathFor(iconUri, ext);
            if (File.Exists(path)) return path;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bytes = await ReadLimitedAsync(stream, MaxIconBytes, ct);
            if (bytes.Length == 0) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Could not download favicon '{iconUri}'", ex);
            return null;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (copy.Length <= maxBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0) break;
            copy.Write(buffer, 0, read);
        }

        return copy.Length > maxBytes ? Array.Empty<byte>() : copy.ToArray();
    }

    private static string? AttributeValue(string tag, string name)
    {
        var quoted = Regex.Match(tag, $@"(?is)\b{name}\s*=\s*(['""])(.*?)\1");
        if (quoted.Success) return quoted.Groups[2].Value;

        var unquoted = Regex.Match(tag, $@"(?is)\b{name}\s*=\s*([^\s>]+)");
        return unquoted.Success ? unquoted.Groups[1].Value : null;
    }

    /// <summary>The on-disk path of an already-downloaded favicon for this URL, or null when nothing is
    /// cached or the URL extension is not by itself enough to name the file (only the server's
    /// Content-Type could, so a network fetch is still needed).</summary>
    internal static string? CachedIconPath(Uri iconUri)
    {
        var candidate = IconCachePathCandidate(iconUri);
        return candidate != null && File.Exists(candidate) ? candidate : null;
    }

    /// <summary>The path a favicon for this URL would be cached at, based solely on the URL extension,
    /// or null when the extension can't name the file. Does not check whether the file exists.</summary>
    internal static string? IconCachePathCandidate(Uri iconUri)
        => UrlIconExtension(iconUri) is { } ext ? IconPathFor(iconUri, ext) : null;

    /// <summary>The image extension the icon URL itself carries, or null when it names no known image
    /// type (the .ico default used elsewhere is deliberately not assumed here, since it would make the
    /// cache path guessable-but-wrong for extension-less URLs).</summary>
    private static string? UrlIconExtension(Uri iconUri)
    {
        var ext = Path.GetExtension(iconUri.AbsolutePath).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".ico" or ".webp" ? ext : null;
    }

    private static string IconPathFor(Uri iconUri, string extension)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(iconUri.AbsoluteUri)))[..24]
            .ToLowerInvariant();
        return Path.Combine(TargetStore.Dir, "icons", hash + extension);
    }

    private static string? IconExtension(Uri iconUri, string? contentType)
    {
        var byType = contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            "image/webp" => ".webp",
            _ => null,
        };
        if (byType != null) return byType;

        var ext = Path.GetExtension(iconUri.AbsolutePath).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".ico" or ".webp" ? ext : ".ico";
    }

    private static bool IsUnsupportedIcon(Uri iconUri, string? contentType)
    {
        if (contentType?.Contains("svg", StringComparison.OrdinalIgnoreCase) == true) return true;
        return Path.GetExtension(iconUri.AbsolutePath).Equals(".svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebUri(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
