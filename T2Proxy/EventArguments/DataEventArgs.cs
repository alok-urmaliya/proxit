using System;

namespace T2Proxy.StreamExtended.Network;

public class DataEventArgs : EventArgs
{
    public DataEventArgs(byte[] buffer, int offset, int count)
    {
        Buffer = buffer;
        Offset = offset;
        Count = count;
    }

    public byte[] Buffer { get; }

    public int Offset { get; }

    public int Count { get; }
}