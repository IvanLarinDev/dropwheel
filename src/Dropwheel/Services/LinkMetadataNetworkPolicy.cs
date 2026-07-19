using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Dropwheel.Services;

/// <summary>Allows automatic metadata requests only to public HTTP(S) endpoints. Every redirect is
/// checked again, and DNS names are accepted only when all resolved addresses are public.</summary>
internal sealed class LinkMetadataNetworkPolicy
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public LinkMetadataNetworkPolicy()
        : this((host, token) => Dns.GetHostAddressesAsync(host, token))
    {
    }

    internal LinkMetadataNetworkPolicy(Func<string, CancellationToken, Task<IPAddress[]>> resolve) =>
        _resolve = resolve;

    public async Task<bool> AllowsAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri
            || (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (IPAddress.TryParse(uri.DnsSafeHost, out var literal)) return IsPublicAddress(literal);

        IPAddress[] addresses;
        try { addresses = await _resolve(uri.DnsSafeHost, cancellationToken); }
        catch (SocketException) { return false; }

        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    /// <summary>Resolves and connects in one guarded step. This second check is intentional: DNS can
    /// change between the URL-policy check and the socket connection, so the transport itself must
    /// never connect to a private address.</summary>
    internal async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(endpoint.Host, out var literal))
            addresses = [literal];
        else
        {
            try { addresses = await _resolve(endpoint.Host, cancellationToken); }
            catch (SocketException ex)
            {
                throw new HttpRequestException($"Could not resolve metadata host '{endpoint.Host}'.", ex);
            }
        }

        var allowed = addresses.Where(IsPublicAddress).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException($"Metadata host '{endpoint.Host}' resolved to a blocked address.");

        Exception? lastError = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (ex is OperationCanceledException) throw;
                lastError = ex;
            }
        }

        throw new HttpRequestException($"Could not connect to metadata host '{endpoint.Host}'.", lastError);
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            return first != 0
                && first != 10
                && first != 127
                && !(first == 100 && second is >= 64 and <= 127)
                && !(first == 169 && second == 254)
                && !(first == 172 && second is >= 16 and <= 31)
                && !(first == 192 && second == 0 && bytes[2] is 0 or 2)
                && !(first == 192 && second == 168)
                && !(first == 198 && second is 18 or 19)
                && !(first == 198 && second == 51 && bytes[2] == 100)
                && !(first == 203 && second == 0 && bytes[2] == 113)
                && first < 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return false;
        if ((bytes[0] & 0xfe) == 0xfc) return false; // fc00::/7 unique-local
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
            return false; // documentation prefix
        return true;
    }
}
