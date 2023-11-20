using System.Threading;
using System.Threading.Tasks;

namespace T2Proxy.StreamExtended.Network;

public interface IHttpStreamWriter
{
    bool IsNetworkStream { get; }

    void Write(byte[] buffer, int offset, int count);

    Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken);

    ValueTask WriteLineAsync(CancellationToken cancellationToken = default);

    ValueTask WriteLineAsync(string value, CancellationToken cancellationToken = default);
}