using System;
using System.Net;

namespace T2Proxy.Models;


public class ExternalServer : IExternalServer
{
    private static readonly Lazy<NetworkCredential> defaultCredentials =
        new(() => CredentialCache.DefaultNetworkCredentials);

    private string? password;

    private string? userName;    
    public ExternalServer()
    {
    }
    
    public ExternalServer(string hostName, int port)
    {
        HostName = hostName;
        Port = port;
    }
    public ExternalServer(string hostName, int port, string userName, string password)
    {
        HostName = hostName;
        Port = port;
        UserName = userName;
        Password = password;
    }

    public bool UseDefaultCredentials { get; set; }

    public bool BypassLocalhost { get; set; }

    public ExternalProxyType ProxyType { get; set; }

    public bool ProxyDnsRequests { get; set; }

    public string? UserName
    {
        get => UseDefaultCredentials ? defaultCredentials.Value.UserName : userName;
        set
        {
            userName = value;

            if (defaultCredentials.Value.UserName != userName) UseDefaultCredentials = false;
        }
    }

    public string? Password
    {
        get => UseDefaultCredentials ? defaultCredentials.Value.Password : password;
        set
        {
            password = value;

            if (defaultCredentials.Value.Password != password) UseDefaultCredentials = false;
        }
    }

    public string HostName { get; set; } = string.Empty;

    public int Port { get; set; }

    public override string ToString()
    {
        return $"{HostName}:{Port}";
    }
}

public enum ExternalProxyType
{
    Http,
    Socks4,
    Socks5
}