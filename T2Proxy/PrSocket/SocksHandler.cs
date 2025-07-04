﻿/*
    Copyright © 2002, The KPD-Team
    All rights reserved.
    http://www.mentalis.org/

  Redistribution and use in source and binary forms, with or without
  modification, are permitted provided that the following conditions
  are met:

    - Redistributions of source code must retain the above copyright
       notice, this list of conditions and the following disclaimer. 

    - Neither the name of the KPD-Team, nor the names of its contributors
       may be used to endorse or promote products derived from this
       software without specific prior written permission. 

  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
  "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
  LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
  FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL
  THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
  INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
  (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
  SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
  STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
  OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace T2Proxy.ProxySocket;

/// <summary>
///     References the callback method to be called when the protocol negotiation is completed.
/// </summary>
internal delegate void HandShakeComplete(Exception? error);

/// <summary>
///     Implements a specific version of the SOCKS protocol. This is an abstract class; it must be inherited.
/// </summary>
internal abstract class SocksHandler
{
    /// <summary>Holds the address of the method to call when the SOCKS protocol has been completed.</summary>
    protected HandShakeComplete ProtocolComplete;

    // private variables
    /// <summary>Holds the value of the Server property.</summary>
    private Socket server;

    /// <summary>Holds the value of the Username property.</summary>
    private string username = string.Empty;

    /// <summary>
    ///     Initializes a new instance of the SocksHandler class.
    /// </summary>
    /// <param name="server">The socket connection with the proxy server.</param>
    /// <param name="user">The username to use when authenticating with the server.</param>
    /// <exception cref="ArgumentNullException"><c>server</c> -or- <c>user</c> is null.</exception>
    public SocksHandler(Socket server, string user)
    {
        Server = server;
        Username = user;
    }

    /// <summary>
    ///     Gets or sets the socket connection with the proxy server.
    /// </summary>
    /// <value>A Socket object that represents the connection with the proxy server.</value>
    /// <exception cref="ArgumentNullException">The specified value is null.</exception>
    protected Socket Server
    {
        get => server;
        set => server = value ?? throw new ArgumentNullException();
    }

    /// <summary>
    ///     Gets or sets the username to use when authenticating with the proxy server.
    /// </summary>
    /// <value>A string that holds the username to use when authenticating with the proxy server.</value>
    /// <exception cref="ArgumentNullException">The specified value is null.</exception>
    protected string Username
    {
        get => username;
        set => username = value ?? throw new ArgumentNullException();
    }

    /// <summary>
    ///     Gets or sets the return value of the BeginConnect call.
    /// </summary>
    /// <value>An IAsyncProxyResult object that is the return value of the BeginConnect call.</value>
    protected AsyncServerResult AsyncResult { get; set; }

    /// <summary>
    ///     Gets or sets a byte buffer.
    /// </summary>
    /// <value>An array of bytes.</value>
    protected byte[] Buffer { get; set; }

    /// <summary>
    ///     Gets or sets actual data count in the buffer.
    /// </summary>
    protected int BufferCount { get; set; }

    /// <summary>
    ///     Gets or sets the number of bytes that have been received from the remote proxy server.
    /// </summary>
    /// <value>An integer that holds the number of bytes that have been received from the remote proxy server.</value>
    protected int Received { get; set; }

    /// <summary>
    ///     Converts a port number to an array of bytes.
    /// </summary>
    /// <param name="port">The port to convert.</param>
    /// <param name="buffer">The buffer which contains the result data.</param>
    /// <returns>An array of two bytes that represents the specified port.</returns>
    protected void PortToBytes(int port, Span<byte> buffer)
    {
        buffer[0] = (byte)(port / 256);
        buffer[1] = (byte)(port % 256);
    }

    /// <summary>
    ///     Converts an IP address to an array of bytes.
    /// </summary>
    /// <param name="address">The IP address to convert.</param>
    /// <returns>An array of four bytes that represents the specified IP address.</returns>
    protected byte[] AddressToBytes(long address)
    {
        var ret = new byte[4];
        ret[0] = (byte)(address % 256);
        ret[1] = (byte)(address / 256 % 256);
        ret[2] = (byte)(address / 65536 % 256);
        ret[3] = (byte)(address / 16777216);
        return ret;
    }

    /// <summary>
    ///     Reads a specified number of bytes from the Server socket.
    /// </summary>
    /// <param name="buffer">The result buffer.</param>
    /// <param name="count">The number of bytes to return.</param>
    /// <returns>An array of bytes.</returns>
    /// <exception cref="ArgumentException">The number of bytes to read is invalid.</exception>
    /// <exception cref="SocketException">An operating system error occurs while accessing the Socket.</exception>
    /// <exception cref="ObjectDisposedException">The Socket has been closed.</exception>
    protected void ReadBytes(byte[] buffer, int count)
    {
        if (count <= 0)
            throw new ArgumentException();

        var received = 0;
        while (received != count)
        {
            var recv = Server.Receive(buffer, received, count - received, SocketFlags.None);
            if (recv == 0) throw new SocketException(10054);

            received += recv;
        }
    }

    /// <summary>
    ///     Reads number of received bytes and ensures that socket was not shut down
    /// </summary>
    /// <param name="ar">IAsyncResult for receive operation</param>
    /// <returns></returns>
    protected void HandleEndReceive(IAsyncResult ar)
    {
        var recv = Server.EndReceive(ar);
        if (recv <= 0)
            throw new SocketException(10054);

        Received += recv;
    }

    /// <summary>
    ///     Verifies that whole buffer was sent successfully
    /// </summary>
    /// <param name="ar">IAsyncResult for receive operation</param>
    /// <param name="expectedLength">Length of buffer that was sent</param>
    /// <returns></returns>
    protected void HandleEndSend(IAsyncResult ar, int expectedLength)
    {
        if (Server.EndSend(ar) < expectedLength)
            throw new SocketException(10054);
    }

    protected virtual void OnProtocolComplete(Exception? exception)
    {
        if (Buffer != null) ArrayPool<byte>.Shared.Return(Buffer);

        ProtocolComplete(exception);
    }

    /// <summary>
    ///     Starts negotiating with a SOCKS proxy server.
    /// </summary>
    /// <param name="host">The remote server to connect to.</param>
    /// <param name="port">The remote port to connect to.</param>
    public abstract void Negotiate(string host, int port);

    /// <summary>
    ///     Starts negotiating with a SOCKS proxy server.
    /// </summary>
    /// <param name="remoteEp">The remote endpoint to connect to.</param>
    public abstract void Negotiate(IPEndPoint remoteEp);

    /// <summary>
    ///     Starts negotiating asynchronously with a SOCKS proxy server.
    /// </summary>
    /// <param name="remoteEp">An IPEndPoint that represents the remote device. </param>
    /// <param name="callback">The method to call when the connection has been established.</param>
    /// <param name="proxyEndPoint">The IPEndPoint of the SOCKS proxy server.</param>
    /// <param name="state">The state.</param>
    /// <returns>An IAsyncProxyResult that references the asynchronous connection.</returns>
    public abstract AsyncServerResult BeginNegotiate(IPEndPoint remoteEp, HandShakeComplete callback,
        IPEndPoint proxyEndPoint, object state);

    /// <summary>
    ///     Starts negotiating asynchronously with a SOCKS proxy server.
    /// </summary>
    /// <param name="host">The remote server to connect to.</param>
    /// <param name="port">The remote port to connect to.</param>
    /// <param name="callback">The method to call when the connection has been established.</param>
    /// <param name="proxyEndPoint">The IPEndPoint of the SOCKS proxy server.</param>
    /// <param name="state">The state.</param>
    /// <returns>An IAsyncProxyResult that references the asynchronous connection.</returns>
    public abstract AsyncServerResult BeginNegotiate(string host, int port, HandShakeComplete callback,
        IPEndPoint proxyEndPoint, object state);
}