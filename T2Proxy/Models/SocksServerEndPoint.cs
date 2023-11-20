using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Extensions;

namespace T2Proxy.Models;

[DebuggerDisplay("SOCKS: {IpAddress}:{Port}")]
public class SocksServerEndPoint : TransparentBaseServerEndPoint
{
    public SocksServerEndPoint(IPAddress ipAddress, int port, bool decryptSsl = true) : base(ipAddress, port,
        decryptSsl)
    {
        GenericCertificateName = "localhost";
    }

    public override string GenericCertificateName { get; set; }

    public event AsyncEventHandler<BeforeSslAuthenticateEventArgs>? BeforeSslAuthenticate;

    internal override async Task InvokeBeforeSslAuthenticate(ProxyServer proxyServer,
        BeforeSslAuthenticateEventArgs connectArgs, ExceptionHandler? exceptionFunc)
    {
        if (BeforeSslAuthenticate != null)
            await BeforeSslAuthenticate.InvokeAsync(proxyServer, connectArgs, exceptionFunc);
    }
}