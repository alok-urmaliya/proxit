
using System;
using System.IO;

namespace T2Proxy.Http2.Hpack;

public class HuffmanDecoder
{
    public static readonly HuffmanDecoder Instance = new();

    private readonly Node root;

 
    private HuffmanDecoder()
    {
        var codes = HpackUtil.HuffmanCodes;

        var lengths = HpackUtil.HuffmanCodeLengths;
        if (codes.Length != 257 || codes.Length != lengths.Length)
            throw new ArgumentException("invalid Huffman coding");

        root = BuildTree(codes, lengths);
    }

    public ReadOnlyMemory<byte> Decode(byte[] buf)
    {
        var resultBuf = new byte[buf.Length * 2];
        var resultSize = 0;
        var node = root;
        var current = 0;
        var bits = 0;
        for (var i = 0; i < buf.Length; i++)
        {
            int b = buf[i];
            current = (current << 8) | b;
            bits += 8;
            while (bits >= 8)
            {
                var c = (current >> (bits - 8)) & 0xFF;
                node = node.Children![c];
                bits -= node.Bits;
                if (node.IsTerminal)
                {
                    if (node.Symbol == HpackUtil.HuffmanEos) throw new IOException("EOS Decoded");

                    resultBuf[resultSize++] = (byte)node.Symbol;
                    node = root;
                }
            }
        }

        while (bits > 0)
        {
            var c = (current << (8 - bits)) & 0xFF;
            node = node.Children![c];
            if (node.IsTerminal && node.Bits <= bits)
            {
                bits -= node.Bits;
                resultBuf[resultSize++] = (byte)node.Symbol;
                node = root;
            }
            else
            {
                break;
            }
        }

        var mask = (1 << bits) - 1;
        if ((current & mask) != mask) throw new IOException("Invalid Padding");

        return resultBuf.AsMemory(0, resultSize);
    }

    private static Node BuildTree(int[] codes, byte[] lengths)
    {
        var root = new Node();
        for (var i = 0; i < codes.Length; i++) Insert(root, i, codes[i], lengths[i]);

        return root;
    }

    private static void Insert(Node root, int symbol, int code, byte length)
    {
        var current = root;
        while (length > 8)
        {
            if (current.IsTerminal) throw new InvalidDataException("invalid Huffman code: prefix not unique");

            length -= 8;
            var i = (code >> length) & 0xFF;
            if (current.Children![i] == null) current.Children[i] = new Node();

            current = current.Children[i];
        }

        var terminal = new Node(symbol, length);
        var shift = 8 - length;
        var start = (code << shift) & 0xFF;
        var end = 1 << shift;
        for (var i = start; i < start + end; i++) current.Children![i] = terminal;
    }

    private class Node
    {
        public Node()
        {
            Symbol = 0;
            Bits = 8;
            Children = new Node[256];
        }
        public Node(int symbol, int bits)
        {
            Symbol = symbol;
            Bits = bits;
            Children = null;
        }

        public int Symbol { get; }

        public int Bits { get; }
        public Node[]? Children { get; }

        public bool IsTerminal => Children == null;
    }
}