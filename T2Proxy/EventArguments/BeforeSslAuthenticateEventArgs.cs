using System.Threading;
using T2Proxy.Network.Tcp;

namespace T2Proxy.EventArguments;
public class BeforeSslAuthenticateEventArgs : ServerEventArgsBase
{
    internal readonly CancellationTokenSource TaskCancellationSource;

    internal BeforeSslAuthenticateEventArgs(ProxyServer server, TcpClientConnection clientConnection,
        CancellationTokenSource taskCancellationSource, string sniHostName) : base(server, clientConnection)
    {
        TaskCancellationSource = taskCancellationSource;
        SniHostName = sniHostName;
        ForwardHttpsHostName = sniHostName;
    }
    public string SniHostName { get; }

    public bool DecryptSsl { get; set; } = true;

    public string ForwardHttpsHostName { get; set; }

    public int ForwardHttpsPort { get; set; } = 443;

    public void TerminateSession()
    {
        TaskCancellationSource.Cancel();
    }
}