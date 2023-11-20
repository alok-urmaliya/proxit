using System;
using System.Net;
using System.Net.Sockets;

namespace T2Proxy.ProxySocket;
internal enum ProxyTypes
{
    None,
    Https,
    Socks4,
    Socks5
}
internal class ServerSocket : Socket
{
    private AsyncCallback callBack;
    private string proxyPass = string.Empty;
    private string proxyUser = string.Empty;
    public ServerSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType) : this(
        addressFamily, socketType, protocolType, "")
    {
    }
    public ServerSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType,
        string proxyUsername) : this(addressFamily, socketType, protocolType, proxyUsername, "")
    {
    }

    public ServerSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType,
        string proxyUsername, string proxyPassword) : base(addressFamily, socketType, protocolType)
    {
        ProxyUser = proxyUsername;
        ProxyPass = proxyPassword;
        ToThrow = new InvalidOperationException();
    }
    public IPEndPoint ProxyEndPoint { get; set; }
    public ProxyTypes ProxyType { get; set; } = ProxyTypes.None;
    private object State { get; set; }
    public string ProxyUser
    {
        get => proxyUser;
        set => proxyUser = value ?? throw new ArgumentNullException();
    }
    public string ProxyPass
    {
        get => proxyPass;
        set => proxyPass = value ?? throw new ArgumentNullException();
    }
    private AsyncServerResult AsyncResult { get; set; }
    private Exception? ToThrow { get; set; }
    private int RemotePort { get; set; }
    public new void Connect(IPAddress address, int port)
    {
        var remoteEp = new IPEndPoint(address, port);
        Connect(remoteEp);
    }
    public new void Connect(EndPoint remoteEp)
    {
        if (remoteEp == null)
            throw new ArgumentNullException("<remoteEP> cannot be null.");
        if (ProtocolType != ProtocolType.Tcp || ProxyType == ProxyTypes.None || ProxyEndPoint == null)
        {
            base.Connect(remoteEp);
        }
        else
        {
            base.Connect(ProxyEndPoint);
            if (ProxyType == ProxyTypes.Https)
                new SecuredProtocolHandler(this, ProxyUser, ProxyPass).Negotiate((IPEndPoint)remoteEp);
            else if (ProxyType == ProxyTypes.Socks4)
                new Socks4Handler(this, ProxyUser).Negotiate((IPEndPoint)remoteEp);
            else if (ProxyType == ProxyTypes.Socks5)
                new Socks5Handler(this, ProxyUser, ProxyPass).Negotiate((IPEndPoint)remoteEp);
        }
    }

    public new void Connect(string host, int port)
    {
        if (host == null)
            throw new ArgumentNullException(nameof(host));

        if (port <= 0 || port > 65535)
            throw new ArgumentException(nameof(port));

        if (ProtocolType != ProtocolType.Tcp || ProxyType == ProxyTypes.None || ProxyEndPoint == null)
        {
            base.Connect(new IPEndPoint(Dns.GetHostEntry(host).AddressList[0], port));
        }
        else
        {
            base.Connect(ProxyEndPoint);
            if (ProxyType == ProxyTypes.Https)
                new SecuredProtocolHandler(this, ProxyUser, ProxyPass).Negotiate(host, port);
            else if (ProxyType == ProxyTypes.Socks4)
                new Socks4Handler(this, ProxyUser).Negotiate(host, port);
            else if (ProxyType == ProxyTypes.Socks5)
                new Socks5Handler(this, ProxyUser, ProxyPass).Negotiate(host, port);
        }
    }

    public new IAsyncResult BeginConnect(IPAddress address, int port, AsyncCallback callback, object state)
    {
        var remoteEp = new IPEndPoint(address, port);
        return BeginConnect(remoteEp, callback, state);
    }

    public new IAsyncResult BeginConnect(EndPoint remoteEp, AsyncCallback callback, object state)
    {
        if (remoteEp == null)
            throw new ArgumentNullException();

        if (ProtocolType != ProtocolType.Tcp || ProxyType == ProxyTypes.None || ProxyEndPoint == null)
            return base.BeginConnect(remoteEp, callback, state);

        callBack = callback;
        if (ProxyType == ProxyTypes.Https)
        {
            AsyncResult = new SecuredProtocolHandler(this, ProxyUser, ProxyPass).BeginNegotiate((IPEndPoint)remoteEp,
                OnHandShakeComplete, ProxyEndPoint, state);
            return AsyncResult;
        }

        if (ProxyType == ProxyTypes.Socks4)
        {
            AsyncResult = new Socks4Handler(this, ProxyUser).BeginNegotiate((IPEndPoint)remoteEp,
                OnHandShakeComplete, ProxyEndPoint, state);
            return AsyncResult;
        }

        if (ProxyType == ProxyTypes.Socks5)
        {
            AsyncResult = new Socks5Handler(this, ProxyUser, ProxyPass).BeginNegotiate((IPEndPoint)remoteEp,
                OnHandShakeComplete, ProxyEndPoint, state);
            return AsyncResult;
        }

        return null;
    }

    public new IAsyncResult BeginConnect(string host, int port, AsyncCallback callback, object state)
    {
        if (host == null)
            throw new ArgumentNullException();
        if (port <= 0 || port > 65535)
            throw new ArgumentException();
        callBack = callback;
        if (ProtocolType != ProtocolType.Tcp || ProxyType == ProxyTypes.None || ProxyEndPoint == null)
        {
            RemotePort = port;
            AsyncResult = BeginDns(host, OnHandShakeComplete, state);
            return AsyncResult;
        }

        if (ProxyType == ProxyTypes.Https)
        {
            AsyncResult = new SecuredProtocolHandler(this, ProxyUser, ProxyPass).BeginNegotiate(host, port,
                OnHandShakeComplete, ProxyEndPoint, state);
            return AsyncResult;
        }

        if (ProxyType == ProxyTypes.Socks4)
        {
            AsyncResult = new Socks4Handler(this, ProxyUser).BeginNegotiate(host, port,
                OnHandShakeComplete, ProxyEndPoint, state);
            return AsyncResult;
        }

        if (ProxyType == ProxyTypes.Socks5)
        {
            AsyncResult = new Socks5Handler(this, ProxyUser, ProxyPass).BeginNegotiate(host, port,
                OnHandShakeComplete, ProxyEndPoint, state);
            return AsyncResult;
        }

        return null;
    }

    public new void EndConnect(IAsyncResult asyncResult)
    {
        if (asyncResult == null)
            throw new ArgumentNullException();

        if (!(asyncResult is AsyncServerResult))
        {
            base.EndConnect(asyncResult);
            return;
        }

        if (!asyncResult.IsCompleted)
            asyncResult.AsyncWaitHandle.WaitOne();
        if (ToThrow != null)
            throw ToThrow;
    }

    internal AsyncServerResult BeginDns(string host, HandShakeComplete callback, object state)
    {
        try
        {
            Dns.BeginGetHostEntry(host, OnResolved, this);
            return new AsyncServerResult(state);
        }
        catch
        {
            throw new SocketException();
        }
    }


    private void OnResolved(IAsyncResult asyncResult)
    {
        try
        {
            var dns = Dns.EndGetHostEntry(asyncResult);
            base.BeginConnect(new IPEndPoint(dns.AddressList[0], RemotePort), OnConnect,
                State);
        }
        catch (Exception e)
        {
            OnHandShakeComplete(e);
        }
    }

    /// <summary>
    ///     Called when the Socket is connected to the remote host.
    /// </summary>
    /// <param name="asyncResult">The result of the asynchronous operation.</param>
    private void OnConnect(IAsyncResult asyncResult)
    {
        try
        {
            base.EndConnect(asyncResult);
            OnHandShakeComplete(null);
        }
        catch (Exception e)
        {
            OnHandShakeComplete(e);
        }
    }

    /// <summary>
    ///     Called when the Socket has finished talking to the proxy server and is ready to relay data.
    /// </summary>
    /// <param name="error">The error to throw when the EndConnect method is called.</param>
    private void OnHandShakeComplete(Exception? error)
    {
        if (error != null)
            Close();

        ToThrow = error;
        AsyncResult.Reset();
        callBack?.Invoke(AsyncResult);
    }
}