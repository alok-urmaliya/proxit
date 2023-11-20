
using System;
using System.IO;
using T2Proxy.Models;

namespace T2Proxy.Http2.Hpack;

internal class HuffmanEncoder
{
    public static readonly HuffmanEncoder Instance = new();

    private readonly int[] codes = HpackUtil.HuffmanCodes;

    private readonly byte[] lengths = HpackUtil.HuffmanCodeLengths;

    public void Encode(BinaryWriter output, ByteStream data)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));

        if (data.Length == 0) return;

        var current = 0L;
        var n = 0;

        for (var i = 0; i < data.Length; i++)
        {
            var b = data.Span[i] & 0xFF;
            var code = (uint)codes[b];
            int nbits = lengths[b];

            current <<= nbits;
            current |= code;
            n += nbits;

            while (n >= 8)
            {
                n -= 8;
                output.Write((byte)(current >> n));
            }
        }

        if (n > 0)
        {
            current <<= 8 - n;
            current |= (uint)(0xFF >> n); 
            output.Write((byte)current);
        }
    }

    public int GetEncodedLength(ByteStream data)
    {
        var len = 0L;
        foreach (var b in data.Span) len += lengths[b];

        return (int)((len + 7) >> 3);
    }
}