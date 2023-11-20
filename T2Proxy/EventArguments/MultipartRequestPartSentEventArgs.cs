using T2Proxy.Http;

namespace T2Proxy.EventArguments;

public class MultipartRequestPartSentEventArgs : ServerEventArgsBase
{
    internal MultipartRequestPartSentEventArgs(SessionEventArgs session, string boundary, HeaderCollection headers) :
        base(session.Server, session.ClientConnection)
    {
        Session = session;
        Boundary = boundary;
        Headers = headers;
    }

    public SessionEventArgs Session { get; }

    public string Boundary { get; }

    public HeaderCollection Headers { get; }
}