using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Extensions;
using T2Proxy.Models;
using T2Proxy.Network.Tcp;

namespace T2Proxy;

public partial class ProxyServer
{
    private async Task HandleClient(SocksServerEndPoint endPoint, TcpClientConnection clientConnection)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var stream = clientConnection.GetStream();
        var buffer = BufferPool.GetBuffer();
        var port = 0;
        try
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read < 3) return;

            if (buffer[0] == 4)
            {
                if (read < 9 || buffer[1] != 1)
                    return;

                port = (buffer[2] << 8) + buffer[3];

                buffer[0] = 0;
                buffer[1] = 90; 
                await stream.WriteAsync(buffer, 0, 8, cancellationToken);
            }
            else if (buffer[0] == 5)
            {
                int authenticationMethodCount = buffer[1];
                if (read < authenticationMethodCount + 2) return;

                var acceptedMethod = 255;
                for (var i = 0; i < authenticationMethodCount; i++)
                {
                    int method = buffer[i + 2];
                    if (method == 0 && ProxyBasicAuthenticateFunc == null)
                    {
                        acceptedMethod = 0;
                        break;
                    }

                    if (method == 2)
                    {
                        acceptedMethod = 2;
                        break;
                    }
                }

                buffer[1] = (byte)acceptedMethod;
                await stream.WriteAsync(buffer, 0, 2, cancellationToken);

                if (acceptedMethod == 255)
                    return;

                if (acceptedMethod == 2)
                {
                    read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (read < 3 || buffer[0] != 1)
                        return;

                    int userNameLength = buffer[1];
                    if (read < 3 + userNameLength) return;

                    var userName = Encoding.ASCII.GetString(buffer, 2, userNameLength);

                    int passwordLength = buffer[2 + userNameLength];
                    if (read < 3 + userNameLength + passwordLength) return;

                    var password = Encoding.ASCII.GetString(buffer, 3 + userNameLength, passwordLength);
                    var success = true;
                    if (ProxyBasicAuthenticateFunc != null)
                        success = await ProxyBasicAuthenticateFunc.Invoke(null, userName, password);

                    buffer[1] = success ? (byte)0 : (byte)1;
                    await stream.WriteAsync(buffer, 0, 2, cancellationToken);
                    if (!success) return;
                }

                read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read < 10 || buffer[1] != 1) return;

                int portIdx;
                switch (buffer[3])
                {
                    case 1:
                        portIdx = 8;
                        break;
                    case 3:
                        portIdx = buffer[4] + 5;

#if DEBUG
                            var hostname = new ByteStream(buffer.AsMemory(5, buffer[4]));
                            string hostnameStr = hostname.GetString();
#endif
                        break;
                    case 4:
                        portIdx = 20;
                        break;
                    default:
                        return;
                }

                if (read < portIdx + 2) return;

                port = (buffer[portIdx] << 8) + buffer[portIdx + 1];
                buffer[1] = 0; 
                await stream.WriteAsync(buffer, 0, read, cancellationToken);
            }
            else
            {
                return;
            }
        }
        finally
        {
            BufferPool.ReturnBuffer(buffer);
        }

        await HandleClient(endPoint, clientConnection, port, cancellationTokenSource, cancellationToken);
    }
}