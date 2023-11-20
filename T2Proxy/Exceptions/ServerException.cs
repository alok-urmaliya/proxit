using System;

namespace T2Proxy.Exceptions;

public abstract class ServerException : Exception
{
 
    protected ServerException(string message) : base(message)
    {
    }

    protected ServerException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}