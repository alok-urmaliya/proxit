

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace T2Proxy.ProxySocket;

internal sealed class SecuredProtocolHandler : SocksHandler
{
    private string password;

    private int receivedNewlineChars;

    public SecuredProtocolHandler(Socket server) : this(server, "")
    {
    }

    public SecuredProtocolHandler(Socket server, string user) : this(server, user, "")
    {
    }

    public SecuredProtocolHandler(Socket server, string user, string pass) : base(server, user)
    {
        Password = pass;
    }

    private string Password
    {
        get => password;
        set => password = value ?? throw new ArgumentNullException();
    }

    private byte[] GetConnectBytes(string host, int port)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format("CONNECT {0}:{1} HTTP/1.1", host, port));
        sb.AppendLine(string.Format("Host: {0}:{1}", host, port));
        if (!string.IsNullOrEmpty(Username))
        {
            var auth =
                Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", Username, Password)));
            sb.AppendLine(string.Format("Proxy-Authorization: Basic {0}", auth));
        }

        sb.AppendLine();
        var buffer = Encoding.ASCII.GetBytes(sb.ToString());
        return buffer;
    }

    private void VerifyConnectHeader(byte[] buffer, int length)
    {
        var header = Encoding.ASCII.GetString(buffer, 0, length);
        if (!header.StartsWith("HTTP/1.1 ", StringComparison.OrdinalIgnoreCase) &&
            !header.StartsWith("HTTP/1.0 ", StringComparison.OrdinalIgnoreCase) || !header.EndsWith(" "))
            throw new ProtocolViolationException();

        var code = header.Substring(9, 3);
        if (code != "200")
            throw new ServerException("Invalid HTTP status. Code: " + code);
    }

    public override void Negotiate(IPEndPoint remoteEp)
    {
        if (remoteEp == null)
            throw new ArgumentNullException();
        Negotiate(remoteEp.Address.ToString(), remoteEp.Port);
    }

    public override void Negotiate(string host, int port)
    {
        if (host == null)
            throw new ArgumentNullException();

        if (port <= 0 || port > 65535 || host.Length > 255)
            throw new ArgumentException();

        var buffer = GetConnectBytes(host, port);
        if (Server.Send(buffer, 0, buffer.Length, SocketFlags.None) < buffer.Length) throw new SocketException(10054);

        ReadBytes(buffer, 13);
        VerifyConnectHeader(buffer, 13);

        var receivedNewlineChars = 0;
        while (receivedNewlineChars < 4)
        {
            var recv = Server.Receive(buffer, 0, 1, SocketFlags.None);
            if (recv == 0) throw new SocketException(10054);

            var b = buffer[0];
            if (b == (receivedNewlineChars % 2 == 0 ? '\r' : '\n'))
                receivedNewlineChars++;
            else
                receivedNewlineChars = b == '\r' ? 1 : 0;
        }
    }
    public override AsyncServerResult BeginNegotiate(IPEndPoint remoteEp, HandShakeComplete callback,
        IPEndPoint proxyEndPoint, object state)
    {
        return BeginNegotiate(remoteEp.Address.ToString(), remoteEp.Port, callback, proxyEndPoint, state);
    }

    public override AsyncServerResult BeginNegotiate(string host, int port, HandShakeComplete callback,
        IPEndPoint proxyEndPoint, object state)
    {
        ProtocolComplete = callback;
        Buffer = GetConnectBytes(host, port);
        Server.BeginConnect(proxyEndPoint, OnConnect, Server);
        AsyncResult = new AsyncServerResult(state);
        return AsyncResult;
    }

    private void OnConnect(IAsyncResult ar)
    {
        try
        {
            Server.EndConnect(ar);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
            return;
        }

        try
        {
            Server.BeginSend(Buffer, 0, Buffer.Length, SocketFlags.None, OnConnectSent,
                null);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }

    private void OnConnectSent(IAsyncResult ar)
    {
        try
        {
            HandleEndSend(ar, Buffer.Length);
            Buffer = new byte[13];
            Received = 0;
            Server.BeginReceive(Buffer, 0, 13, SocketFlags.None, OnConnectReceive, Server);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }

    private void OnConnectReceive(IAsyncResult ar)
    {
        try
        {
            HandleEndReceive(ar);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
            return;
        }

        try
        {
            if (Received < 13)
            {
                Server.BeginReceive(Buffer, Received, 13 - Received, SocketFlags.None,
                    OnConnectReceive, Server);
            }
            else
            {
                VerifyConnectHeader(Buffer, 13);
                ReadUntilHeadersEnd(true);
            }
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }

    private void ReadUntilHeadersEnd(bool readFirstByte)
    {
        while (Server.Available > 0 && receivedNewlineChars < 4)
        {
            if (!readFirstByte)
            {
                readFirstByte = false;
            }
            else
            {
                var recv = Server.Receive(Buffer, 0, 1, SocketFlags.None);
                if (recv == 0)
                    throw new SocketException(10054);
            }

            if (Buffer[0] == (receivedNewlineChars % 2 == 0 ? '\r' : '\n'))
                receivedNewlineChars++;
            else
                receivedNewlineChars = Buffer[0] == '\r' ? 1 : 0;
        }

        if (receivedNewlineChars == 4)
            OnProtocolComplete(null);
        else
            Server.BeginReceive(Buffer, 0, 1, SocketFlags.None, OnEndHeadersReceive,
                Server);
    }

    private void OnEndHeadersReceive(IAsyncResult ar)
    {
        try
        {
            HandleEndReceive(ar);
            ReadUntilHeadersEnd(false);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }

    protected override void OnProtocolComplete(Exception? exception)
    {
        ProtocolComplete(exception);
    }
}