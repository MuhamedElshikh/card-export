using System.Net;
using AspNetIpNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace IdCardReader.Api;

/// <summary>Parses the reverse proxy networks that are allowed to set X-Forwarded-* headers.</summary>
public static class ProxyTrust
{
    /// <summary>
    /// Parses CIDR notation such as <c>172.16.0.0/12</c> or <c>fd00::/8</c>.
    /// A bare address (no prefix) is treated as a single host.
    /// </summary>
    public static bool TryParseNetwork(string? value, out AspNetIpNetwork network)
    {
        network = null!;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var address))
        {
            return false;
        }

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        var prefix = maxPrefix;

        if (parts.Length == 2 && !int.TryParse(parts[1], out prefix))
        {
            return false;
        }

        if (prefix < 0 || prefix > maxPrefix)
        {
            return false;
        }

        network = new AspNetIpNetwork(address, prefix);
        return true;
    }
}
