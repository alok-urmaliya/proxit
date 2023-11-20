using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Extensions;

namespace T2Proxy.Models;

[DebuggerDisplay("Explicit: {IpAddress}:{Port}")]
public class ExplicitServerEndPoint : ServerEndPoint
{
    public ExplicitServerEndPoint(IPAddress ipAddress, int port, bool decryptSsl = true) : base(ipAddress, port,
        decryptSsl)
    {
    }

    internal bool IsSystemHttpProxy { get; set; }

    internal bool IsSystemHttpsProxy { get; set; }

    public event AsyncEventHandler<TunnelConnectSessionEventArgs>? BeforeTunnelConnectRequest;
  
    public event AsyncEventHandler<TunnelConnectSessionEventArgs>? BeforeTunnelConnectResponse;

    internal async Task InvokeBeforeTunnelConnectRequest(ProxyServer proxyServer,
        TunnelConnectSessionEventArgs connectArgs, ExceptionHandler? exceptionFunc)
    {
        if (BeforeTunnelConnectRequest != null)
            await BeforeTunnelConnectRequest.InvokeAsync(proxyServer, connectArgs, exceptionFunc);
    }

    internal async Task InvokeBeforeTunnelConnectResponse(ProxyServer proxyServer,
        TunnelConnectSessionEventArgs connectArgs, ExceptionHandler? exceptionFunc, bool isClientHello = false)
    {
        if (BeforeTunnelConnectResponse != null)
        {
            connectArgs.IsHttpsConnect = isClientHello;
            await BeforeTunnelConnectResponse.InvokeAsync(proxyServer, connectArgs, exceptionFunc);
        }
    }
}