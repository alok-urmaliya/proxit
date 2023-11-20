using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace T2Proxy.ProxySocket;
internal sealed class Socks4Handler : SocksHandler
{
    public Socks4Handler(Socket server, string user) : base(server, user)
    {
    }

    private int GetHostPortBytes(string host, int port, Memory<byte> buffer)
    {
        if (host == null)
            throw new ArgumentNullException(nameof(host));

        if (port <= 0 || port > 65535)
            throw new ArgumentException(nameof(port));

        var length = 10 + Username.Length + host.Length;
        Debug.Assert(buffer.Length >= length);

        var connect = buffer.Span;
        connect[0] = 4;
        connect[1] = 1;
        PortToBytes(port, connect.Slice(2));
        connect[4] = connect[5] = connect[6] = 0;
        connect[7] = 1;
        var userNameArray = Encoding.ASCII.GetBytes(Username);
        userNameArray.CopyTo(connect.Slice(8));
        connect[8 + Username.Length] = 0;
        Encoding.ASCII.GetBytes(host).CopyTo(connect.Slice(9 + Username.Length));
        connect[length - 1] = 0;
        return length;
    }

    private int GetEndPointBytes(IPEndPoint remoteEp, Memory<byte> buffer)
    {
        if (remoteEp == null)
            throw new ArgumentNullException(nameof(remoteEp));

        var length = 9 + Username.Length;
        Debug.Assert(buffer.Length >= length);

        var connect = buffer.Span;
        connect[0] = 4;
        connect[1] = 1;
        PortToBytes(remoteEp.Port, connect.Slice(2));
        remoteEp.Address.GetAddressBytes().CopyTo(connect.Slice(4));
        Encoding.ASCII.GetBytes(Username).CopyTo(connect.Slice(8));
        connect[length - 1] = 0;
        return length;
    }

    public override void Negotiate(string host, int port)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(10 + Username.Length + host.Length);
        try
        {
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
        var buffer = ArrayPool<byte>.Shared.Rent(9 + Username.Length);
        try
        {
            var length = GetEndPointBytes(remoteEp, buffer);
            Negotiate(buffer, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Negotiate(byte[] connect, int length)
    {
        if (connect == null)
            throw new ArgumentNullException(nameof(connect));

        if (length < 2)
            throw new ArgumentException(nameof(length));

        if (Server.Send(connect, 0, length, SocketFlags.None) < length)
            throw new SocketException(10054);

        ReadBytes(connect, 8);
        if (connect[1] != 90)
        {
            Server.Close();
            throw new ServerException("Negotiation failed.");
        }
    }

    public override AsyncServerResult BeginNegotiate(string host, int port, HandShakeComplete callback,
        IPEndPoint proxyEndPoint, object state)
    {
        ProtocolComplete = callback;
        Buffer = ArrayPool<byte>.Shared.Rent(10 + Username.Length + host.Length);
        BufferCount = GetHostPortBytes(host, port, Buffer);
        Server.BeginConnect(proxyEndPoint, OnConnect, Server);
        AsyncResult = new AsyncServerResult(state);
        return AsyncResult;
    }
    public override AsyncServerResult BeginNegotiate(IPEndPoint remoteEp, HandShakeComplete callback,
        IPEndPoint proxyEndPoint, object state)
    {
        ProtocolComplete = callback;
        Buffer = ArrayPool<byte>.Shared.Rent(9 + Username.Length);
        BufferCount = GetEndPointBytes(remoteEp, Buffer);
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
            Server.BeginSend(Buffer, 0, BufferCount, SocketFlags.None, OnSent, Server);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }
    private void OnSent(IAsyncResult ar)
    {
        try
        {
            HandleEndSend(ar, BufferCount);
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
            return;
        }

        try
        {
            BufferCount = 8;
            Received = 0;
            Server.BeginReceive(Buffer, 0, BufferCount, SocketFlags.None, OnReceive, Server);
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
            if (Received == 8)
            {
                if (Buffer[1] == 90)
                {
                    OnProtocolComplete(null);
                }
                else
                {
                    Server.Close();
                    OnProtocolComplete(new ServerException("Negotiation failed."));
                }
            }
            else
            {
                Server.BeginReceive(Buffer, Received, BufferCount - Received, SocketFlags.None, OnReceive,
                    Server);
            }
        }
        catch (Exception e)
        {
            OnProtocolComplete(e);
        }
    }
}