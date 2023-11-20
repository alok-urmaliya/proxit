using System;
using T2Proxy.EventArguments;

namespace T2Proxy.Exceptions;

public class ServerConnectException : ServerException
{
 
    internal ServerConnectException(string message, Exception innerException, SessionEventArgsBase session) : base(
        message, innerException)
    {
        Session = session;
    }

    public SessionEventArgsBase Session { get; }
}