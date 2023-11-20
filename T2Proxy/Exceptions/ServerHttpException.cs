using System;
using T2Proxy.EventArguments;

namespace T2Proxy.Exceptions;
public class ServerHttpException : ServerException
{
    internal ServerHttpException(string message, Exception? innerException, SessionEventArgs? session) : base(
        message, innerException)
    {
        Session = session;
    }

    public SessionEventArgs? Session { get; }
}