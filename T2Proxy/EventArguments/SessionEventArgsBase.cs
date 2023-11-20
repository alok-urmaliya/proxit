using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using T2Proxy.Helpers;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.Network.Tcp;
using T2Proxy.StreamExtended.BufferPool;
using T2Proxy.StreamExtended.Network;

namespace T2Proxy.EventArguments;

public abstract class SessionEventArgsBase : ServerEventArgsBase, IDisposable
{
    protected readonly IBufferPool BufferPool;

    internal readonly CancellationTokenSource CancellationTokenSource;
    protected readonly ExceptionHandler? ExceptionFunc;

    private bool disposed;
    private bool enableWinAuth;

    private protected SessionEventArgsBase(ProxyServer server, ServerEndPoint endPoint,
        HttpClientStream clientStream, ConnectRequest? connectRequest, Request request,
        CancellationTokenSource cancellationTokenSource) : base(server, clientStream.Connection)
    {
        BufferPool = server.BufferPool;
        ExceptionFunc = server.ExceptionFunc;
        TimeLine["Session Created"] = DateTime.UtcNow;

        CancellationTokenSource = cancellationTokenSource;

        ClientStream = clientStream;
        HttpClient = new HttpWebClient(connectRequest, request,
            new Lazy<int>(() => clientStream.Connection.GetProcessId(endPoint)));
        ProxyEndPoint = endPoint;
        EnableWinAuth = server.EnableWinAuth && IsWindowsAuthenticationSupported;
    }

    private static bool IsWindowsAuthenticationSupported => RunTime.IsWindows;

    internal TcpServerConnection ServerConnection => HttpClient.Connection;

    internal TcpClientConnection ClientConnection => ClientStream.Connection;

    internal HttpClientStream ClientStream { get; }

    public Guid ClientConnectionId => ClientConnection.Id;

    public Guid ServerConnectionId => HttpClient.HasConnection ? ServerConnection.Id : Guid.Empty;

    public Dictionary<string, DateTime> TimeLine { get; } = new();

    public object? UserData
    {
        get => HttpClient.UserData;
        set => HttpClient.UserData = value;
    }

    public bool EnableWinAuth
    {
        get => enableWinAuth;
        set
        {
            if (value && !IsWindowsAuthenticationSupported)
                throw new Exception("Windows Authentication is not supported");

            enableWinAuth = value;
        }
    }

    public bool IsHttps => HttpClient.Request.IsHttps;

    public IPEndPoint ClientLocalEndPoint => (IPEndPoint)ClientConnection.LocalEndPoint;

    public IPEndPoint ClientRemoteEndPoint => (IPEndPoint)ClientConnection.RemoteEndPoint;

    [Obsolete("Use ClientRemoteEndPoint instead.")]
    public IPEndPoint ClientEndPoint => ClientRemoteEndPoint;

    public HttpWebClient HttpClient { get; }

    [Obsolete("Use HttpClient instead.")] public HttpWebClient WebSession => HttpClient;
    public IExternalServer? CustomUpStreamProxy { get; set; }
    public IExternalServer? CustomUpStreamProxyUsed { get; internal set; }
    public ServerEndPoint ProxyEndPoint { get; }

    [Obsolete("Use ProxyEndPoint instead.")]
    public ServerEndPoint LocalEndPoint => ProxyEndPoint;
    public bool IsTransparent => ProxyEndPoint is TransparentServerEndPoint;
    public bool IsSocks => ProxyEndPoint is SocksServerEndPoint;
    public Exception? Exception { get; internal set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected void OnException(Exception exception)
    {
        ExceptionFunc?.Invoke(exception);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        if (disposing)
        {
            CustomUpStreamProxyUsed = null;

            HttpClient.FinishSession();
        }

        DataSent = null;
        DataReceived = null;
        Exception = null;

        disposed = true;
    }

    ~SessionEventArgsBase()
    {
#if DEBUG
            System.Diagnostics.Debugger.Break();
#endif

        Dispose(false);
    }
    public event EventHandler<DataEventArgs>? DataSent;

    public event EventHandler<DataEventArgs>? DataReceived;

    internal void OnDataSent(byte[] buffer, int offset, int count)
    {
        try
        {
            DataSent?.Invoke(this, new DataEventArgs(buffer, offset, count));
        }
        catch (Exception ex)
        {
            OnException(new Exception("Exception thrown in user event", ex));
        }
    }

    internal void OnDataReceived(byte[] buffer, int offset, int count)
    {
        try
        {
            DataReceived?.Invoke(this, new DataEventArgs(buffer, offset, count));
        }
        catch (Exception ex)
        {
            OnException(new Exception("Exception thrown in user event", ex));
        }
    }
    public void TerminateSession()
    {
        CancellationTokenSource.Cancel();
    }
}