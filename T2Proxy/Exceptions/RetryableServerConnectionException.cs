using System;

namespace T2Proxy.Exceptions;
public class RetryableServerConnectionException : ServerException
{
    internal RetryableServerConnectionException(string message) : base(message)
    {
    }

    internal RetryableServerConnectionException(string message, Exception e) : base(message, e)
    {
    }
}