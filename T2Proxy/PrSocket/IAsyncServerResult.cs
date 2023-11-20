

using System;
using System.Threading;

namespace T2Proxy.ProxySocket;

internal class AsyncServerResult : IAsyncResult
{
    private ManualResetEvent waitHandle;

    internal AsyncServerResult(object stateObject = null)
    {
        AsyncState = stateObject;
        IsCompleted = false;
        waitHandle?.Reset();
    }

    public bool IsCompleted { get; private set; }

    public bool CompletedSynchronously => false;

    public object AsyncState { get; }

    public WaitHandle AsyncWaitHandle => waitHandle ??= new ManualResetEvent(false);

    internal void Reset()
    {
        IsCompleted = true;
        waitHandle?.Set();
    }
}