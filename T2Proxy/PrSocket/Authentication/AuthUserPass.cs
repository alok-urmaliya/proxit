

using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace T2Proxy.ProxySocket.Authentication;

internal sealed class AuthUserPass : AuthMethod
{
    private string password;

    private string username;

    public AuthUserPass(Socket server, string user, string pass) : base(server)
    {
        Username = user;
        Password = pass;
    }

    private string Username
    {
        get => username;
        set => username = value ?? throw new ArgumentNullException();
    }

    private string Password
    {
        get => password;
        set => password = value ?? throw new ArgumentNullException();
    }

    private void GetAuthenticationBytes(Memory<byte> buffer)
    {
        var span = buffer.Span;
        span[0] = 1;
        span[1] = (byte)Username.Length;
        Encoding.ASCII.GetBytes(Username).CopyTo(span.Slice(2));
        span[Username.Length + 2] = (byte)Password.Length;
        Encoding.ASCII.GetBytes(Password).CopyTo(span.Slice(Username.Length + 3));
    }

    private int GetAuthenticationLength()
    {
        return 3 + Username.Length + Password.Length;
    }

    public override void Authenticate()
    {
        var length = GetAuthenticationLength();
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            GetAuthenticationBytes(buffer);
            if (Server.Send(buffer, 0, length, SocketFlags.None) < length) throw new SocketException(10054);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var received = 0;
        while (received != 2)
        {
            var recv = Server.Receive(buffer, received, 2 - received, SocketFlags.None);
            if (recv == 0)
                throw new SocketException(10054);

            received += recv;
        }

        if (buffer[1] != 0)
        {
            Server.Close();
            throw new ServerException("Username/password combination rejected.");
        }
    }
    public override void BeginAuthenticate(HandShakeComplete callback)
    {
        var length = GetAuthenticationLength();
        Buffer = ArrayPool<byte>.Shared.Rent(length);
        GetAuthenticationBytes(Buffer);
        CallBack = callback;
        Server.BeginSend(Buffer, 0, length, SocketFlags.None, OnSent, Server);
    }
    private void OnSent(IAsyncResult ar)
    {
        try
        {
            if (Server.EndSend(ar) < GetAuthenticationLength())
                throw new SocketException(10054);

            Server.BeginReceive(Buffer, 0, 2, SocketFlags.None, OnReceive, Server);
        }
        catch (Exception e)
        {
            OnCallBack(e);
        }
    }
    private void OnReceive(IAsyncResult ar)
    {
        try
        {
            var recv = Server.EndReceive(ar);
            if (recv <= 0)
                throw new SocketException(10054);

            Received += recv;
            if (Received == 2)
                if (Buffer[1] == 0)
                    OnCallBack(null);
                else
                    throw new ServerException("Username/password combination not accepted.");
            else
                Server.BeginReceive(Buffer, Received, 2 - Received, SocketFlags.None,
                    OnReceive, Server);
        }
        catch (Exception e)
        {
            OnCallBack(e);
        }
    }

    private void OnCallBack(Exception? exception)
    {
        ArrayPool<byte>.Shared.Return(Buffer);
        CallBack(exception);
    }
}