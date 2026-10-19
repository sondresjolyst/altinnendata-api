using System.Net;
using System.Net.Sockets;

namespace altinnendata_api.Helpers
{
    /// <summary>
    /// Truncates IP addresses to /24 (IPv4) or /48 (IPv6) before they are stored. Enough to show who
    /// accepted the terms and to spot fraud, without pointing at one household.
    /// </summary>
    public static class IpTruncator
    {
        public static string Truncate(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
            if (!IPAddress.TryParse(ip, out var addr)) return string.Empty;

            // Kestrel reports an IPv4 client on a dual-stack socket as ::ffff:a.b.c.d.
            if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();

            if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = addr.GetAddressBytes();
                bytes[3] = 0;
                return new IPAddress(bytes).ToString();
            }

            if (addr.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = addr.GetAddressBytes();
                for (int i = 6; i < bytes.Length; i++) bytes[i] = 0;
                return new IPAddress(bytes).ToString();
            }

            return string.Empty;
        }
    }
}
