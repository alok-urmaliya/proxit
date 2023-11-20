using System;
using System.Collections.Generic;
using T2Proxy.EventArguments;
using T2Proxy.Models;

namespace T2Proxy.Exceptions;

public class ServerAuthorizationException : ServerException
{
   
    internal ServerAuthorizationException(string message, SessionEventArgsBase session, Exception innerException,
        IEnumerable<HttpHeader> headers) : base(message, innerException)
    {
        Session = session;
        Headers = headers;
    }

  
    public SessionEventArgsBase Session { get; }

    public IEnumerable<HttpHeader> Headers { get; }
}