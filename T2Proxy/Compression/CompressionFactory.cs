using System;
using System.IO;
using System.IO.Compression;

namespace T2Proxy.Compression
{
    internal static class CompressionFactory
    {
        internal static Stream Create(HttpCompression type, Stream stream, bool leaveOpen = true)
        {
            return type switch
            {
                HttpCompression.Gzip => new GZipStream(stream, CompressionMode.Compress, leaveOpen),
                HttpCompression.Deflate => new DeflateStream(stream, CompressionMode.Compress, leaveOpen),
                HttpCompression.Brotli => new BrotliSharpLib.BrotliStream(stream, CompressionMode.Compress, leaveOpen),
                _ => throw new Exception($"Unsupported compression mode: {type}")
            };
        }
    }
}