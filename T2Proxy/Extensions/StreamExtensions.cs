using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.StreamExtended.BufferPool;

namespace T2Proxy.Extensions;
internal static class StreamExtensions
{
    internal static Task CopyToAsync(this Stream input, Stream output, Action<byte[], int, int> onCopy,
        IBufferPool bufferPool)
    {
        return CopyToAsync(input, output, onCopy, bufferPool, CancellationToken.None);
    }
    internal static async Task CopyToAsync(this Stream input, Stream output, Action<byte[], int, int>? onCopy,
        IBufferPool bufferPool, CancellationToken cancellationToken)
    {
        var buffer = bufferPool.GetBuffer();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var num = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    .WithCancellation(cancellationToken);
                int bytesRead;
                if ((bytesRead = num) != 0 && !cancellationToken.IsCancellationRequested)
                {
                    await output.WriteAsync(buffer, 0, bytesRead, CancellationToken.None);
                    onCopy?.Invoke(buffer, 0, bytesRead);
                }
                else
                {
                    break;
                }
            }
        }
        finally
        {
            bufferPool.ReturnBuffer(buffer);
        }
    }

    internal static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        where T : struct
    {
        var tcs = new TaskCompletionSource<bool>();
        using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task)) return default;
        }

        return await task;
    }
}