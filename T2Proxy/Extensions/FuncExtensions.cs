using System;
using System.Threading.Tasks;
using T2Proxy.EventArguments;

namespace T2Proxy.Extensions;

internal static class FuncExtensions
{
    internal static async Task InvokeAsync<T>(this AsyncEventHandler<T> callback, object sender, T args,
        ExceptionHandler? exceptionFunc)
    {
        var invocationList = callback.GetInvocationList();

        foreach (var @delegate in invocationList)
            await InternalInvokeAsync((AsyncEventHandler<T>)@delegate, sender, args, exceptionFunc);
    }

    private static async Task InternalInvokeAsync<T>(AsyncEventHandler<T> callback, object sender, T args,
        ExceptionHandler? exceptionFunc)
    {
        try
        {
            await callback(sender, args);
        }
        catch (Exception e)
        {
            exceptionFunc?.Invoke(new Exception("Exception thrown in user event", e));
        }
    }
}