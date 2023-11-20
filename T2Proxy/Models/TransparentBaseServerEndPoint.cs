using System.Net;
using System.Threading.Tasks;
using T2Proxy.EventArguments;

namespace T2Proxy.Models;

public abstract class TransparentBaseServerEndPoint : ServerEndPoint
{
    protected TransparentBaseServerEndPoint(IPAddress ipAddress, int port, bool decryptSsl) : base(ipAddress, port,
        decryptSsl)
    {
    }

    public abstract string GenericCertificateName { get; set; }

    internal abstract Task InvokeBeforeSslAuthenticate(ProxyServer proxyServer,
        BeforeSslAuthenticateEventArgs connectArgs, ExceptionHandler? exceptionFunc);
}