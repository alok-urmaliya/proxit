using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace T2Proxy.Helpers;

internal partial class NativeMethods
{
    internal const int AfInet = 2;
    internal const int AfInet6 = 23;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool sort, int ipVersion,
        int tableClass, int reserved);

    internal enum TcpTableType
    {
        BasicListener,
        BasicConnections,
        BasicAll,
        OwnerPidListener,
        OwnerPidConnections,
        OwnerPidAll,
        OwnerModuleListener,
        OwnerModuleConnections,
        OwnerModuleAll
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TcpRow
    {
        public TcpState state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public int owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Tcp6Row
    {
        public fixed byte localAddr[16];
        public uint localScopeId;
        public uint localPort;
        public fixed byte remoteAddr[16];
        public uint remoteScopeId;
        public uint remotePort;
        public TcpState state;
        public int owningPid;
    }
}