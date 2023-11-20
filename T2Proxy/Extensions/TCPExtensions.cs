using System.Net.Sockets;

namespace T2Proxy.Extensions;

internal static class TCPExtensions
{
    internal static bool IsGoodConnection(this Socket socket)
    {
        if (!socket.Connected) return false;
        var blockingState = socket.Blocking;
        try
        {
            var tmp = new byte[1];

            socket.Blocking = false;
            socket.Send(tmp, 0, 0);
           
        }
        catch
        {
            return false;
        }
        finally
        {
            socket.Blocking = blockingState;
        }

        return true;
    }
}