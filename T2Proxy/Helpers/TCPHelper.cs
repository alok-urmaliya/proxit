using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Extensions;
using T2Proxy.StreamExtended.BufferPool;

namespace T2Proxy.Helpers;

internal class TCPHelper
{

    internal static unsafe int GetProcessIdByLocalPort(AddressFamily addressFamily, int localPort)
    {
        var tcpTable = IntPtr.Zero;
        var tcpTableLength = 0;

        var addressFamilyValue =
            addressFamily == AddressFamily.InterNetwork ? NativeMethods.AfInet : NativeMethods.AfInet6;
        const int allPid = (int)NativeMethods.TcpTableType.OwnerPidAll;

        if (NativeMethods.GetExtendedTcpTable(tcpTable, ref tcpTableLength, false, addressFamilyValue, allPid, 0) != 0)
            try
            {
                tcpTable = Marshal.AllocHGlobal(tcpTableLength);
                if (NativeMethods.GetExtendedTcpTable(tcpTable, ref tcpTableLength, true, addressFamilyValue, allPid,
                        0) == 0)
                {
                    var rowCount = *(int*)tcpTable;
                    var portInNetworkByteOrder = ToNetworkByteOrder((uint)localPort);

                    if (addressFamily == AddressFamily.InterNetwork)
                    {
                        var rowPtr = (NativeMethods.TcpRow*)(tcpTable + 4);

                        for (var i = 0; i < rowCount; ++i)
                        {
                            if (rowPtr->localPort == portInNetworkByteOrder) return rowPtr->owningPid;

                            rowPtr++;
                        }
                    }
                    else
                    {
                        var rowPtr = (NativeMethods.Tcp6Row*)(tcpTable + 4);

                        for (var i = 0; i < rowCount; ++i)
                        {
                            if (rowPtr->localPort == portInNetworkByteOrder) return rowPtr->owningPid;

                            rowPtr++;
                        }
                    }
                }
            }
            finally
            {
                if (tcpTable != IntPtr.Zero) Marshal.FreeHGlobal(tcpTable);
            }

        return 0;
    }

    private static uint ToNetworkByteOrder(uint port)
    {
        return ((port >> 8) & 0x00FF00FFu) | ((port << 8) & 0xFF00FF00u);
    }

    private static async Task SendRawTap(Stream clientStream, Stream serverStream, IBufferPool bufferPool,
        Action<byte[], int, int>? onDataSend, Action<byte[], int, int>? onDataReceive,
        CancellationTokenSource cancellationTokenSource)
    {

        var sendRelay =
            clientStream.CopyToAsync(serverStream, onDataSend, bufferPool, cancellationTokenSource.Token);
        var receiveRelay =
            serverStream.CopyToAsync(clientStream, onDataReceive, bufferPool, cancellationTokenSource.Token);

        await Task.WhenAny(sendRelay, receiveRelay);
        cancellationTokenSource.Cancel();

        await Task.WhenAll(sendRelay, receiveRelay);
    }

    internal static Task SendRaw(Stream clientStream, Stream serverStream, IBufferPool bufferPool,
        Action<byte[], int, int>? onDataSend, Action<byte[], int, int>? onDataReceive,
        CancellationTokenSource cancellationTokenSource,
        ExceptionHandler? exceptionFunc)
    {
        return SendRawTap(clientStream, serverStream, bufferPool, onDataSend, onDataReceive,
            cancellationTokenSource);
    }
}