
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using T2Proxy.ProxySocket.Authentication;

namespace T2Proxy.ProxySocket;

internal sealed class Socks5Handler : SocksHandler
{
    private const int ConnectOffset = 4;

    private int handShakeLength;

    private string password = string.Empty;
    public Socks5Handler(Socket server) : this(server, "")
    {
    }

    public Socks5Handler(Socket server, string user) : this(server, user, "")
    {
    }

    public Socks5Handler(Socket server, string user, string pass) : base(server, user)
    {
        Password = pass;
    }

    private string Password
    {
        get => password;
        set => password = value ?? throw new ArgumentNullException();
    }

    private void Authenticate(byte[] buffer)
    {
        buffer[0] = 5;
        buffer[1] = 2;
        buffer[2] = 0;
        buffer[3] = 2;
        if (Server.Send(buffer, 0, 4, SocketFlags.None) < 4)
            throw new SocketException(10054);

        ReadBytes(buffer, 2);
        if (buffer[1] == 255)
            throw new ServerException("No authentication method accepted.");

        AuthMethod authenticate;
        switch (buffer[1])
        {
            case 0:
                authenticate = new AuthNone(Server);
                break;
            case 2:
                authenticate = new AuthUserPass(Server, Username, Password);
                break;
            default:
                throw new ProtocolViolationException();
        }

        authenticate.Authenticate();
    }

    private int GetHostPortBytes(string host, int port, Memory<byte> buffer)
    {
        if (host == null)
            throw new ArgumentNullException();

        if (port <= 0 || port > 65535 || host.Length > 255)
            throw new ArgumentException();

        var length = 7 + host.Length;
        if (buffer.Length < length)
            throw new ArgumentException(nameof(buffer));

        var connect = buffer.Span;
        connect[0] = 5;
        connect[1] = 1;
        connect[2] = 0;
        connect[3] = 3;
        connect[4] = (byte)host.Length;
        Encoding.ASCII.GetBytes(host).CopyTo(connect.Slice(5));
        PortToBytes(port, connect.Slice(host.Length + 5));
        return length;
    }

    private int GetEndPointBytes(IPEndPoint remoteEp, Memory<byte> buffer)
    {
        if (remoteEp == null)
            throw new ArgumentNullException();

        if (buffer.Length < 10)
            throw new ArgumentException(nameof(buffer));

        var connect = buffer.Span;
        connect[0] = 5;
        connect[1] = 1;
        connect[2] = 0; // reserved
        connect[3] = 1;
        remoteEp.Address.GetAddressBytes().CopyTo(connect.Slice(4));
        PortToBytes(remoteEp.Port, connect.Slice(8));
        return 10;
    }

    public override void Negotiate(string host, int port)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(258, 10 + host.Length + Username.Length + Password.Length));
        try
        {
            Authenticate(buffer);

            var length = GetHostPortBytes(host, port, buffer);
            Negotiate(buffer, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override void Negotiate(IPEndPoint remoteEp)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(258, 13 + Username.Length + Password.Length));
        try
        {
            Authenticate(buffer);

            var length = GetEndPointBytes(remoteEp, buffer);
            Negotiate(buffer, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Negotiate(byte[] buffer, int length)
    {
        if (Server.Send(buffer, 0, length, SocketFlags.None) < length)
            throw new SocketException(10054);

        ReadBytes(buffer, 4);
        if (buffer[1] != 0)
        {
            Server.Close();
            throw new ServerException(buffer[1]);
        }

        switch (buffer[3])
        {
            case 1:
                ReadBytes(buffer, 6); 
                break;
            case 3:
                ReadBytes(buffer, 1);
                ReadBytes(buffer, buffer[0] + 2);
                break;
            case 4:
                ReadBytes(buffer, 18);
                break;
            default:
                Server.Close();
                throw new ProtocolViolationException();
        }
    }

    public override AsyncServerResult BeginNegotiate(string host, int port, HandShakeComplete callback,
        IPEndPoint proxyEndPoint, object state)
    {
        ProtocolComplete = callback;
        Buffer = ArrayPool<byte>.Shared.Rent(Math.Max(258, 10 + host.Length + Username.Length + Password.Length));

      
        handShakeLength = GetHostPortBytes(host, port, Buffer.AsMemory(ConnectOffset));
        Server.BeginConnect(proxyEndPoint, OnConnect, Server);
        AsyncResult = new AsyncServerResult(state);
        return AsyncResult;
    }

    public override AsyncServerResult BeginNegotiate(IPEndPoint remoteEp, HandShakeComplete callback,
        IPEndPoint proxyEndPoint, object state)
    {
        ProtocolComplete = callback;
        Buffer = ArrayPool<byte>.Shared.Rent(Math.Max(258, 13 + Username.Length + Password.Length));

        handShakeLength = GetEndPointBytes(remoteEp, Buffer.AsMemory(ConnectOffset));
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
            Buffer[0] = 5;
            Buffer[1] = 2;
            Buffer[2] = 0;
            Buffer[3] = 2;
            Server.BeginSend(Buffer, 0, 4, SocketFlags.None, OnAuthSent,
                Server);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }
    private void OnAuthSent(IAsyncResult ar)
    {
        try
        {
            HandleEndSend(ar, 4);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
            return;
        }

        try
        {
            BufferCount = 2;
            Received = 0;
            Server.BeginReceive(Buffer, 0, BufferCount, SocketFlags.None, OnAuthReceive,
                Server);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }

    private void OnAuthReceive(IAsyncResult ar)
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
            if (Received < BufferCount)
            {
                Server.BeginReceive(Buffer, Received, BufferCount - Received, SocketFlags.None,
                    OnAuthReceive, Server);
            }
            else
            {
                AuthMethod authenticate;
                switch (Buffer[1])
                {
                    case 0:
                        authenticate = new AuthNone(Server);
                        break;
                    case 2:
                        authenticate = new AuthUserPass(Server, Username, Password);
                        break;
                    default:
                        OnProtocolComplete(new SocketException());
                        return;
                }

                authenticate.BeginAuthenticate(OnAuthenticated);
            }
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }

    private void OnAuthenticated(Exception e)
    {
        if (e != null)
        {
            OnProtocolComplete(e);
            return;
        }

        try
        {
            Server.BeginSend(Buffer, ConnectOffset, handShakeLength, SocketFlags.None, OnSent,
                Server);
        }
        catch (Exception ex)
        {
            OnProtocolComplete(ex);
        }
    }

    private void OnSent(IAsyncResult ar)
    {
        try
        {
            HandleEndSend(ar, BufferCount - ConnectOffset);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
            return;
        }

        try
        {
            BufferCount = 5;
            Received = 0;
            Server.BeginReceive(Buffer, 0, BufferCount, SocketFlags.None, OnReceive,
                Server);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }

    private void OnReceive(IAsyncResult ar)
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
            if (Received == BufferCount)
                ProcessReply(Buffer);
            else
                Server.BeginReceive(Buffer, Received, BufferCount - Received, SocketFlags.None,
                    OnReceive, Server);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }

    private void ProcessReply(byte[] buffer)
    {
        int lengthToRead;
        switch (buffer[3])
        {
            case 1:
                lengthToRead = 5;
                break;
            case 3:
                lengthToRead = buffer[4] + 2;
                break;
            case 4:
                lengthToRead = 17;
                break;
            default:
                throw new ProtocolViolationException();
        }

        Received = 0;
        BufferCount = lengthToRead;
        Server.BeginReceive(Buffer, 0, BufferCount, SocketFlags.None, OnReadLast, Server);
    }

    private void OnReadLast(IAsyncResult ar)
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
            if (Received == BufferCount)
                OnProtocolComplete(null);
            else
                Server.BeginReceive(Buffer, Received, BufferCount - Received, SocketFlags.None,
                    OnReadLast, Server);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }
}