using System.Threading;
using System.Threading.Tasks;

namespace T2Proxy.StreamExtended.Network;

public interface ILineStream
{
    bool DataAvailable { get; }

    ValueTask<bool> FillBufferAsync(CancellationToken cancellationToken = default);

    byte ReadByteFromBuffer();

    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);
}