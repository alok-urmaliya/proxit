using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.StreamExtended.BufferPool;
using T2Proxy.StreamExtended.Models;
using T2Proxy.StreamExtended.Network;

namespace T2Proxy.StreamExtended;

internal class SslTools
{
    public static async Task<ClientHelloInfo?> PeekClientHello(IPeekStream clientStream, IBufferPool bufferPool,
        CancellationToken cancellationToken = default)
    {
        var recordType = await clientStream.PeekByteAsync(0, cancellationToken);
        if (recordType == -1) return null;

        if ((recordType & 0x80) == 0x80)
        {
            var peekStream = new PeekStreamReader(clientStream, 1);

            if (!await peekStream.EnsureBufferLength(10, cancellationToken)) return null;

            var recordLength = ((recordType & 0x7f) << 8) + peekStream.ReadByte();
            if (recordLength < 9)
                return null;

            if (peekStream.ReadByte() != 0x01)
                return null;

            int majorVersion = peekStream.ReadByte();
            int minorVersion = peekStream.ReadByte();

            var ciphersCount = peekStream.ReadInt16() / 3;
            var sessionIdLength = peekStream.ReadInt16();
            var randomLength = peekStream.ReadInt16();

            if (!await peekStream.EnsureBufferLength(ciphersCount * 3 + sessionIdLength + randomLength,
                    cancellationToken)) return null;

            var ciphers = new int[ciphersCount];
            for (var i = 0; i < ciphers.Length; i++)
                ciphers[i] = (peekStream.ReadByte() << 16) + (peekStream.ReadByte() << 8) + peekStream.ReadByte();

            var sessionId = peekStream.ReadBytes(sessionIdLength);
            var random = peekStream.ReadBytes(randomLength);

            var clientHelloInfo = new ClientHelloInfo(2, majorVersion, minorVersion, random, sessionId, ciphers,
                peekStream.Position);

            return clientHelloInfo;
        }

        if (recordType == 0x16)
        {
            var peekStream = new PeekStreamReader(clientStream, 1);

            if (!await peekStream.EnsureBufferLength(43, cancellationToken)) return null;

            int majorVersion = peekStream.ReadByte();
            int minorVersion = peekStream.ReadByte();

            var recordLength = peekStream.ReadInt16();

            if (peekStream.ReadByte() != 0x01)
                return null;

            var length = peekStream.ReadInt24();

            majorVersion = peekStream.ReadByte();
            minorVersion = peekStream.ReadByte();

            var random = peekStream.ReadBytes(32);
            length = peekStream.ReadByte();

            if (!await peekStream.EnsureBufferLength(length + 2, cancellationToken)) return null;

            var sessionId = peekStream.ReadBytes(length);

            length = peekStream.ReadInt16();
            if (!await peekStream.EnsureBufferLength(length + 1, cancellationToken)) return null;

            var ciphers = new int[length / 2];
            for (var i = 0; i < ciphers.Length; i++) ciphers[i] = peekStream.ReadInt16();

            length = peekStream.ReadByte();
            if (length < 1) return null;

            if (!await peekStream.EnsureBufferLength(length, cancellationToken)) return null;

            var compressionData = peekStream.ReadBytes(length);

            var extensionsStartPosition = peekStream.Position;

            Dictionary<string, SslExtension>? extensions = null;

            if (extensionsStartPosition < recordLength + 5)
                extensions = await ReadExtensions(majorVersion, minorVersion, peekStream, cancellationToken);

            var clientHelloInfo = new ClientHelloInfo(3, majorVersion, minorVersion, random, sessionId, ciphers,
                peekStream.Position)
            {
                ExtensionsStartPosition = extensionsStartPosition,
                CompressionData = compressionData,
                Extensions = extensions
            };

            return clientHelloInfo;
        }

        return null;
    }


    public static async Task<bool> IsServerHello(IPeekStream stream, IBufferPool bufferPool,
        CancellationToken cancellationToken)
    {
        var serverHello = await PeekServerHello(stream, bufferPool, cancellationToken);
        return serverHello != null;
    }
    public static async Task<ServerHelloInfo?> PeekServerHello(IPeekStream serverStream, IBufferPool bufferPool,
        CancellationToken cancellationToken = default)
    {

        var recordType = await serverStream.PeekByteAsync(0, cancellationToken);
        if (recordType == -1) return null;

        if ((recordType & 0x80) == 0x80)
        {
            var peekStream = new PeekStreamReader(serverStream, 1);
            if (!await peekStream.EnsureBufferLength(39, cancellationToken)) return null;

            var recordLength = ((recordType & 0x7f) << 8) + peekStream.ReadByte();
            if (recordLength < 38)
                return null;

            if (peekStream.ReadByte() != 0x04)
                return null;

            int majorVersion = peekStream.ReadByte();
            int minorVersion = peekStream.ReadByte();
            if (!await peekStream.EnsureBufferLength(35, cancellationToken)) return null;

            var random = peekStream.ReadBytes(32);
            var sessionId = peekStream.ReadBytes(1);
            var cipherSuite = peekStream.ReadInt16();

            var serverHelloInfo = new ServerHelloInfo(2, majorVersion, minorVersion, random, sessionId, cipherSuite,
                peekStream.Position);

            return serverHelloInfo;
        }

        if (recordType == 0x16)
        {
            var peekStream = new PeekStreamReader(serverStream, 1);
            if (!await peekStream.EnsureBufferLength(43, cancellationToken)) return null;

            int majorVersion = peekStream.ReadByte();
            int minorVersion = peekStream.ReadByte();

            var recordLength = peekStream.ReadInt16();

            if (peekStream.ReadByte() != 0x02)
                return null;

            var length = peekStream.ReadInt24();

            majorVersion = peekStream.ReadByte();
            minorVersion = peekStream.ReadByte();

            var random = peekStream.ReadBytes(32);
            length = peekStream.ReadByte();
            if (!await peekStream.EnsureBufferLength(length + 2 + 1, cancellationToken)) return null;

            var sessionId = peekStream.ReadBytes(length);

            var cipherSuite = peekStream.ReadInt16();
            var compressionMethod = peekStream.ReadByte();

            var extensionsStartPosition = peekStream.Position;

            Dictionary<string, SslExtension>? extensions = null;

            if (extensionsStartPosition < recordLength + 5)
                extensions = await ReadExtensions(majorVersion, minorVersion, peekStream, cancellationToken);

            var serverHelloInfo = new ServerHelloInfo(3, majorVersion, minorVersion, random, sessionId, cipherSuite,
                peekStream.Position)
            {
                CompressionMethod = compressionMethod,
                EntensionsStartPosition = extensionsStartPosition,
                Extensions = extensions
            };

            return serverHelloInfo;
        }

        return null;
    }

    private static async Task<Dictionary<string, SslExtension>?> ReadExtensions(int majorVersion, int minorVersion,
        PeekStreamReader peekStreamReader, CancellationToken cancellationToken)
    {
        Dictionary<string, SslExtension>? extensions = null;
        if (majorVersion > 3 || majorVersion == 3 && minorVersion >= 1)
            if (await peekStreamReader.EnsureBufferLength(2, cancellationToken))
            {
                var extensionsLength = peekStreamReader.ReadInt16();

                if (await peekStreamReader.EnsureBufferLength(extensionsLength, cancellationToken))
                {
                    var extensionsData = peekStreamReader.ReadBytes(extensionsLength).AsMemory();
                    extensions = new Dictionary<string, SslExtension>();
                    var idx = 0;
                    while (extensionsData.Length > 3)
                    {
                        var id = BinaryPrimitives.ReadInt16BigEndian(extensionsData.Span);
                        var length = BinaryPrimitives.ReadInt16BigEndian(extensionsData.Span.Slice(2));
                        var extension = new SslExtension(id, extensionsData.Slice(4, length), idx++);
                        extensions[extension.Name] = extension;
                        extensionsData = extensionsData.Slice(4 + length);
                    }
                }
            }

        return extensions;
    }
}