using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace T2Proxy.Models;

public abstract class ServerEndPoint
{
  
    protected ServerEndPoint(IPAddress ipAddress, int port, bool decryptSsl)
    {
        IpAddress = ipAddress;
        Port = port;
        DecryptSsl = decryptSsl;
    }

    internal TcpListener? Listener { get; set; }

    public IPAddress IpAddress { get; }

    public int Port { get; internal set; }

    public bool DecryptSsl { get; }

    public X509Certificate2? GenericCertificate { get; set; }
}