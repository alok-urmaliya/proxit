namespace T2Proxy.Models;

public interface IExternalServer
{
    bool UseDefaultCredentials { get; set; }
    bool BypassLocalhost { get; set; }
    ExternalProxyType ProxyType { get; set; }
    bool ProxyDnsRequests { get; set; }
    string? UserName { get; set; }
    string? Password { get; set; }
    string HostName { get; set; }
    int Port { get; set; }
    string ToString();
}