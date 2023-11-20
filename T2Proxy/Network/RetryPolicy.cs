using System;
using System.Threading.Tasks;
using T2Proxy.Network.Tcp;

namespace T2Proxy.Network;

internal class RetryPolicy<T> where T : Exception
{
    private readonly int retries;
    private readonly TcpConnectionFactory tcpConnectionFactory;

    private TcpServerConnection? currentConnection;

    internal RetryPolicy(int retries, TcpConnectionFactory tcpConnectionFactory)
    {
        this.retries = retries;
        this.tcpConnectionFactory = tcpConnectionFactory;
    }

    internal async Task<RetryResult> ExecuteAsync(Func<TcpServerConnection, Task<bool>> action,
        Func<Task<TcpServerConnection>> generator, TcpServerConnection? initialConnection)
    {
        currentConnection = initialConnection;
        var @continue = true;
        Exception? exception = null;

        var attempts = retries;

        while (true)
        {
            currentConnection ??= await generator();

            try
            {
                @continue = await action(currentConnection);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            attempts--;

            if (attempts < 0 || exception == null || !(exception is T)) break;

            exception = null;

            await tcpConnectionFactory.Release(currentConnection, true);
            currentConnection = null;
        }

        return new RetryResult(currentConnection, exception, @continue);
    }
}

internal class RetryResult
{
    internal RetryResult(TcpServerConnection? lastConnection, Exception? exception, bool @continue)
    {
        LatestConnection = lastConnection;
        Exception = exception;
        Continue = @continue;
    }

    internal TcpServerConnection? LatestConnection { get; }

    internal Exception? Exception { get; }

    internal bool Continue { get; }
}