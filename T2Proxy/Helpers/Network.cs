using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace T2Proxy.Helpers;

internal class NetworkHelper
{
    private static readonly string localhostName = Dns.GetHostName();
    private static readonly IPHostEntry localhostEntry = Dns.GetHostEntry(string.Empty);
    internal static bool IsLocalIpAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        return localhostEntry.AddressList.Contains(address);
    }

    internal static bool IsLocalIpAddress(string hostName, bool proxyDnsRequests = false)
    {
        if (IPAddress.TryParse(hostName, out var ipAddress)
            && IsLocalIpAddress(ipAddress))
            return true;

        if (hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;

        if (hostName.Equals(localhostName, StringComparison.OrdinalIgnoreCase)) return true;

        if (hostName.Equals(localhostEntry.HostName, StringComparison.OrdinalIgnoreCase)) return true;

        if (!proxyDnsRequests)
            try
            {
                var hostEntry = Dns.GetHostEntry(hostName);
                if (hostEntry.HostName.Equals(localhostEntry.HostName, StringComparison.OrdinalIgnoreCase)
                    || hostEntry.AddressList.Any(hostIp => localhostEntry.AddressList.Contains(hostIp)))
                    return true;
            }
            catch (SocketException)
            {
            }

        return false;
    }
}